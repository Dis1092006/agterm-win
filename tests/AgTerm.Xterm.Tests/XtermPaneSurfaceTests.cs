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

    private async Task<XtermPaneSurface> OpenAsync(Action<XtermSurfaceOptions>? mutate = null)
    {
        var options = new XtermSurfaceOptions { Argv = new[] { Cmd } };
        mutate?.Invoke(options);
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
    public async Task Inject_types_keystrokes_with_Enter_as_CR()
    {
        using var surface = await OpenAsync();
        WaitForScreen(surface, ">");
        // the \n MUST land as Enter (CR) — cmd runs the line and echoes the output
        Assert.True(surface.Inject("echo surface-inject-ok\n"));
        WaitForScreen(surface, "surface-inject-ok");
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
        using var surface = await OpenAsync(o => o.BaseFontSize = 14);
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
    public async Task Paste_types_the_clipboard_through_xterm()
    {
        using var surface = await OpenAsync(o => o.ReadClipboard = () => "clip-payload-42");
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
}
