using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

// tree.changed on an overlay slot change (upstream 8c5ac7a0) and pane.focused (upstream e958477b)
public class AppStoreOverlayAndPaneEventsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-overlay-events-").FullName;
    private readonly List<ControlEventDraft> _events = new();
    private readonly AppStore _store;
    private readonly Session _session;
    private readonly Guid _workspaceId;

    public AppStoreOverlayAndPaneEventsTests()
    {
        _store = new AppStore(persistence: new PersistenceStore(_dir),
                              controlEventSink: _events.Add,
                              stateDirectory: _dir);
        _workspaceId = _store.AddWorkspace("work").Id;
        _session = _store.AddSession(_workspaceId, "/repo")!;
        _events.Clear();
    }

    public void Dispose()
    {
        _store.PaneFocusDebouncer.Cancel();
        Directory.Delete(_dir, recursive: true);
    }

    private int TreeChanges => _events.Count(e => e.Kind == ControlEventKind.TreeChanged);

    private ControlEventDraft[] PaneFocused() =>
        _events.Where(e => e.Kind == ControlEventKind.PaneFocused).ToArray();

    private void Settle() => _store.PaneFocusDebouncer.Flush();

    private void SplitShown()
    {
        _store.ToggleSplit(_session.Id);
        Settle();
        _events.Clear();
    }

    [Fact]
    public void OpeningAndClosingAnOverlaySchedulesTreeChanged()
    {
        Assert.True(_store.OpenOverlay(_session.Id, "htop"));
        Assert.Equal(1, TreeChanges);

        Assert.True(_store.CloseOverlay(_session.Id));
        Assert.Equal(2, TreeChanges);
    }

    [Fact]
    public void ARefusedOrNoOpOverlayChangeStaysSilent()
    {
        _store.OpenOverlay(_session.Id, "htop");
        _events.Clear();

        Assert.False(_store.OpenOverlay(_session.Id, "top"));
        Assert.True(_store.ResizeOverlay(_session.Id, 50));
        _store.CloseOverlay(_session.Id);
        _events.Clear();
        Assert.False(_store.CloseOverlay(_session.Id));
        Assert.False(_store.OpenOverlay(Guid.NewGuid(), "htop"));

        Assert.Equal(0, TreeChanges);
    }

    [Fact]
    public void OpeningAndClosingAHudSchedulesTreeChangedButAnUpdateDoesNot()
    {
        Assert.True(_store.OpenHud(_session.Id, new HudSpec("working"), null));
        Assert.Equal(1, TreeChanges);

        Assert.True(_store.UpdateHud(_session.Id, new HudSpec("still working"), null));
        Assert.True(_store.ResizeHud(_session.Id, 40));
        Assert.Equal(1, TreeChanges);

        Assert.True(_store.CloseHud(_session.Id));
        Assert.Equal(2, TreeChanges);

        Assert.False(_store.CloseHud(_session.Id));
        Assert.Equal(2, TreeChanges);
    }

    [Fact]
    public void AProgramTakingTheSlotFromAHudSchedulesTreeChanged()
    {
        _store.OpenHud(_session.Id, new HudSpec("working"), null);
        _events.Clear();

        Assert.True(_store.OpenOverlay(_session.Id, "htop"));

        Assert.True(TreeChanges >= 1);
    }

    [Fact]
    public void AHudRefusedByAProgramOverlayStaysSilent()
    {
        _store.OpenOverlay(_session.Id, "htop");
        _events.Clear();

        Assert.False(_store.OpenHud(_session.Id, new HudSpec("working"), null));

        Assert.Equal(0, TreeChanges);
    }

    [Fact]
    public void ClosingEveryHudOnWindowCloseDoesNotSignalTheTree()
    {
        _store.OpenHud(_session.Id, new HudSpec("working"), null);
        _events.Clear();

        _store.CloseAllHuds();

        Assert.Equal(0, TreeChanges);
        Assert.False(_session.HudActive);
    }

    [Fact]
    public void SelectingTheOtherPaneReportsPaneFocusedWithTheSessionAndPane()
    {
        SplitShown();

        _session.SplitFocused = false;
        Settle();

        var focused = Assert.Single(PaneFocused());
        Assert.Equal(_session.Id.ToString("D").ToUpperInvariant(), focused.Session);
        Assert.Equal(_workspaceId.ToString("D").ToUpperInvariant(), focused.Workspace);
        Assert.Equal(_session.DisplayName, focused.Payload.Name);
        Assert.Equal("left", focused.Payload.Pane);
    }

    [Fact]
    public void PaneFocusedCarriesThePaneTokenOnceTheSurfaceExists()
    {
        SplitShown();
        _session.SplitFocused = false;
        Settle();
        Assert.Null(Assert.Single(PaneFocused()).Payload.PaneId);

        _events.Clear();
        _session.Surface = new FakeSurface("tok-left");
        _session.SplitSurface = new FakeSurface("tok-right");
        _session.SplitFocused = true;
        Settle();

        var focused = Assert.Single(PaneFocused());
        Assert.Equal("right", focused.Payload.Pane);
        Assert.Equal("tok-right", focused.Payload.PaneId);
    }

    [Fact]
    public void AMoveAndBackInsideTheDebounceWindowIsNotReported()
    {
        SplitShown();

        _session.SplitFocused = false;
        _session.SplitFocused = true;
        Settle();

        Assert.Empty(PaneFocused());
    }

    [Fact]
    public void ASwapKeepsTheSelectedTerminalAndIsNotReported()
    {
        SplitShown();

        _session.SwapPaneIdentities();
        _session.SplitFocused = !_session.SplitFocused;
        Settle();

        Assert.Empty(PaneFocused());
    }

    [Fact]
    public void ANewSplitFocusingTheRightPaneIsReported()
    {
        _store.ToggleSplit(_session.Id);
        Settle();

        var focused = Assert.Single(PaneFocused());
        Assert.Equal("right", focused.Payload.Pane);
    }

    [Fact]
    public void ACreatedSessionOnlyUpdatesTheBaseline()
    {
        var created = _store.AddSession(_workspaceId, "/other")!;
        _events.Clear();

        created.SplitFocused = true;
        Settle();
        Assert.Empty(PaneFocused());

        created.SplitFocused = false;
        Settle();
        Assert.Empty(PaneFocused());
    }

    [Fact]
    public void ASettledDebounceRunsOnTheHostScheduler()
    {
        SplitShown();
        Action? scheduled = null;
        _store.PendingCloseFinalizationScheduler = action => scheduled = action;

        _session.SplitFocused = false;
        Settle();

        Assert.Empty(PaneFocused());
        Assert.NotNull(scheduled);
        scheduled!();
        Assert.Single(PaneFocused());
    }

    private sealed class FakeSurface : ITerminalSurface
    {
        public FakeSurface(string token) => PaneToken = token;
        public string? PaneToken { get; }
        public void Teardown() { }
        public void PromoteToPrimaryPane() { }
    }
}
