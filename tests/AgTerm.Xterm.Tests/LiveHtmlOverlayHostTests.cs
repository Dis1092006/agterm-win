using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm.Tests;

// What the live host gives the app for a page in the overlay slot: its release and its reloads as
// events, the page in the selection snapshot, the page reports into the store, and the control API's
// navigate through the app's web view.
public sealed class LiveHtmlOverlayHostTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-live-html-").FullName;
    private readonly LiveControlHost _host;

    public LiveHtmlOverlayHostTests()
    {
        _host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _dir,
            ConfigDir = _dir,
            PipeName = "agterm-live-html-" + Guid.NewGuid().ToString("N"),
            OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
        });
    }

    public void Dispose()
    {
        _host.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private HtmlOverlay OpenPage()
    {
        var page = new HtmlOverlay(new HtmlSource.File(Path.Combine(_dir, "r.html"), null));
        _host.Host.RunLocked(() =>
            Assert.Null(_host.Host.Store.OpenHtmlOverlay(_host.Host.Store.ActiveSession!.Id, page, sizePercent: null)));
        return page;
    }

    private ControlResponse Navigate(HtmlNavigation navigation)
    {
        ControlResponse? response = null;
        _host.Host.RunLocked(() =>
            response = _host.Host.Actions.NavigateSessionOverlay("active", null, null, navigation));
        return response!;
    }

    [Fact]
    public void TheAppOpensHtml()
    {
        ControlResponse? response = null;
        _host.Host.RunLocked(() => response = _host.Host.Actions.OpenSessionOverlay("active", null,
            new ControlSessionOverlayOpenOptions("", null, false, null, null,
                Page: new HtmlSource.File(Path.Combine(_dir, "r.html"), null))));

        Assert.True(response!.Ok, response.Error);
        _host.Host.RunLocked(() => Assert.NotNull(_host.Host.Store.ActiveSession!.HtmlOverlay));
    }

    [Fact]
    public void AReleasedPageIsAnnounced()
    {
        var released = new List<Guid>();
        _host.HtmlOverlayReleased += released.Add;
        var page = OpenPage();

        _host.Host.RunLocked(() => Assert.True(_host.Host.Store.CloseOverlay(_host.Host.Store.ActiveSession!.Id)));

        Assert.Equal([page.Id], released);
    }

    [Fact]
    public void AReloadAnnouncesACopyOfThePage()
    {
        var reloaded = new List<HtmlOverlay>();
        _host.HtmlOverlayReloaded += reloaded.Add;
        var page = OpenPage();

        _host.Host.RunLocked(() =>
            Assert.True(_host.Host.Actions.ReloadSessionOverlay("active", null, null, current: true).Ok));

        var copy = Assert.Single(reloaded);
        Assert.NotSame(page, copy);
        Assert.Equal(page.Id, copy.Id);
        Assert.Equal(1, copy.ReloadRevision);
        Assert.Equal(HtmlReloadTarget.Current, copy.ReloadTarget);
    }

    [Fact]
    public async Task PageReportsReachTheStore()
    {
        var page = OpenPage();

        _host.HtmlPages.SetLoadState(page.Id, HtmlLoadState.Failed, "boom");
        await ((LiveHtmlPageStore)_host.HtmlPages).Flushed;

        _host.Host.RunLocked(() =>
        {
            Assert.Equal(HtmlLoadState.Failed, page.LoadState);
            Assert.Equal("boom", page.LoadError);
        });
    }

    [Fact]
    public async Task TheSnapshotCarriesACopyOfTheSelectedSessionsPage()
    {
        var seen = new TaskCompletionSource<LiveSelectionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
        {
            if (snapshot.HtmlOverlay is not null) seen.TrySetResult(snapshot);
        };

        var page = OpenPage();
        var snapshot = await seen.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotSame(page, snapshot.HtmlOverlay);
        Assert.Equal(page.Id, snapshot.HtmlOverlay!.Id);
        Assert.True(snapshot.OverlayVisible);
        Assert.Null(snapshot.OverlayPaneId);
    }

    [Fact]
    public void NavigateRunsInTheAppsWebView()
    {
        var page = OpenPage();
        Assert.Equal(OverlayHtmlError.NotRealized, Navigate(HtmlNavigation.Back).Error);

        var asked = new List<(Guid, HtmlNavigation)>();
        string? answer = null;
        _host.HtmlOverlayNavigator = (id, navigation) =>
        {
            asked.Add((id, navigation));
            return Task.FromResult(answer);
        };
        Assert.True(Navigate(HtmlNavigation.Forward).Ok);
        answer = OverlayHtmlError.NoBrowser;
        Assert.Equal(OverlayHtmlError.NoBrowser, Navigate(HtmlNavigation.Browser).Error);

        Assert.Equal([(page.Id, HtmlNavigation.Forward), (page.Id, HtmlNavigation.Browser)], asked);
    }

    [Fact]
    public void AWebViewThatDoesNotAnswerInTimeIsNotRealized()
    {
        OpenPage();
        _host.HtmlNavigateBudget = TimeSpan.FromMilliseconds(50);
        _host.HtmlOverlayNavigator = (_, _) => new TaskCompletionSource<string?>().Task;

        Assert.Equal(OverlayHtmlError.NotRealized, Navigate(HtmlNavigation.Back).Error);
    }

    private sealed class FakeSurface : IHeadlessSurface
    {
        public string? PaneToken => "fake";
        public bool Inject(string text) => true;
        public string? ReadScreenText(bool all, int? lines) => "";
        public string? SelectedText => null;
        public void SelectAll() { }
        public bool Paste() => true;
        public bool PerformBindingAction(string action) => true;
        public void PromoteToPrimaryPane() { }
        public void Teardown() { }
    }
}
