using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm.Tests;

// The page engine of a file page in the overlay slot (upstream HtmlOverlayRegistry.swift and its
// tests, c63e5158), driven through a scripted web view.
public sealed class HtmlOverlayPageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-html-page-").FullName;
    private readonly string _pages;
    private readonly AppStore _store;
    private readonly Session _session;
    private readonly FakeHtmlBrowser _browser = new();
    private readonly List<string?> _themeAsks = [];
    private readonly HtmlOverlayRegistry _registry;
    private HtmlOverlayTheme _theme = new("#101010", "#e0e0e0", dark: true);

    public HtmlOverlayPageTests()
    {
        _pages = Path.Combine(_dir, "pages");
        Directory.CreateDirectory(Path.Combine(_pages, "sub dir"));
        File.WriteAllText(Path.Combine(_pages, "a.html"), "<title>A</title><a href=\"b.html\">b</a>");
        File.WriteAllText(Path.Combine(_pages, "b.html"), "<title>B</title>");
        File.WriteAllText(Path.Combine(_pages, "sub dir", "c d.html"), "<title>C</title>");
        _store = new AppStore(persistence: new PersistenceStore(Path.Combine(_dir, "state")),
                              stateDirectory: Path.Combine(_dir, "state"));
        _session = _store.AddSession(_store.AddWorkspace("work").Id, _dir)!;
        _registry = new HtmlOverlayRegistry(new DirectHtmlPageStore(_store), _browser, background =>
        {
            _themeAsks.Add(background);
            return _theme;
        });
        _store.HtmlOverlayReleased = _registry.Release;
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PagePath(string name) => Path.Combine(_pages, name);

    private HtmlOverlay Open(string file = "a.html", string? grant = null, bool javascript = false)
    {
        var overlay = new HtmlOverlay(new HtmlSource.File(PagePath(file), grant)) { Javascript = javascript };
        Assert.Null(_store.OpenHtmlOverlay(_session.Id, overlay, sizePercent: null));
        return overlay;
    }

    private async Task<(HtmlOverlayPage Page, FakeHtmlPageView View)> Realize(HtmlOverlay overlay, string? background = null)
    {
        var view = new FakeHtmlPageView();
        var page = await _registry.RealizeAsync(overlay, () => Task.FromResult<IHtmlPageView>(view), background);
        return (page, view);
    }

    private static string Host(HtmlOverlay overlay) => HtmlOverlayPage.VirtualHost(overlay.Id);

    private static string Url(HtmlOverlay overlay, string relative) => $"https://{Host(overlay)}/{relative}";

    // a granted page loaded and shown
    private async Task<(HtmlOverlay Overlay, HtmlOverlayPage Page, FakeHtmlPageView View)> Loaded(bool javascript = false)
    {
        var overlay = Open(grant: _pages, javascript: javascript);
        var (page, view) = await Realize(overlay);
        view.Load(Url(overlay, "a.html"), Url(overlay, "a.html"), "A");
        Assert.Equal(HtmlLoadState.Loaded, overlay.LoadState);
        return (overlay, page, view);
    }

    [Fact]
    public async Task APageIsCreatedOncePerIdentityAndReleasedByTheModel()
    {
        var overlay = Open();
        var made = 0;
        var view = new FakeHtmlPageView();
        Task<IHtmlPageView> Make()
        {
            made += 1;
            return Task.FromResult<IHtmlPageView>(view);
        }

        var first = await _registry.RealizeAsync(overlay, Make);
        Assert.Same(first, await _registry.RealizeAsync(overlay, Make));
        Assert.Equal(1, made);
        Assert.Same(first, _registry.Existing(overlay.Id));

        Assert.True(_store.CloseOverlay(_session.Id));
        Assert.Null(_registry.Existing(overlay.Id));
        Assert.True(first.IsClosed);
        Assert.True(view.Closed);
        Assert.False(view.Subscribed);
    }

    [Fact]
    public async Task APageReleasedWhileItsViewIsMadeIsClosedOnArrival()
    {
        var overlay = Open();
        var view = new FakeHtmlPageView();
        var making = new TaskCompletionSource<IHtmlPageView>();
        var realizing = _registry.RealizeAsync(overlay, () => making.Task);

        Assert.True(_store.CloseOverlay(_session.Id));
        making.SetResult(view);
        var page = await realizing;

        Assert.True(page.IsClosed);
        Assert.True(view.Closed);
        Assert.Empty(view.Calls.Where(call => call.StartsWith("navigate", StringComparison.Ordinal)));
        Assert.Null(_registry.Existing(overlay.Id));
    }

    [Fact]
    public async Task ASoftClosedPageSurvivesUntilTheCloseIsFinal()
    {
        var overlay = Open();
        await Realize(overlay);
        Assert.True(_store.SoftCloseSession(_session.Id, grace: 60));
        Assert.NotNull(_registry.Existing(overlay.Id));
        _store.FinalizeAllPendingCloses();
        Assert.Null(_registry.Existing(overlay.Id));
    }

    [Fact]
    public async Task AGrantedPageIsServedFromItsOwnHostWithTheThemeAndItsScriptSetting()
    {
        var overlay = Open(file: Path.Combine("sub dir", "c d.html"), grant: _pages, javascript: true);
        var (_, view) = await Realize(overlay, background: "#202020");

        Assert.True(view.Javascript);
        Assert.Equal([(Host(overlay), _pages)], view.Mapped);
        Assert.Equal([_theme.Script(themed: true)], view.DocumentScripts);
        Assert.Equal(["navigate " + Url(overlay, "sub%20dir/c%20d.html")], view.Calls);
        Assert.Equal(["#202020"], _themeAsks);
        Assert.EndsWith(".example", Host(overlay), StringComparison.Ordinal);
        Assert.NotEqual(Host(overlay), HtmlOverlayPage.VirtualHost(Guid.NewGuid()));
    }

    [Fact]
    public async Task APageWithoutAGrantIsLoadedFromItsTextWithScriptOff()
    {
        var overlay = Open();
        var (_, view) = await Realize(overlay);

        Assert.False(view.Javascript);
        Assert.Empty(view.Mapped);
        Assert.Equal(["text <title>A</title><a href=\"b.html\">b</a>"], view.Calls);

        Assert.False(view.Start("data:text/html;charset=utf-8;base64,PHRpdGxlPg=="));
        view.Commit("about:blank", "A");
        view.Complete();
        Assert.Equal(HtmlLoadState.Loaded, overlay.LoadState);
        Assert.Equal(new HtmlPageInfo(PagePath("a.html"), "A", false, false), overlay.Current);

        Assert.True(view.Start("data:text/html,<script>1</script>"), "only the app's own text load is allowed");
    }

    [Fact]
    public async Task AMissingFileReportsAFailure()
    {
        var overlay = Open(file: "missing.html", grant: _pages);
        var (_, view) = await Realize(overlay);

        Assert.Equal(HtmlLoadState.Failed, overlay.LoadState);
        Assert.Contains("missing.html", overlay.LoadError, StringComparison.Ordinal);
        Assert.Empty(view.Calls);
    }

    [Fact]
    public async Task LoadReportsStateTitleAndHistory()
    {
        var overlay = Open(grant: _pages);
        var (_, view) = await Realize(overlay);
        Assert.False(view.Start(Url(overlay, "a.html"), user: true));
        Assert.Equal(HtmlLoadState.Loading, overlay.LoadState);
        view.Commit(Url(overlay, "a.html"), "A");
        view.Complete();
        Assert.Equal(HtmlLoadState.Loaded, overlay.LoadState);
        Assert.Equal(new HtmlPageInfo(PagePath("a.html"), "A", false, false), overlay.Current);

        Assert.False(view.Start(Url(overlay, "b.html")));
        Assert.Equal(HtmlLoadState.Loading, overlay.LoadState);
        view.CanGoBack = true;
        view.Commit(Url(overlay, "b.html"), "");
        view.Complete();
        Assert.Equal(new HtmlPageInfo(PagePath("b.html"), null, true, false), overlay.Current);

        // WebView2 names a page without a title by its address
        view.Commit(Url(overlay, "b.html"), $"{Host(overlay)}/b.html");
        Assert.Null(overlay.Current?.Title);
    }

    [Fact]
    public async Task NavigationStaysInsideTheGrant()
    {
        var (overlay, _, view) = await Loaded();

        Assert.False(view.Start(Url(overlay, "sub%20dir/c%20d.html")));
        Assert.False(view.Start(Url(overlay, "b.html"), HtmlNavigationTarget.Subframe));
        Assert.False(view.Start("about:srcdoc", HtmlNavigationTarget.Subframe));
        Assert.True(view.Start(new Uri(PagePath("b.html")).AbsoluteUri, user: true), "a raw file URL leaves the page's host");
        Assert.True(view.Start("https://other.example/a.html", HtmlNavigationTarget.Subframe));
        Assert.True(view.Start("data:text/html,x", HtmlNavigationTarget.Subframe));
    }

    [Fact]
    public async Task AFileAloneGrantLoadsNothingButItsText()
    {
        var overlay = Open();
        var (_, view) = await Realize(overlay);
        Assert.False(view.Start("data:text/html;base64,QQ=="));
        view.Commit("about:blank", "A");
        view.Complete();

        Assert.True(view.Start(new Uri(PagePath("b.html")).AbsoluteUri, user: true));
        Assert.True(view.Start(Url(overlay, "b.html")), "a page without a grant has no host");
        Assert.True(view.Start("about:blank#blocked", user: true), "Chromium's address for a link it cannot resolve");
        Assert.Empty(_browser.Prompts);
    }

    [Fact]
    public async Task APageScriptLoadingAWebPageIsBlockedAndAPendingLoadFails()
    {
        var overlay = Open(grant: _pages);
        var (_, view) = await Realize(overlay);
        Assert.False(view.Start(Url(overlay, "a.html"), user: true));

        Assert.True(view.Start("https://example.com/"));
        Assert.Equal(HtmlLoadState.Failed, overlay.LoadState);
        Assert.Equal("navigation blocked: https://example.com/", overlay.LoadError);
        Assert.Empty(_browser.Prompts);
    }

    [Fact]
    public async Task ABlockedNavigationLeavesALoadedPageLoaded()
    {
        var (overlay, _, view) = await Loaded(javascript: true);

        Assert.True(view.Start("https://example.com/"));
        view.Complete(success: false, canceled: true, error: "OperationCanceled");
        Assert.Equal(HtmlLoadState.Loaded, overlay.LoadState);
        Assert.Null(overlay.LoadError);
    }

    [Fact]
    public async Task AFailedLoadReportsTheMissingFileOrTheStatus()
    {
        var (overlay, _, view) = await Loaded();

        Assert.False(view.Start(Url(overlay, "gone.html")));
        view.Complete(success: false, error: "Unknown");
        Assert.Equal(HtmlLoadState.Failed, overlay.LoadState);
        Assert.Contains("gone.html", overlay.LoadError, StringComparison.Ordinal);

        Assert.False(view.Start(Url(overlay, "b.html")));
        view.Complete(success: false, error: "ConnectionAborted");
        Assert.Equal("page load failed: ConnectionAborted", overlay.LoadError);
    }

    [Fact]
    public async Task AProcessFailureFailsThePageAndTheNextReloadLoadsTheSourceAgain()
    {
        var (overlay, _, view) = await Loaded();

        view.Crash();
        Assert.Equal(HtmlLoadState.Failed, overlay.LoadState);
        Assert.Equal("web content process terminated", overlay.LoadError);

        view.Calls.Clear();
        Assert.Null(_registry.Reload(_store, _session.Id, HtmlReloadTarget.Current));
        Assert.Equal(["navigate " + Url(overlay, "a.html")], view.Calls);
    }

    [Fact]
    public async Task ReloadShowsTheCurrentPageOrReturnsToTheOriginal()
    {
        var (overlay, _, view) = await Loaded();
        view.Load(Url(overlay, "b.html"), Url(overlay, "b.html"), "B");
        view.Calls.Clear();

        Assert.Null(_registry.Reload(_store, _session.Id, HtmlReloadTarget.Current));
        Assert.Equal(["reload"], view.Calls);
        Assert.Equal(HtmlLoadState.Loading, overlay.LoadState);

        view.Calls.Clear();
        Assert.Null(_registry.Reload(_store, _session.Id, HtmlReloadTarget.Original));
        Assert.Equal(["navigate " + Url(overlay, "a.html")], view.Calls);
        Assert.Null(overlay.Current);
    }

    [Fact]
    public async Task ReloadBeforeTheFirstCommitAndOfATextPageLoadsTheSourceAgain()
    {
        var overlay = Open(grant: _pages);
        var (_, view) = await Realize(overlay);
        view.Calls.Clear();
        Assert.Null(_registry.Reload(_store, _session.Id, HtmlReloadTarget.Current));
        Assert.Equal(["navigate " + Url(overlay, "a.html")], view.Calls);

        Assert.True(_store.CloseOverlay(_session.Id));
        var text = Open();
        var (_, textView) = await Realize(text);
        Assert.False(textView.Start("data:text/html;base64,QQ=="));
        textView.Commit("about:blank", "A");
        textView.Complete();
        textView.Calls.Clear();
        Assert.Null(_registry.Reload(_store, _session.Id, HtmlReloadTarget.Current));
        Assert.Equal(["text <title>A</title><a href=\"b.html\">b</a>"], textView.Calls);
    }

    [Fact]
    public async Task AReloadOfASessionWithoutAPageIsRefused()
    {
        Assert.Equal(HtmlOverlayCommandFailure.NoOverlay, _registry.Reload(_store, _session.Id, HtmlReloadTarget.Current));
        Assert.Equal(HtmlOverlayCommandFailure.UnknownSession, _registry.Reload(_store, Guid.NewGuid(), HtmlReloadTarget.Current));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ThePageKeepsWhatItReportsForItsStrip()
    {
        var overlay = Open(grant: _pages);
        var (page, view) = await Realize(overlay);
        var changes = 0;
        page.Changed += () => changes += 1;
        Assert.Equal(HtmlLoadState.Loading, page.LoadState);

        view.Load(Url(overlay, "a.html"), Url(overlay, "a.html"), "A");
        Assert.Equal(HtmlLoadState.Loaded, page.LoadState);
        Assert.Equal(new HtmlPageInfo(PagePath("a.html"), "A", false, false), page.Current);
        Assert.NotEqual(0, changes);

        changes = 0;
        view.Crash();
        Assert.Equal(HtmlLoadState.Failed, page.LoadState);
        Assert.Equal("web content process terminated", page.LoadError);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task AReloadAppliedFromACopyLoadsAgainAndShowsLoadingAtOnce()
    {
        var (overlay, page, view) = await Loaded();
        view.Crash();
        Assert.Null(_store.ReloadHtmlOverlay(_session.Id, null));
        var changes = 0;
        page.Changed += () => changes += 1;
        view.Calls.Clear();

        _registry.Apply(overlay.Copy());
        _registry.Apply(overlay.Copy());
        _registry.Apply(new HtmlOverlay(overlay.Source) { ReloadRevision = 5 });

        Assert.Equal(["navigate " + Url(overlay, "a.html")], view.Calls);
        Assert.Equal(HtmlLoadState.Loading, page.LoadState);
        Assert.Null(page.LoadError);
        Assert.Null(page.Current);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task BackAndForwardFollowTheHistory()
    {
        var (overlay, _, view) = await Loaded();
        Assert.Equal(OverlayHtmlError.NoHistory(HtmlNavigation.Back), _registry.Navigate(overlay.Id, HtmlNavigation.Back));
        Assert.Equal(OverlayHtmlError.NoHistory(HtmlNavigation.Forward), _registry.Navigate(overlay.Id, HtmlNavigation.Forward));

        view.CanGoBack = true;
        view.CanGoForward = true;
        view.Calls.Clear();
        Assert.Null(_registry.Navigate(overlay.Id, HtmlNavigation.Back));
        Assert.Null(_registry.Navigate(overlay.Id, HtmlNavigation.Forward));
        Assert.Equal(["back", "forward"], view.Calls);
    }

    [Fact]
    public async Task NavigatingAPageThatWasNeverShownIsRefused()
    {
        var overlay = Open();
        Assert.Equal(OverlayHtmlError.NotRealized, _registry.Navigate(overlay.Id, HtmlNavigation.Browser));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task OpenInBrowserOpensTheOriginalFileAndFailsWithoutABrowser()
    {
        var (overlay, _, view) = await Loaded();
        view.Load(Url(overlay, "b.html"), Url(overlay, "b.html"), "B");

        Assert.Null(_registry.Navigate(overlay.Id, HtmlNavigation.Browser));
        Assert.Equal([new Uri(PagePath("a.html"))], _browser.Opened);
        Assert.Empty(_browser.Prompts);
        _browser.Available = false;
        Assert.Equal(OverlayHtmlError.NoBrowser, _registry.Navigate(overlay.Id, HtmlNavigation.Browser));
    }

    [Fact]
    public async Task FinderRevealsTheCurrentFile()
    {
        var (overlay, _, view) = await Loaded();
        view.Load(Url(overlay, "sub%20dir/c%20d.html"), Url(overlay, "sub%20dir/c%20d.html"), "C");

        Assert.Null(_registry.Navigate(overlay.Id, HtmlNavigation.Finder));
        Assert.Equal([PagePath(Path.Combine("sub dir", "c d.html"))], _browser.Revealed);
        Assert.Empty(_browser.Opened);
    }

    [Fact]
    public async Task FinderRevealsTheOriginalFileOfATextPageAndBeforeTheFirstLoad()
    {
        var pending = Open(grant: _pages);
        await Realize(pending);
        Assert.Null(_registry.Navigate(pending.Id, HtmlNavigation.Finder));

        Assert.True(_store.CloseOverlay(_session.Id));
        var text = Open(file: "b.html");
        var (_, view) = await Realize(text);
        Assert.False(view.Start("data:text/html;base64,QQ=="));
        view.Commit("about:blank", "B");
        view.Complete();
        Assert.Null(_registry.Navigate(text.Id, HtmlNavigation.Finder));

        Assert.Equal([PagePath("a.html"), PagePath("b.html")], _browser.Revealed);
    }

    [Fact]
    public async Task APageClickingLinksInALoopOpensOnlyWhatTheUserApproves()
    {
        var (overlay, _, view) = await Loaded(javascript: true);
        var target = new Uri("https://example.com/x");

        Assert.True(view.Start(target.AbsoluteUri, user: true));
        Assert.True(view.Start(target.AbsoluteUri, user: true));
        Assert.Single(_browser.Prompts);
        _browser.Answers[0](true);
        Assert.Equal([target], _browser.Opened);

        view.Start(target.AbsoluteUri, HtmlNavigationTarget.NewWindow, user: true);
        Assert.Equal(2, _browser.Prompts.Count);
        _browser.Answers[1](false);
        view.Start(target.AbsoluteUri, user: true);
        Assert.Equal(2, _browser.Prompts.Count);

        view.Input();
        view.Start(target.AbsoluteUri, user: true);
        Assert.Equal([target, target, target], _browser.Prompts);

        Assert.True(_store.CloseOverlay(_session.Id));
        Assert.Equal(1, _browser.Dismissals);
        _browser.Answers[2](true);
        Assert.Equal([target], _browser.Opened);
        Assert.Equal(HtmlLoadState.Loaded, overlay.LoadState);
    }

    [Fact]
    public async Task APageOutOfSightAsksNothingAndLosesItsPrompt()
    {
        var (_, page, view) = await Loaded();
        view.Start("https://example.com/x", user: true);
        Assert.Single(_browser.Prompts);

        page.SetOnScreen(false);
        Assert.Equal(1, _browser.Dismissals);
        _browser.Answers[0](true);
        Assert.Empty(_browser.Opened);
        view.Start("https://example.com/x", user: true);
        Assert.Single(_browser.Prompts);

        page.SetOnScreen(true);
        view.Start("https://example.com/x", user: true);
        Assert.Equal(2, _browser.Prompts.Count);
    }

    [Fact]
    public async Task APageScriptCannotOpenAWindowOrAPromptWithoutTheUser()
    {
        var (_, _, view) = await Loaded(javascript: true);
        view.Start("https://example.com/x", HtmlNavigationTarget.NewWindow);
        view.Start("https://example.com/x", HtmlNavigationTarget.Subframe);
        Assert.Empty(_browser.Prompts);
    }

    [Fact]
    public async Task AThemeChangeReloadsThePageShownAndAnUnchangedThemeDoesNot()
    {
        var (overlay, page, view) = await Loaded();
        view.Load(Url(overlay, "b.html"), Url(overlay, "b.html"), "B");
        view.Calls.Clear();

        await page.ApplyThemeAsync(new HtmlOverlayTheme("#101010", "#e0e0e0", dark: true));
        Assert.Empty(view.Calls);
        Assert.Single(view.DocumentScripts);

        var changed = new HtmlOverlayTheme("#000000", "#102030", dark: true);
        await page.ApplyThemeAsync(changed);
        Assert.Equal(changed.Script(themed: true), view.DocumentScripts[^1]);
        Assert.Equal(["reload"], view.Calls);
    }

    [Fact]
    public async Task RefreshingThemesAsksForEachPagesBackground()
    {
        var overlay = Open(grant: _pages);
        var (_, view) = await Realize(overlay, background: "#303030");
        _themeAsks.Clear();
        _theme = new HtmlOverlayTheme("#000000", "#ffffff", dark: true);

        await _registry.RefreshThemesAsync();
        Assert.Equal(["#303030"], _themeAsks);
        Assert.Equal(_theme.Script(themed: true), view.DocumentScripts[^1]);
    }

    [Fact]
    public async Task AClosedPageReportsNothingMore()
    {
        var (overlay, page, view) = await Loaded();
        var current = overlay.Current;
        page.Close();

        Assert.Contains("stop", view.Calls);
        Assert.True(view.Closed);
        Assert.False(view.Subscribed);
        Assert.Equal(current, overlay.Current);
    }
}
