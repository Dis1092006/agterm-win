using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

// A local HTML file page in the session-wide overlay slot (upstream 11fe64eb,
// AppStore+HtmlOverlay.swift). The port has no pane overlays, so a pane never holds a page.
public class AppStoreHtmlOverlayTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-html-overlay-").FullName;
    private readonly List<ControlEventDraft> _events = new();
    private readonly List<Guid> _released = new();
    private readonly AppStore _store;
    private readonly Session _session;

    public AppStoreHtmlOverlayTests()
    {
        _store = new AppStore(persistence: new PersistenceStore(_dir),
                              controlEventSink: _events.Add,
                              stateDirectory: _dir);
        _store.HtmlOverlayReleased = _released.Add;
        var workspaceId = _store.AddWorkspace("work").Id;
        _session = _store.AddSession(workspaceId, "/repo")!;
        _events.Clear();
    }

    public void Dispose()
    {
        _store.PaneFocusDebouncer.Cancel();
        Directory.Delete(_dir, recursive: true);
    }

    private static HtmlOverlay Page(string? grant = null) =>
        new(new HtmlSource.File(@"C:\work\r.html", grant));

    private HtmlOverlay OpenPage(HtmlOverlay? page = null)
    {
        page ??= Page();
        Assert.Null(_store.OpenHtmlOverlay(_session.Id, page, sizePercent: null));
        return page;
    }

    private ControlSessionNode Node() =>
        _store.ControlTree().Workspaces.SelectMany(w => w.Sessions).Single(s => s.Id == _session.Id.ToString("D").ToUpperInvariant());

    [Fact]
    public void OpenPutsThePageInTheSlot()
    {
        var page = Page();
        Assert.Null(_store.OpenHtmlOverlay(_session.Id, page, sizePercent: 140, points: new OverlayPointSize(Width: 300),
                                           backgroundColor: "#102030"));

        Assert.True(_session.OverlayActive);
        Assert.Same(page, _session.HtmlOverlay);
        Assert.Equal(100, _session.OverlaySizePercent);
        Assert.Equal(new OverlayPointSize(Width: 300), _session.OverlayPoints);
        Assert.Equal("#102030", _session.OverlayBackgroundColor);
        Assert.Null(_session.OverlayCommand);
        Assert.Contains(_events, e => e.Kind == ControlEventKind.TreeChanged);
    }

    [Fact]
    public void OpenWithoutAPercentDropsThePoints()
    {
        Assert.Null(_store.OpenHtmlOverlay(_session.Id, Page(), sizePercent: null, points: new OverlayPointSize(Width: 300)));

        Assert.Null(_session.OverlaySizePercent);
        Assert.Equal(default, _session.OverlayPoints);
    }

    [Fact]
    public void OpenClearsAnEarlierExitCode()
    {
        _session.OverlayExitCode = 3;

        OpenPage();

        Assert.Null(_session.OverlayExitCode);
    }

    [Fact]
    public void OpenRefusesAnUnknownSessionAndAnOccupiedSlot()
    {
        Assert.Equal(HtmlOverlayOpenFailure.UnknownSession, _store.OpenHtmlOverlay(Guid.NewGuid(), Page(), sizePercent: null));
        Assert.True(_store.OpenOverlay(_session.Id, "htop"));

        Assert.Equal(HtmlOverlayOpenFailure.AlreadyOpen, _store.OpenHtmlOverlay(_session.Id, Page(), sizePercent: null));
        Assert.Null(_session.HtmlOverlay);
        Assert.Equal("no such session", HtmlOverlayOpenFailure.UnknownSession.Message());
        Assert.Equal("overlay already open", HtmlOverlayOpenFailure.AlreadyOpen.Message());
    }

    [Fact]
    public void APageTakesTheSlotFromAHud()
    {
        Assert.True(_store.OpenHud(_session.Id, new HudSpec("building"), null));

        OpenPage();

        Assert.False(_session.HudActive);
        Assert.True(_session.HtmlOverlayActive);
    }

    [Fact]
    public void AProgramCannotOpenOverAPage()
    {
        OpenPage();

        Assert.False(_store.OpenOverlay(_session.Id, "htop"));
        Assert.Null(_session.OverlayCommand);
    }

    [Fact]
    public void CommandFailuresNameWhatTheSlotHolds()
    {
        Assert.Equal(HtmlOverlayCommandFailure.UnknownSession, _store.HtmlOverlayCommandFailure(Guid.NewGuid(), null));
        Assert.Equal(HtmlOverlayCommandFailure.NoOverlay, _store.HtmlOverlayCommandFailure(_session.Id, null));
        Assert.True(_store.OpenHud(_session.Id, new HudSpec("building"), null));
        Assert.Equal(HtmlOverlayCommandFailure.NoOverlay, _store.HtmlOverlayCommandFailure(_session.Id, null));
        _store.CloseOverlay(_session.Id);
        Assert.True(_store.OpenOverlay(_session.Id, "htop"));
        Assert.Equal(HtmlOverlayCommandFailure.NotHtml, _store.HtmlOverlayCommandFailure(_session.Id, null));
        _store.CloseOverlay(_session.Id);
        OpenPage();
        Assert.Null(_store.HtmlOverlayCommandFailure(_session.Id, null));
        Assert.Equal(HtmlOverlayCommandFailure.NoOverlay, _store.HtmlOverlayCommandFailure(_session.Id, OverlayPane.Left));

        Assert.Equal("no such session", HtmlOverlayCommandFailure.UnknownSession.Message());
        Assert.Equal("no overlay", HtmlOverlayCommandFailure.NoOverlay.Message());
        Assert.Equal("the overlay is not an html page", HtmlOverlayCommandFailure.NotHtml.Message());
    }

    [Fact]
    public void ReloadOfTheOriginalDropsTheCurrentPage()
    {
        var page = OpenPage();
        _store.SetHtmlLoadState(page.Id, HtmlLoadState.Failed, "boom");
        _store.SetHtmlPage(page.Id, new HtmlPageInfo("file:///C:/work/next.html", "Next", CanGoBack: true, CanGoForward: false));

        Assert.Null(_store.ReloadHtmlOverlay(_session.Id, null));

        Assert.Equal(1, page.ReloadRevision);
        Assert.Equal(HtmlReloadTarget.Original, page.ReloadTarget);
        Assert.Equal(HtmlLoadState.Loading, page.LoadState);
        Assert.Null(page.LoadError);
        Assert.Null(page.Current);
    }

    [Fact]
    public void ReloadOfTheCurrentPageKeepsIt()
    {
        var page = OpenPage();
        var info = new HtmlPageInfo("file:///C:/work/next.html", "Next", CanGoBack: true, CanGoForward: false);
        _store.SetHtmlPage(page.Id, info);

        Assert.Null(_store.ReloadHtmlOverlay(_session.Id, null, HtmlReloadTarget.Current));

        Assert.Equal(HtmlReloadTarget.Current, page.ReloadTarget);
        Assert.Equal(info, page.Current);
    }

    [Fact]
    public void ReloadAnswersTheCommandFailure()
    {
        Assert.Equal(HtmlOverlayCommandFailure.NoOverlay, _store.ReloadHtmlOverlay(_session.Id, null));
        Assert.True(_store.OpenOverlay(_session.Id, "htop"));
        Assert.Equal(HtmlOverlayCommandFailure.NotHtml, _store.ReloadHtmlOverlay(_session.Id, null));
    }

    [Fact]
    public void ReloadTellsTheAppWhichPageToReload()
    {
        var reloaded = new List<HtmlOverlay>();
        _store.HtmlOverlayReloaded = reloaded.Add;

        Assert.NotNull(_store.ReloadHtmlOverlay(_session.Id, null));
        var page = OpenPage();
        Assert.Null(_store.ReloadHtmlOverlay(_session.Id, null, HtmlReloadTarget.Current));

        Assert.Same(page, Assert.Single(reloaded));
        Assert.Equal(1, page.ReloadRevision);
    }

    [Fact]
    public void SlotFindsThePageByIdUntilItCloses()
    {
        var page = OpenPage();

        Assert.Same(_session, _store.HtmlOverlaySlot(page.Id));
        Assert.Null(_store.HtmlOverlaySlot(Guid.NewGuid()));
        Assert.True(_store.CloseHtmlOverlay(page.Id));
        Assert.Null(_store.HtmlOverlaySlot(page.Id));
        Assert.False(_store.CloseHtmlOverlay(page.Id));
    }

    [Fact]
    public void CloseReleasesThePageOnceAndRecordsNoExitCode()
    {
        var page = OpenPage();

        Assert.True(_store.CloseOverlay(_session.Id));
        Assert.False(_store.CloseOverlay(_session.Id));

        Assert.Equal([page.Id], _released);
        Assert.Null(_session.HtmlOverlay);
        Assert.False(_session.OverlayActive);
        Assert.Null(_session.OverlayExitCode);
    }

    [Fact]
    public void ClosingAProgramReleasesNothing()
    {
        Assert.True(_store.OpenOverlay(_session.Id, "htop"));

        Assert.True(_store.CloseOverlay(_session.Id));

        Assert.Empty(_released);
    }

    [Fact]
    public void ClosingTheSessionReleasesThePage()
    {
        var page = OpenPage();

        _store.CloseSession(_session.Id);

        Assert.Equal([page.Id], _released);
        Assert.Null(_session.HtmlOverlay);
    }

    [Fact]
    public void RemovingTheWorkspaceReleasesThePage()
    {
        var page = OpenPage();
        _store.AddWorkspace("other");

        _store.RemoveWorkspace(_store.Workspaces.Single(w => w.Sessions.Contains(_session)).Id);

        Assert.Equal([page.Id], _released);
    }

    [Fact]
    public void AnUndoableCloseReleasesThePageOnlyWhenFinal()
    {
        var page = OpenPage();
        _store.AddSession(_store.Workspaces[^1].Id, "/repo");
        Assert.True(_store.SoftCloseSession(_session.Id));
        Assert.Empty(_released);

        _store.SetHtmlLoadState(page.Id, HtmlLoadState.Loaded, null);
        Assert.Equal(HtmlLoadState.Loaded, page.LoadState);

        _store.FinalizeAllPendingCloses();

        Assert.Equal([page.Id], _released);
    }

    [Fact]
    public void TheTreeReportsTheFilePage()
    {
        var page = Page(grant: @"C:\work");
        page.Navigation = true;
        OpenPage(page);
        _store.SetHtmlLoadState(page.Id, HtmlLoadState.Loaded, null);
        _store.SetHtmlPage(page.Id, new HtmlPageInfo("file:///C:/work/r.html", "Report", CanGoBack: false, CanGoForward: false));

        var node = Node();

        Assert.True(node.Overlay);
        var html = Assert.Single(node.HtmlOverlays!);
        var expected = "{\"file\":\"C:\\\\work\\\\r.html\",\"cwd\":\"C:\\\\work\",\"state\":\"loaded\"," +
                       "\"page\":\"file:///C:/work/r.html\",\"title\":\"Report\",\"canGoBack\":false,\"canGoForward\":false," +
                       "\"navigation\":true,\"javascript\":false,\"chromeless\":false,\"persistent\":false,\"browse\":false," +
                       $"\"id\":\"{page.Id.ToString("D").ToUpperInvariant()}\"}}";
        Assert.Equal(expected, ControlJson.Serialize(html));
    }

    [Fact]
    public void TheTreeReportsAHiddenFreshPage()
    {
        var page = OpenPage();
        page.Javascript = true;
        _store.SetHtmlLoadState(page.Id, HtmlLoadState.Failed, "not found");
        Assert.Null(_store.SetOverlaysHidden(_session.Id, null, hidden: true));

        var json = ControlJson.Serialize(Assert.Single(Node().HtmlOverlays!));

        Assert.Equal("{\"file\":\"C:\\\\work\\\\r.html\",\"state\":\"failed\",\"error\":\"not found\"," +
                     "\"javascript\":true,\"chromeless\":false,\"persistent\":false,\"browse\":false," +
                     $"\"id\":\"{page.Id.ToString("D").ToUpperInvariant()}\",\"hidden\":true}}", json);
    }

    [Fact]
    public void TheTreeOmitsHtmlOverlaysWithoutAPage()
    {
        Assert.True(_store.OpenOverlay(_session.Id, "htop"));

        Assert.Null(Node().HtmlOverlays);
        Assert.DoesNotContain("htmlOverlays", ControlJson.Serialize(_store.ControlTree()));
    }

    [Fact]
    public void HtmlOverlaysComeJustBeforeHiddenOverlays()
    {
        OpenPage();
        Assert.Null(_store.SetOverlaysHidden(_session.Id, null, hidden: true));

        var json = ControlJson.Serialize(_store.ControlTree());

        Assert.Contains("}],\"hiddenOverlays\":[\"session\"]", json, StringComparison.Ordinal);
        Assert.True(json.IndexOf("\"htmlOverlays\"", StringComparison.Ordinal) < json.IndexOf("\"hiddenOverlays\"", StringComparison.Ordinal));
    }
}
