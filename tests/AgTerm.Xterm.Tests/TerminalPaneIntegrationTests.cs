using System.Text;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// The full live loop: real ConPTY child ↔ real xterm.js in a real (hidden) WebView2 — the same
/// assets and bridge the app ships. cmd.exe drives (always present).
/// </summary>
[Collection("webview2")]
public class TerminalPaneIntegrationTests
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;

    public TerminalPaneIntegrationTests(WebView2Fixture webView)
    {
        _webView = webView;
    }

    private async Task<TerminalPane> OpenAsync(params string[] argv)
    {
        var page = await _webView.CreatePageAsync();
        return await _webView.RunAsync(async _ =>
            await TerminalPane.OpenAsync(
                page,
                Guid.NewGuid().ToString("N"),
                new TerminalPaneOptions { Argv = argv }));
    }

    private async Task<string> WaitForTextAsync(TerminalPane pane, string contains, int timeoutMs = 30000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var text = "";
        while (Environment.TickCount64 < deadline)
        {
            text = await _webView.RunAsync(_ => pane.Terminal.ReadTextAsync(all: true, lines: null));
            if (text.Contains(contains)) return text;
            await Task.Delay(50);
        }
        Assert.Fail($"terminal text never contained \"{contains}\"; buffer:\n{text}");
        return "";
    }

    [Fact]
    public async Task Shell_output_renders_into_the_xterm_buffer()
    {
        using var pane = await OpenAsync(Cmd, "/c", "echo xterm-live-marker");
        await WaitForTextAsync(pane, "xterm-live-marker");
    }

    [Fact]
    public async Task Pty_gets_the_xterm_size_and_input_typed_into_the_pty_renders()
    {
        using var pane = await OpenAsync(Cmd);
        Assert.True(pane.Terminal.Cols > 0 && pane.Terminal.Rows > 0);
        await WaitForTextAsync(pane, ">");
        pane.Pty.WriteText("echo live-roundtrip-ok\r");
        await WaitForTextAsync(pane, "live-roundtrip-ok");
        pane.Pty.WriteText("exit\r");
        Assert.Equal(0, await pane.Exited.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Title_change_in_the_shell_reaches_TitleChanged()
    {
        using var pane = await OpenAsync(Cmd);
        var titles = new List<string>();
        pane.TitleChanged += t => { lock (titles) titles.Add(t); };
        await WaitForTextAsync(pane, ">");
        pane.Pty.WriteText("title live-title-xyz\r");
        var deadline = Environment.TickCount64 + 30000;
        while (Environment.TickCount64 < deadline)
        {
            lock (titles)
            {
                if (titles.Any(t => t.Contains("live-title-xyz"))) return;
            }
            await Task.Delay(50);
        }
        lock (titles) Assert.Fail($"no title with the marker; saw: {string.Join(" | ", titles)}");
    }

    [Fact]
    public async Task Osc99_report_reaches_CwdChanged_parsed()
    {
        using var pane = await OpenAsync(Cmd);
        var cwds = new List<string>();
        pane.CwdChanged += c => { lock (cwds) cwds.Add(c); };
        // synthetic OSC 9;9 straight through the renderer (what a configured prompt would emit)
        await _webView.RunAsync<object?>(_ =>
        {
            pane.Terminal.FeedOutput(Encoding.UTF8.GetBytes("\x1b]9;9;\"C:\\git\\proj\"\x1b\\"));
            return Task.FromResult<object?>(null);
        });
        var deadline = Environment.TickCount64 + 30000;
        while (Environment.TickCount64 < deadline)
        {
            lock (cwds)
            {
                if (cwds.Contains("C:\\git\\proj")) return;
            }
            await Task.Delay(50);
        }
        lock (cwds) Assert.Fail($"no parsed cwd; saw: {string.Join(" | ", cwds)}");
    }
}
