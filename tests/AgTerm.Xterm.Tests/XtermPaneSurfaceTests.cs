using AgTerm.ControlServer;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// The REAL terminal surface behind the control arms (session.type / text / copy / selectall /
/// paste / font), driven exactly the way the control host calls it: synchronous IHeadlessSurface
/// members invoked from a non-UI thread, marshaled inside onto the WebView2 UI thread. Real ConPTY
/// child + real xterm.js.
/// </summary>
[Collection("webview2")]
public class XtermPaneSurfaceTests
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;

    public XtermPaneSurfaceTests(WebView2Fixture webView)
    {
        _webView = webView;
    }

    private async Task<XtermPaneSurface> OpenAsync(XtermSurfaceOptions? options = null)
    {
        options ??= new XtermSurfaceOptions { Argv = new[] { Cmd } };
        var page = await _webView.CreatePageAsync();
        return await _webView.RunAsync(async _ => await XtermPaneSurface.OpenAsync(page, options));
    }

    private static string WaitForScreen(XtermPaneSurface surface, string contains, int timeoutMs = 30000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var text = "";
        while (Environment.TickCount64 < deadline)
        {
            text = surface.ReadScreenText(all: true, lines: null) ?? "";
            if (text.Contains(contains)) return text;
            Thread.Sleep(50);
        }
        Assert.Fail($"screen never contained \"{contains}\"; buffer:\n{text}");
        return "";
    }

    [Fact]
    public async Task Two_surfaces_on_one_page_have_independent_shells_and_teardown()
    {
        var page = await _webView.CreatePageAsync();
        var options = new XtermSurfaceOptions { Argv = new[] { Cmd } };
        using var one = await _webView.RunAsync(async _ => await XtermPaneSurface.OpenAsync(page, options));
        using var two = await _webView.RunAsync(async _ => await XtermPaneSurface.OpenAsync(page, options));
        Assert.NotEqual(one.PaneToken, two.PaneToken);

        WaitForScreen(one, ">");
        WaitForScreen(two, ">");
        Assert.True(one.Inject("echo pane-one-marker\n"));
        Assert.True(two.Inject("echo pane-two-marker\n"));
        var oneText = WaitForScreen(one, "pane-one-marker");
        var twoText = WaitForScreen(two, "pane-two-marker");
        Assert.DoesNotContain("pane-two-marker", oneText);
        Assert.DoesNotContain("pane-one-marker", twoText);

        // tearing one down must not touch its sibling
        one.Teardown();
        Assert.Null(one.ReadScreenText(all: true, lines: null));
        Assert.True(two.Inject("echo survivor-ok\n"));
        WaitForScreen(two, "survivor-ok");
    }

    [Fact]
    public async Task Restored_output_is_shown_and_captured_again_without_the_alternate_screen()
    {
        using var surface = await OpenAsync(new XtermSurfaceOptions
        {
            Argv = new[] { Cmd, "/c", "echo surface-live-marker" },
            RestoredOutput = "surface-restored-marker",
        });
        WaitForScreen(surface, "surface-live-marker");
        Assert.Contains("surface-restored-marker", surface.ReadScreenText(all: true, lines: null));

        await _webView.RunAsync<object?>(_ =>
        {
            surface.Pane.Terminal.FeedOutput(System.Text.Encoding.UTF8.GetBytes("\x1b[?1049halt-screen-only"));
            return Task.FromResult<object?>(null);
        });
        WaitForScreen(surface, "alt-screen-only");

        var captured = await surface.CaptureScrollbackAsync();
        Assert.NotNull(captured);
        Assert.Contains("surface-restored-marker", captured);
        Assert.Contains("surface-live-marker", captured);
        Assert.DoesNotContain("alt-screen-only", captured);
        Assert.DoesNotContain("\x1b[?1049h", captured);
    }

    [Fact]
    public async Task Capture_after_teardown_is_null()
    {
        var surface = await OpenAsync();
        surface.Teardown();
        Assert.Null(await surface.CaptureScrollbackAsync());
    }

    [Fact]
    public async Task Inject_types_keystrokes_with_Enter_as_CR()
    {
        using var surface = await OpenAsync();
        WaitForScreen(surface, ">");
        // the \n MUST land as Enter (CR) — cmd runs the line and echoes the output
        Assert.True(surface.Inject("echo surface-inject-ok\n"));
        WaitForScreen(surface, "surface-inject-ok");
    }

    [Fact]
    public async Task Initial_input_is_injected_after_the_shell_starts()
    {
        using var surface = await OpenAsync(new XtermSurfaceOptions
        {
            Argv = new[] { Cmd },
            InitialInput = "echo initial-input-ok\n",
        });

        WaitForScreen(surface, "initial-input-ok");
    }

    [Fact]
    public async Task ReadScreenText_lines_returns_the_tail()
    {
        using var surface = await OpenAsync();
        WaitForScreen(surface, ">");
        surface.Inject("echo tail-marker-xyz\n");
        WaitForScreen(surface, "tail-marker-xyz");
        var tail = surface.ReadScreenText(all: false, lines: 3) ?? "";
        Assert.Contains("tail-marker", tail);
        Assert.True(tail.Split('\n').Length <= 3, $"expected ≤3 lines, got:\n{tail}");
    }

    [Fact]
    public async Task Selection_is_null_until_SelectAll_captures_the_buffer()
    {
        using var surface = await OpenAsync();
        WaitForScreen(surface, ">");
        surface.Inject("echo select-me-please\n");
        WaitForScreen(surface, "select-me-please");
        Assert.Null(surface.SelectedText);
        surface.SelectAll();
        var selection = surface.SelectedText;
        Assert.NotNull(selection);
        Assert.Contains("select-me-please", selection);
    }

    [Fact]
    public async Task Font_binding_actions_step_and_reset_the_live_size()
    {
        using var surface = await OpenAsync(new XtermSurfaceOptions
        {
            Argv = new[] { Cmd },
            BaseFontSize = 14,
        });
        WaitForScreen(surface, ">");
        Assert.True(surface.PerformBindingAction("increase_font_size:1"));
        Assert.Equal(15, surface.FontSize);
        Assert.True(surface.PerformBindingAction("increase_font_size:1"));
        Assert.Equal(16, surface.FontSize);
        Assert.True(surface.PerformBindingAction("decrease_font_size:1"));
        Assert.Equal(15, surface.FontSize);
        Assert.True(surface.PerformBindingAction("reset_font_size"));
        Assert.Equal(14, surface.FontSize);
    }

    [Fact]
    public async Task Pane_token_is_baked_into_the_shell_environment()
    {
        using var surface = await OpenAsync();
        Assert.False(string.IsNullOrWhiteSpace(surface.PaneToken));
        WaitForScreen(surface, ">");
        surface.Inject("echo token=%AGTERM_PANE_ID%\n");
        WaitForScreen(surface, $"token={surface.PaneToken}");
    }

    [Fact]
    public async Task Shell_is_told_the_declared_terminal_and_not_the_launchers()
    {
        // the identification a TUI negotiates against: TERM_PROGRAM declared (read first and
        // returned verbatim, so it IS the terminal name), the launcher's value beaten, and an
        // inherited WT_SESSION gone, since it would make Claude Code demand a CSI ? u reply that
        // ConPTY never lets through (PLATFORM-NOTES).
        Environment.SetEnvironmentVariable("TERM_PROGRAM", "zed");
        Environment.SetEnvironmentVariable("WT_SESSION", "inherited-from-the-launcher");
        try
        {
            using var surface = await OpenAsync();
            WaitForScreen(surface, ">");
            // the command line is echoed by the prompt too, so read the RESULT line: it is the
            // one carrying the marker, and cmd leaves an unset variable's reference verbatim
            surface.Inject("echo mark wt=[%WT_SESSION%] prog=[%TERM_PROGRAM%] term=[%TERM%]\n");
            var screen = WaitForScreen(surface, "term=[xterm-256color]");
            var line = screen.Split('\n').Last(l => l.Contains("mark wt=[") && !l.Contains("echo mark"));
            Assert.DoesNotContain("prog=[zed]", line);
            Assert.Contains("prog=[ghostty]", line); // declared, so the launcher's value loses
            Assert.DoesNotContain("inherited-from-the-launcher", line);
            Assert.Contains("wt=[%WT_SESSION%]", line); // removed, so never expanded
        }
        finally
        {
            Environment.SetEnvironmentVariable("TERM_PROGRAM", null);
            Environment.SetEnvironmentVariable("WT_SESSION", null);
        }
    }

    [Fact]
    public async Task Paste_types_the_clipboard_through_xterm()
    {
        using var surface = await OpenAsync(new XtermSurfaceOptions
        {
            Argv = new[] { Cmd },
            ReadClipboard = () => "clip-payload-42",
        });
        WaitForScreen(surface, ">");
        Assert.True(surface.Paste());
        WaitForScreen(surface, "clip-payload-42");
    }

    [Fact]
    public async Task Teardown_kills_the_child()
    {
        var surface = await OpenAsync();
        WaitForScreen(surface, ">");
        surface.Teardown();
        await surface.Pane.Exited.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(surface.Inject("dead\n"));
    }

    // O3.5 — OpenAsync must respect a pre-canceled token and throw OperationCanceledException
    // without leaving a pane registered on the page.
    // NOTE: no public pane-count API exists on XtermPage, so the assertion is the exception alone.
    [Fact]
    public async Task OpenAsync_with_pre_canceled_token_throws()
    {
        var page = await _webView.CreatePageAsync();
        var options = new XtermSurfaceOptions { Argv = new[] { Cmd } };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _webView.RunAsync(async _ =>
                await XtermPaneSurface.OpenAsync(page, options, cts.Token)));
    }

    [Fact]
    public async Task ApplyBaseFontSize_resets_zoom_and_changes_future_reset_target()
    {
        using var surface = await OpenAsync(new XtermSurfaceOptions
        {
            Argv = new[] { Cmd },
            BaseFontSize = 14,
        });
        WaitForScreen(surface, ">");

        // zoom up twice: 14 -> 15 -> 16
        Assert.True(surface.PerformBindingAction("increase_font_size:1"));
        Assert.True(surface.PerformBindingAction("increase_font_size:1"));
        Assert.Equal(16, surface.FontSize);

        // applying a new base size must: clear runtime zoom and change the reset target
        surface.ApplyBaseFontSize(18);
        Assert.Equal(18, surface.FontSize);

        // zoom once more: 18 -> 19
        Assert.True(surface.PerformBindingAction("increase_font_size:1"));
        Assert.Equal(19, surface.FontSize);

        // reset must return to the new base, not to the original 14
        Assert.True(surface.PerformBindingAction("reset_font_size"));
        Assert.Equal(18, surface.FontSize);
    }
}
