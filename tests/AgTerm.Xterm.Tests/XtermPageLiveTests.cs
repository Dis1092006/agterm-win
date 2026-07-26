using System.Text;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// Bridge v2 against the REAL page: one WebView2, many xterm.js instances. Verifies per-pane
/// isolation (buffers, titles), show/hide refits, and pane teardown — the live twin of the
/// FakeScriptHost routing tests.
/// </summary>
[Collection("webview2")]
public class XtermPageLiveTests
{
    private readonly WebView2Fixture _webView;

    public XtermPageLiveTests(WebView2Fixture webView)
    {
        _webView = webView;
    }

    private async Task<(XtermPage Page, Microsoft.Web.WebView2.WinForms.WebView2 View)> BootPageAsync()
    {
        var view = await _webView.CreateWebViewAsync();
        var page = await _webView.RunAsync(async _ =>
        {
            var p = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await p.InitializeAsync();
            return p;
        });
        return (page, view);
    }

    private static async Task<string> WaitForTextAsync(XtermTerminal terminal, string contains,
        Func<Func<Task<string>>, Task<string>> onUi, int timeoutMs = 30000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var text = "";
        while (Environment.TickCount64 < deadline)
        {
            text = await onUi(() => terminal.ReadTextAsync(all: true, lines: null));
            if (text.Contains(contains)) return text;
            await Task.Delay(50);
        }
        Assert.Fail($"pane text never contained \"{contains}\"; buffer:\n{text}");
        return "";
    }

    [Fact]
    public async Task Two_panes_have_isolated_buffers_and_title_routing()
    {
        var (page, _) = await BootPageAsync();
        var one = await _webView.RunAsync(_ => page.OpenTerminalAsync("live-p1"));
        var two = await _webView.RunAsync(_ => page.OpenTerminalAsync("live-p2"));
        Assert.True(one.Cols > 0 && one.Rows > 0, $"p1 grid: {one.Cols}x{one.Rows}");
        Assert.True(two.Cols > 0 && two.Rows > 0, $"p2 grid: {two.Cols}x{two.Rows}");

        var oneTitles = new List<string>();
        var twoTitles = new List<string>();
        one.TitleChanged += oneTitles.Add;
        two.TitleChanged += twoTitles.Add;

        await _webView.RunAsync<object?>(_ =>
        {
            one.FeedOutput(Encoding.UTF8.GetBytes("hello-from-one\r\n\x1b]0;title-one\x07"));
            two.FeedOutput(Encoding.UTF8.GetBytes("hello-from-two\r\n\x1b]0;title-two\x07"));
            return Task.FromResult<object?>(null);
        });

        Func<Func<Task<string>>, Task<string>> onUi = work => _webView.RunAsync(_ => work());
        var oneText = await WaitForTextAsync(one, "hello-from-one", onUi);
        var twoText = await WaitForTextAsync(two, "hello-from-two", onUi);
        Assert.DoesNotContain("hello-from-two", oneText);
        Assert.DoesNotContain("hello-from-one", twoText);

        // OSC title reports must land on their own pane's event only
        var deadline = Environment.TickCount64 + 30000;
        while ((oneTitles.Count == 0 || twoTitles.Count == 0) && Environment.TickCount64 < deadline)
            await Task.Delay(50);
        Assert.Contains("title-one", oneTitles);
        Assert.DoesNotContain("title-two", oneTitles);
        Assert.Contains("title-two", twoTitles);
        Assert.DoesNotContain("title-one", twoTitles);
    }

    [Fact]
    public async Task ShowPane_refits_the_shown_pane_and_reports_its_resize()
    {
        var (page, _) = await BootPageAsync();
        var one = await _webView.RunAsync(_ => page.OpenTerminalAsync("live-show1"));
        var two = await _webView.RunAsync(_ => page.OpenTerminalAsync("live-show2"));

        var resized = new TaskCompletionSource<(int, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
        two.Resized += (c, r) => resized.TrySetResult((c, r));
        await _webView.RunAsync<object?>(async _ => { await page.ShowPaneAsync("live-show2"); return null; });

        var (cols, rows) = await resized.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(cols > 0 && rows > 0, $"shown pane grid: {cols}x{rows}");
        Assert.Equal(cols, two.Cols);
        _ = one; // the hidden pane stays registered; no resize is required of it
    }

    [Fact]
    public async Task Intercepted_chords_come_back_as_chord_messages_and_skip_the_shell()
    {
        var (page, view) = await BootPageAsync();
        var one = await _webView.RunAsync(_ => page.OpenTerminalAsync("live-chord1"));
        var data = new List<string>();
        var chords = new List<(string, AgTerm.Core.Keymap.PhysicalChord)>();
        one.DataReceived += data.Add;
        page.ChordPressed += (paneId, chord) => chords.Add((paneId, chord));

        await _webView.RunAsync<object?>(async _ =>
        {
            await page.SetInterceptChordsAsync(
                new[] { new AgTerm.Core.Keymap.PhysicalChord(true, false, false, "n") });
            return null;
        });
        // synthetic keydowns through the real xterm key path (test-only page hook), on the
        // page's OWN WebView2 (the fixture's first view hosts a different page)
        await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
            "agterm.dispatchKey(\"live-chord1\", {key:'n', ctrlKey:true})"));
        await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
            "agterm.dispatchKey(\"live-chord1\", {key:'u', ctrlKey:true})"));

        var deadline = Environment.TickCount64 + 30000;
        while (chords.Count == 0 && Environment.TickCount64 < deadline) await Task.Delay(50);
        var hit = Assert.Single(chords);
        Assert.Equal("live-chord1", hit.Item1);
        Assert.Equal(new AgTerm.Core.Keymap.PhysicalChord(true, false, false, "n"), hit.Item2);
        // the intercepted Ctrl+N must NOT have reached the pty as ^N (0x0E); the uncaptured
        // Ctrl+U may (that's the point of the allowlist)
        Assert.DoesNotContain(data, d => d.Contains('\x0E'));
    }

    [Fact]
    public async Task ClosePane_disposes_the_terminal_so_reads_report_null()
    {
        var (page, view) = await BootPageAsync();
        var one = await _webView.RunAsync(_ => page.OpenTerminalAsync("live-close1"));
        // the pane answers before the close...
        var before = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync("agterm.text(\"live-close1\", true, null)"));
        Assert.NotEqual("null", before);
        await _webView.RunAsync<object?>(async _ => { await page.ClosePaneAsync("live-close1"); return null; });
        // ...and reads report null after it (the pane and its terminal are gone)
        var raw = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync("agterm.text(\"live-close1\", true, null)"));
        Assert.Equal("null", raw);
        _ = one;
    }
}
