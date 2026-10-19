using AgTerm.Core.Install;

namespace AgTerm.Xterm.Tests;

/// <summary>After the command line tool install, a pane spawned by this process finds tools in the
/// installed directory without a restart. Real ConPTY child; the user Path goes to a scratch key.</summary>
[Collection("process-environment")]
public class CliInstallPaneTests
{
    private static readonly string Cmd = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;

    public CliInstallPaneTests(WebView2Fixture webView) => _webView = webView;

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
    public async Task A_pane_spawned_after_the_install_has_the_directory_on_PATH()
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var toolDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "agterm-cli-" + id)).FullName;
        File.WriteAllText(Path.Combine(toolDirectory, $"agterm-probe-{id}.cmd"), $"@echo probe-found-{id}\r\n");
        var scratch = new ScratchRegistryKey();
        var savedPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var env = new WindowsUserPathEnvironment(scratch.Open, () => { });
            var outcome = CliInstall.Install(toolDirectory, toolExists: true, env);
            Assert.Equal("Command Line Tool Installed", outcome.Title);

            var page = await _webView.CreatePageAsync();
            using var surface = await _webView.RunAsync(async _ =>
                await XtermPaneSurface.OpenAsync(page, new XtermSurfaceOptions { Argv = new[] { Cmd } }));
            WaitForScreen(surface, ">");
            surface.Inject($"agterm-probe-{id}\n");

            WaitForScreen(surface, $"probe-found-{id}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", savedPath);
            scratch.Dispose();
            Directory.Delete(toolDirectory, recursive: true);
        }
    }
}
