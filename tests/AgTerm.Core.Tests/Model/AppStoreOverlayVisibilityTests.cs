using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

// Hiding an overlay without closing it (upstream 7f450d17, #790). The port has only the
// session-wide program slot: no pane overlays and no pages.
public class AppStoreOverlayVisibilityTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-overlay-visibility-").FullName;
    private readonly List<ControlEventDraft> _events = new();
    private readonly AppStore _store;
    private readonly Session _session;

    public AppStoreOverlayVisibilityTests()
    {
        _store = new AppStore(persistence: new PersistenceStore(_dir),
                              controlEventSink: _events.Add,
                              stateDirectory: _dir);
        var workspaceId = _store.AddWorkspace("work").Id;
        _session = _store.AddSession(workspaceId, "/repo")!;
        _events.Clear();
    }

    public void Dispose()
    {
        _store.PaneFocusDebouncer.Cancel();
        Directory.Delete(_dir, recursive: true);
    }

    private int TreeChanges => _events.Count(e => e.Kind == ControlEventKind.TreeChanged);

    private ControlSessionNode Node() =>
        _store.ControlTree().Workspaces.SelectMany(w => w.Sessions).Single(s => s.Id == _session.Id.ToString("D").ToUpperInvariant());

    private void OpenOverlay()
    {
        Assert.True(_store.OpenOverlay(_session.Id, "htop"));
        _events.Clear();
    }

    [Fact]
    public void HideKeepsTheOverlayRunningAndShowBringsItBack()
    {
        OpenOverlay();

        Assert.Null(_store.ApplyOverlayVisibility(_session.Id, null, OverlayVisibilityMode.Hide));
        Assert.True(_session.OverlayActive);
        Assert.True(_session.OverlayHidden);
        Assert.False(_session.AnyOverlayShown(null));

        Assert.Null(_store.ApplyOverlayVisibility(_session.Id, null, OverlayVisibilityMode.Show));
        Assert.True(_session.OverlayActive);
        Assert.False(_session.OverlayHidden);
        Assert.True(_session.AnyOverlayShown(null));
    }

    [Fact]
    public void ToggleHidesAShownOverlayAndShowsAHiddenOne()
    {
        OpenOverlay();

        Assert.Null(_store.ApplyOverlayVisibility(_session.Id, null, OverlayVisibilityMode.Toggle));
        Assert.True(_session.OverlayHidden);

        Assert.Null(_store.ApplyOverlayVisibility(_session.Id, null, OverlayVisibilityMode.Toggle));
        Assert.False(_session.OverlayHidden);
    }

    [Fact]
    public void TreeChangedFiresOnlyWhenTheStateChanges()
    {
        OpenOverlay();

        _store.ApplyOverlayVisibility(_session.Id, null, OverlayVisibilityMode.Hide);
        Assert.Equal(1, TreeChanges);
        _store.ApplyOverlayVisibility(_session.Id, null, OverlayVisibilityMode.Hide);
        Assert.Equal(1, TreeChanges);
        _store.ApplyOverlayVisibility(_session.Id, null, OverlayVisibilityMode.Show);
        Assert.Equal(2, TreeChanges);
        _store.ApplyOverlayVisibility(_session.Id, null, OverlayVisibilityMode.Show);
        Assert.Equal(2, TreeChanges);
    }

    [Fact]
    public void ASessionWithoutAnOverlayHasNothingToHide()
    {
        foreach (var mode in Enum.GetValues<OverlayVisibilityMode>())
        {
            Assert.Equal(OverlayVisibilityFailure.NoOverlay, _store.ApplyOverlayVisibility(_session.Id, null, mode));
        }
        Assert.Equal(0, TreeChanges);
    }

    [Fact]
    public void AnUnknownSessionIsRefused()
    {
        Assert.Equal(OverlayVisibilityFailure.UnknownSession,
                     _store.ApplyOverlayVisibility(Guid.NewGuid(), null, OverlayVisibilityMode.Hide));
    }

    [Fact]
    public void AHudIsNeverHidden()
    {
        Assert.True(_store.OpenHud(_session.Id, new HudSpec("working"), null));
        _events.Clear();

        Assert.Equal(OverlayVisibilityFailure.NoOverlay,
                     _store.ApplyOverlayVisibility(_session.Id, null, OverlayVisibilityMode.Toggle));
        Assert.True(_session.HudActive);
        Assert.Equal(0, TreeChanges);
    }

    [Theory]
    [InlineData(OverlayPane.Left)]
    [InlineData(OverlayPane.Right)]
    public void APaneScopeHoldsNothingBecauseThePortHasNoPaneOverlays(OverlayPane pane)
    {
        OpenOverlay();

        Assert.Equal(OverlayVisibilityFailure.NoOverlay,
                     _store.ApplyOverlayVisibility(_session.Id, pane, OverlayVisibilityMode.Hide));
        Assert.False(_session.OverlayHidden);
        Assert.False(_session.AnyOverlayShown(pane));
    }

    [Fact]
    public void ClosingClearsTheHiddenStateAndANewOverlayStartsShown()
    {
        OpenOverlay();
        _store.ApplyOverlayVisibility(_session.Id, null, OverlayVisibilityMode.Hide);

        Assert.True(_store.CloseOverlay(_session.Id));
        Assert.False(_session.OverlayHidden);

        Assert.True(_store.OpenOverlay(_session.Id, "top"));
        Assert.False(_session.OverlayHidden);
        Assert.True(_session.AnyOverlayShown(null));
    }

    [Fact]
    public void AWaitOverlayStaysHiddenAfterItsProgramExits()
    {
        Assert.True(_store.OpenOverlay(_session.Id, "make", wait: true));
        _store.ApplyOverlayVisibility(_session.Id, null, OverlayVisibilityMode.Hide);

        _store.RecordOverlayExit(_session.Id, 2);

        Assert.True(_session.OverlayActive);
        Assert.True(_session.OverlayHidden);
        Assert.Equal(2, _session.OverlayExitCode);
    }

    [Fact]
    public void ResizingAHiddenOverlayKeepsItHidden()
    {
        OpenOverlay();
        _store.ApplyOverlayVisibility(_session.Id, null, OverlayVisibilityMode.Hide);

        Assert.True(_store.ResizeOverlay(_session.Id, 40));

        Assert.True(_session.OverlayHidden);
    }

    [Fact]
    public void TheTreeListsTheHiddenSlotAndKeepsReportingOccupancy()
    {
        OpenOverlay();
        Assert.Null(Node().HiddenOverlays);

        _store.ApplyOverlayVisibility(_session.Id, null, OverlayVisibilityMode.Hide);

        var node = Node();
        Assert.Equal(["session"], node.HiddenOverlays);
        Assert.True(node.Overlay);
    }

    [Fact]
    public void TheTreeOmitsHiddenOverlaysWhenNothingIsHidden()
    {
        OpenOverlay();

        Assert.DoesNotContain("hiddenOverlays", ControlJson.Serialize(_store.ControlTree()));
    }
}
