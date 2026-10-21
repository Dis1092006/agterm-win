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
    public async Task Restored_output_stays_above_the_new_shell_output()
    {
        var page = await _webView.CreatePageAsync();
        using var pane = await _webView.RunAsync(async _ =>
            await TerminalPane.OpenAsync(
                page,
                Guid.NewGuid().ToString("N"),
                new TerminalPaneOptions
                {
                    Argv = [Cmd, "/c", "echo live-after-restore"],
                    RestoredOutput = "restored-line-1\r\n\x1b[32mrestored-line-2\x1b[0m",
                }));

        var text = await WaitForTextAsync(pane, "live-after-restore");
        var first = text.IndexOf("restored-line-1", StringComparison.Ordinal);
        var second = text.IndexOf("restored-line-2", StringComparison.Ordinal);
        var banner = text.IndexOf("-- saved output, new shell below --", StringComparison.Ordinal);
        var live = text.IndexOf("live-after-restore", StringComparison.Ordinal);
        Assert.True(first >= 0 && first < second && second < banner && banner < live, text);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Only_a_pane_running_the_integrated_powershell_listens_for_the_readline_marker(bool powershell)
    {
        var view = await _webView.CreateWebViewAsync();
        var page = await _webView.RunAsync(async _ =>
        {
            var p = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await p.InitializeAsync();
            return p;
        });
        // the script does not have to exist: only the argv shape decides
        var argv = powershell
            ? AgTerm.Terminal.ShellIntegration.Inject(["powershell.exe"], @"C:\agterm-missing-integration")
            : AgTerm.Terminal.ShellIntegration.Inject([Cmd], @"C:\agterm-missing-integration");
        var paneId = Guid.NewGuid().ToString("N");
        using var pane = await _webView.RunAsync(async _ =>
            await TerminalPane.OpenAsync(page, paneId, new TerminalPaneOptions { Argv = argv }));

        var listening = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync($"agterm.readlineState(\"{paneId}\").listening"));

        Assert.Equal(powershell ? "true" : "false", listening);
    }

    [Fact]
    public async Task Output_pump_delivers_all_lines_and_stays_within_memory_budget()
    {
        using var pane = await OpenAsync(Cmd, "/d", "/c",
            "for /L %i in (1,1,1500) do @echo pump-line-%i & echo pump-final-marker");

        Assert.Equal(0, await pane.Exited.WaitAsync(TimeSpan.FromSeconds(30)));

        var text = await _webView.RunAsync(_ => pane.Terminal.ReadTextAsync(all: true, lines: null));
        Assert.Contains("pump-line-1", text);
        Assert.Contains("pump-line-750", text);
        Assert.Contains("pump-line-1500", text);
        Assert.Contains("pump-final-marker", text);
        Assert.True(pane.OutputPump.MaxObservedQueueBytes <= 1 * 1024 * 1024,
            $"OutputPump exceeded 1 MB budget: {pane.OutputPump.MaxObservedQueueBytes} bytes");
    }
}
