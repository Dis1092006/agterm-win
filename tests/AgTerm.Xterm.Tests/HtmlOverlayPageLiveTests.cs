using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

// The page engine on a real WebView2: what the scripted view in HtmlOverlayPageTests cannot show,
// the browser's own handling of script settings, the virtual host and the document script.
[Collection("webview2")]
public sealed class HtmlOverlayPageLiveTests : IAsyncLifetime
{
    private readonly WebView2Fixture _webView;
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-html-live-").FullName;
    private readonly string _pages;
    private readonly AppStore _store;
    private readonly Session _session;
    private readonly FakeHtmlBrowser _browser = new();
    private readonly HtmlOverlayRegistry _registry;
    private int _completions;

    public HtmlOverlayPageLiveTests(WebView2Fixture webView)
    {
        _webView = webView;
        _pages = Path.Combine(_dir, "pages");
        Directory.CreateDirectory(_pages);
        Write("a.html", "<title>A</title><p>a</p><a id=\"b\" href=\"b.html\">b</a>");
        Write("b.html", "<title>B</title><p>b</p>");
        Write("plain.html", "<p>no title</p>");
        Write("script.html", "<title>A</title><script>document.title = 'ran'</script>");
        Write("grant.html", "<title>G</title><script src=\"s.js\"></script><script src=\"../outside.js\"></script>");
        Write("s.js", "window.inside = 1;");
        File.WriteAllText(Path.Combine(_dir, "outside.js"), "window.outside = 1;");
        _store = new AppStore(persistence: new PersistenceStore(Path.Combine(_dir, "state")),
                              stateDirectory: Path.Combine(_dir, "state"));
        _session = _store.AddSession(_store.AddWorkspace("work").Id, _dir)!;
        _registry = new HtmlOverlayRegistry(_store, _browser, _ => new HtmlOverlayTheme("#102030", "#e0e0e0", dark: true));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _webView.RunAsync(_ => Task.FromResult(_store.CloseOverlay(_session.Id)));
        Directory.Delete(_dir, recursive: true);
    }

    private void Write(string name, string html) => File.WriteAllText(Path.Combine(_pages, name), html);

    private async Task<(HtmlOverlay Overlay, Win32HtmlPageView View)> OpenAsync(
        string file, bool grant = true, bool javascript = false)
    {
        var overlay = new HtmlOverlay(new HtmlSource.File(Path.Combine(_pages, file), grant ? _pages : null))
        {
            Javascript = javascript,
        };
        var control = await _webView.CreateWebViewAsync();
        var view = await _webView.RunAsync(async _ =>
        {
            Assert.Null(_store.OpenHtmlOverlay(_session.Id, overlay, sizePercent: null));
            var adapter = new Win32HtmlPageView(control);
            adapter.NavigationCompleted += _ => _completions += 1;
            await _registry.RealizeAsync(overlay, () => Task.FromResult<IHtmlPageView>(adapter));
            return adapter;
        });
        await WaitAsync(() => _completions >= 1, "first load");
        return (overlay, view);
    }

    private Task<T> OnUi<T>(Func<T> read) => _webView.RunAsync(_ => Task.FromResult(read()));

    private async Task WaitAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await OnUi(condition))
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(what);
            await Task.Delay(20);
        }
    }

    private Task<string> EvalAsync(Win32HtmlPageView view, string script) =>
        _webView.RunAsync(_ => view.Core.ExecuteScriptAsync(script));

    [Fact]
    public async Task AGrantedPageLoadsFromItsHostAndReportsItself()
    {
        var (overlay, _) = await OpenAsync("a.html");

        Assert.Equal(HtmlLoadState.Loaded, overlay.LoadState);
        Assert.Equal(new HtmlPageInfo(Path.Combine(_pages, "a.html"), "A", false, false), overlay.Current);
    }

    [Fact]
    public async Task APageWithoutAGrantLoadsAsText()
    {
        var (overlay, view) = await OpenAsync("a.html", grant: false);

        Assert.Equal(HtmlLoadState.Loaded, overlay.LoadState);
        Assert.Equal(new HtmlPageInfo(Path.Combine(_pages, "a.html"), "A", false, false), overlay.Current);
        Assert.Equal("\"a\"", await EvalAsync(view, "document.querySelector('p').textContent"));
    }

    [Fact]
    public async Task APageWithoutATitleReportsNone()
    {
        var (overlay, _) = await OpenAsync("plain.html");

        Assert.Equal(HtmlLoadState.Loaded, overlay.LoadState);
        Assert.Null(overlay.Current?.Title);
    }

    [Fact]
    public async Task PageScriptStaysOffUnlessAskedAndAcrossAReload()
    {
        var (off, offView) = await OpenAsync("script.html");
        Assert.Equal("A", off.Current?.Title);

        await OnUi(() => _registry.Reload(_session.Id, HtmlReloadTarget.Current));
        await WaitAsync(() => _completions >= 2, "reload");
        Assert.Equal(HtmlLoadState.Loaded, off.LoadState);
        Assert.Equal("\"A\"", await EvalAsync(offView, "document.title"));

        await OnUi(() => _store.CloseOverlay(_session.Id));
        _completions = 0;
        var (on, _) = await OpenAsync("script.html", javascript: true);
        await WaitAsync(() => on.Current?.Title == "ran", "page script");
    }

    [Fact]
    public async Task ThePageGetsTheThemeWithItsScriptOff()
    {
        var (_, view) = await OpenAsync("a.html");

        Assert.Equal("\"#102030\"", await EvalAsync(view,
            "getComputedStyle(document.documentElement).getPropertyValue('--agterm-background').trim()"));
        Assert.Equal("\"dark\"", await EvalAsync(view,
            "getComputedStyle(document.documentElement).colorScheme"));
    }

    [Fact]
    public async Task TheGrantServesItsFolderAndNothingAbove()
    {
        var (overlay, view) = await OpenAsync("grant.html", javascript: true);

        Assert.Equal(HtmlLoadState.Loaded, overlay.LoadState);
        Assert.Equal("1", await EvalAsync(view, "window.inside"));
        Assert.Equal("null", await EvalAsync(view, "window.outside ?? null"));
    }

    [Fact]
    public async Task ALinkInTheGrantOpensTheNextPageWithHistory()
    {
        var (overlay, view) = await OpenAsync("a.html");

        await EvalAsync(view, "document.getElementById('b').click()");
        await WaitAsync(() => overlay.Current?.Title == "B" && overlay.Current.CanGoBack
                              && overlay.LoadState == HtmlLoadState.Loaded, "page b");
        Assert.Equal(new HtmlPageInfo(Path.Combine(_pages, "b.html"), "B", true, false), overlay.Current);
        Assert.Equal(HtmlLoadState.Loaded, overlay.LoadState);

        Assert.Null(await OnUi(() => _registry.Navigate(overlay.Id, HtmlNavigation.Back)));
        await WaitAsync(() => overlay.Current?.Title == "A" && overlay.Current.CanGoForward, "back to a");
    }

    [Fact]
    public async Task AMissingPageInTheGrantFails()
    {
        var (overlay, view) = await OpenAsync("a.html");

        await EvalAsync(view, "location.href = 'missing.html'");
        await WaitAsync(() => overlay.LoadState == HtmlLoadState.Failed, "failure");
        Assert.Contains("missing.html", overlay.LoadError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWebLinkIsOfferedToTheBrowserAndThePageStays()
    {
        var (overlay, view) = await OpenAsync("a.html");

        await EvalAsync(view, "location.href = 'https://example.com/'");
        await WaitAsync(() => _browser.Prompts.Count == 1, "prompt");
        Assert.Equal(new Uri("https://example.com/"), _browser.Prompts[0]);
        Assert.Equal(HtmlLoadState.Loaded, overlay.LoadState);
        Assert.Equal("A", overlay.Current?.Title);
        Assert.Equal("\"a\"", await EvalAsync(view, "document.querySelector('p').textContent"));
    }
}
