using System.Text.Json;
using System.Text.Json.Nodes;
using AgTerm.Cli;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// session.hud end to end: agtermctl over the live pipe, the store, the selection snapshot and the
/// real page, wired the way MainWindow wires them. The page's metrics go back through
/// LiveControlHost.ApplyHudMetricsAsync, so the sizes tree reads back are measured in the page.
/// </summary>
[Collection("webview2")]
public sealed class LiveHudE2ETests : IDisposable
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private const string HudShown = "(() => document.querySelector('.hud-layer') !== null)()";

    private readonly WebView2Fixture _webView;
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-live-hud-").FullName;
    private readonly TaskCompletionSource<LiveControlHost> _hostReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly LiveControlHost _host;
    private readonly Lazy<Task<XtermPage>> _page;

    public LiveHudE2ETests(WebView2Fixture webView)
    {
        _webView = webView;
        _page = new Lazy<Task<XtermPage>>(CreatePageAsync);
        _host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-hud-" + Guid.NewGuid().ToString("N"),
            DefaultShell = Cmd,
            HomeDirectory = _stateDir,
            OpenSurface = async (request, cancellationToken) =>
            {
                var shared = await _page.Value;
                return await _webView.RunAsync(async _ =>
                    await XtermPaneSurface.OpenAsync(shared, request.Options, cancellationToken));
            },
        });
        _hostReady.SetResult(_host);
        _host.Start();
    }

    public void Dispose()
    {
        _host.Dispose();
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // Subscribed before the first pane opens, as MainWindow does: the page posts its first metrics
    // then, and it never posts equal values twice.
    private async Task<XtermPage> CreatePageAsync()
    {
        var page = await _webView.CreatePageAsync();
        page.HudMetricsReceived += (paneId, metrics, singlePane) => _ = ForwardMetricsAsync(paneId, metrics, singlePane);
        return page;
    }

    private async Task ForwardMetricsAsync(string paneId, HudSessionMetrics metrics, HudRect? singlePane)
    {
        var host = await _hostReady.Task;
        await host.ApplyHudMetricsAsync(paneId, metrics, singlePane);
    }

    private (int Exit, string Out, string Error) Ctl(params string[] argv)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = Agtermctl.Run(argv, new CliEnvironment
        {
            GetEnv = key => key == "AGTERM_CONTROL_SOCKET" ? _host.PipeName : null,
            Out = stdout,
            Error = stderr,
        });
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private string CtlOk(params string[] argv)
    {
        var (exit, output, error) = Ctl(argv);
        Assert.True(exit == 0, $"agtermctl {string.Join(' ', argv)} failed ({exit}): {error}");
        return output;
    }

    private void WaitForText(string contains, int timeoutMs = 30000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var text = "";
        while (Environment.TickCount64 < deadline)
        {
            text = Ctl("session", "text", "--all").Out;
            if (text.Contains(contains)) return;
            Thread.Sleep(100);
        }
        Assert.Fail($"session text never contained \"{contains}\"; last:\n{text}");
    }

    private T ReadLocked<T>(Func<T> read)
    {
        var result = default(T)!;
        _host.Host.RunLocked(() => result = read());
        return result;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Condition not met within the timeout.");
            await Task.Delay(50);
        }
    }

    private async Task<string> EvalAsync(XtermPage page, string script) =>
        await _webView.RunAsync(_ => page.ExecuteScriptAsync(script));

    private async Task WaitForPageAsync(XtermPage page, string condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (await EvalAsync(page, condition) != "true")
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Not met within the timeout: {condition}");
            await Task.Delay(50);
        }
    }

    private JsonNode ActiveSessionNode()
    {
        var tree = JsonNode.Parse(CtlOk("tree", "--json"))!["result"]!["tree"]!;
        foreach (var workspace in tree["workspaces"]!.AsArray())
        {
            foreach (var session in workspace!["sessions"]!.AsArray())
            {
                if (session!["active"]?.GetValue<bool>() == true)
                    return session;
            }
        }
        Assert.Fail("tree has no active session");
        return null!;
    }

    private JsonNode SessionNode(string id)
    {
        var tree = JsonNode.Parse(CtlOk("tree", "--json"))!["result"]!["tree"]!;
        foreach (var workspace in tree["workspaces"]!.AsArray())
        {
            foreach (var session in workspace!["sessions"]!.AsArray())
            {
                if (string.Equals(session!["id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase))
                    return session;
            }
        }
        Assert.Fail($"tree has no session {id}");
        return null!;
    }

    /// <summary>MainWindow's other half: the hud of each selection snapshot goes to the page.</summary>
    private void PresentHudsOn(XtermPage page) =>
        _host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
        {
            var view = snapshot.Hud is { } placement ? TerminalHudViews.From(placement) : null;
            _ = _webView.RunAsync(async _ =>
            {
                await page.ApplyHudAsync(view);
                return true;
            });
        };

    /// <summary>What MainWindow does on a selection: lay out the active session's pane, after which
    /// the page posts its metrics.</summary>
    private async Task ShowActiveSessionAsync(XtermPage page)
    {
        var mainPaneId = await _host.StoreCoordinator.ReadAsync(store => store.ActiveSession!.Surface!.PaneToken!);
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Single(1, mainPaneId, "Shell")));
        await WaitUntilAsync(() => ReadLocked(() => _host.Host.Store.ActiveSession?.HudMetrics is not null));
    }

    private async Task<XtermPage> ShowSessionAsync()
    {
        WaitForText(">");
        var page = await _page.Value;
        PresentHudsOn(page);
        await ShowActiveSessionAsync(page);
        return page;
    }

    // review focus 1: a HUD opened while its session is off screen reads the unmeasured width, and
    // selecting and showing that session measures it in the page with no further command
    [Fact]
    public async Task A_hud_opened_on_a_background_session_is_measured_once_that_session_is_shown()
    {
        WaitForText(">");
        var page = await _page.Value;
        PresentHudsOn(page);
        // the page lays out the first pane it opens and measures it, so the background session is
        // one the page has never shown
        CtlOk("session", "new");
        var background = ActiveSessionNode()["id"]!.GetValue<string>();
        CtlOk("session", "new");
        Assert.NotEqual(background, ActiveSessionNode()["id"]!.GetValue<string>());

        CtlOk("session", "hud", "building the release artifacts", "--target", background);
        Assert.Equal(HudLayout.MaxSizePercent, SessionNode(background)["hud"]!["sizePercent"]!.GetValue<int>());

        CtlOk("session", "select", "--target", background);
        await ShowActiveSessionAsync(page);

        await WaitUntilAsync(() => SessionNode(background)["hud"]!["sizePercent"]!.GetValue<int>() < HudLayout.MaxSizePercent);
        var sizePercent = SessionNode(background)["hud"]!["sizePercent"]!.GetValue<int>();
        await WaitForPageAsync(page, $$"""
            (() => {
                const layer = document.querySelector('.hud-layer');
                if (!layer) return false;
                const share = layer.getBoundingClientRect().width / layer.parentElement.getBoundingClientRect().width * 100;
                return Math.abs(share - {{sizePercent}}) <= 0.5;
            })()
            """);
    }

    [Fact]
    public async Task A_hud_opened_over_the_pipe_is_drawn_at_the_size_tree_reads_back()
    {
        var page = await ShowSessionAsync();

        CtlOk("session", "hud", "building", "--detail", "step 2 of 5");
        await WaitForPageAsync(page, "(() => document.querySelector('.hud-layer')?.textContent.includes('step 2 of 5') === true)()");

        var hud = ActiveSessionNode()["hud"]!;
        var sizePercent = hud["sizePercent"]!.GetValue<int>();
        var heightPercent = hud["heightPercent"]!.GetValue<int>();
        Assert.InRange(sizePercent, 10, 79); // an unmeasured session reads the 80 maximum

        using var drawn = JsonDocument.Parse(await EvalAsync(page, """
            (() => {
                const layer = document.querySelector('.hud-layer');
                const host = layer.parentElement.getBoundingClientRect();
                const r = layer.getBoundingClientRect();
                return { parent: layer.parentElement.id, width: r.width / host.width * 100, height: r.height / host.height * 100 };
            })()
            """));
        var root = drawn.RootElement;
        Assert.Equal("panes", root.GetProperty("parent").GetString());
        Assert.InRange(root.GetProperty("width").GetDouble(), sizePercent - 0.5, sizePercent + 0.5);
        // tree reports the ceiling of the share the panel takes
        Assert.InRange(root.GetProperty("height").GetDouble(), heightPercent - 1.01, heightPercent + 0.01);
    }

    [Fact]
    public async Task A_narrower_page_re_measures_the_hud_without_a_command()
    {
        var page = await ShowSessionAsync();
        CtlOk("session", "hud", "building the release artifacts");
        await WaitForPageAsync(page, HudShown);
        var before = ActiveSessionNode()["hud"]!["sizePercent"]!.GetValue<int>();

        await EvalAsync(page, "(() => { document.getElementById('panes').style.width = '50%'; return 1; })()");
        try
        {
            var after = before;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (after == before && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
                after = ActiveSessionNode()["hud"]!["sizePercent"]!.GetValue<int>();
            }
            // ceil(2x) is 2 ceil(x) or one less
            Assert.InRange(after, 2 * before - 1, 2 * before);
        }
        finally
        {
            await EvalAsync(page, "(() => { document.getElementById('panes').style.width = ''; return 1; })()");
        }
    }

    [Fact]
    public async Task An_update_over_the_pipe_redraws_the_same_panel_in_place()
    {
        var page = await ShowSessionAsync();
        CtlOk("session", "hud", "building");
        await WaitForPageAsync(page, HudShown);
        await EvalAsync(page, "(() => { document.querySelector('.hud-layer').dataset.mark = 'first'; return 1; })()");

        CtlOk("session", "hud", "update", "done", "--position", "top");

        await WaitForPageAsync(page, "(() => document.querySelector('.hud-layer')?.textContent === 'done')()");
        Assert.Equal("\"first\"", await EvalAsync(page, "(() => document.querySelector('.hud-layer').dataset.mark)()"));
        Assert.Equal("top-center", ActiveSessionNode()["hud"]!["position"]!.GetValue<string>());
    }

    // the page probes the cell at a drawn panel's font and posts nothing when the panel closes, so the
    // next HUD with no font of its own has to take the renderer font, not the closed panel's
    [Fact]
    public async Task A_hud_with_no_font_after_a_closed_hud_with_its_own_is_drawn_at_the_renderer_font()
    {
        var page = await ShowSessionAsync();
        var rendererFont = double.Parse(await EvalAsync(page, "(() => rendererSettings.fontSize)()"),
            System.Globalization.CultureInfo.InvariantCulture);
        CtlOk("session", "hud", "building", "--font-size", "30");
        await WaitForPageAsync(page, "(() => document.querySelector('.hud-layer')?.style.fontSize === '30px')()");
        await WaitUntilAsync(() => ReadLocked(() => _host.Host.Store.ActiveSession?.HudMetrics?.FontSize == 30));
        CtlOk("session", "hud", "close");
        await WaitForPageAsync(page, "(() => document.querySelector('.hud-layer') === null)()");

        CtlOk("session", "hud", "building");

        await WaitForPageAsync(page, HudShown);
        Assert.Equal(rendererFont, ReadLocked(() => _host.Host.Store.ActiveSession!.HudFontSize));
        Assert.Equal($"\"{rendererFont}px\"", await EvalAsync(page, "(() => document.querySelector('.hud-layer').style.fontSize)()"));
    }

    [Fact]
    public async Task Closing_the_hud_over_the_pipe_takes_it_off_the_page()
    {
        var page = await ShowSessionAsync();
        CtlOk("session", "hud", "building");
        await WaitForPageAsync(page, HudShown);

        CtlOk("session", "hud", "close");

        await WaitForPageAsync(page, "(() => document.querySelector('.hud-layer') === null)()");
        Assert.Null(ActiveSessionNode()["hud"]);
    }
}
