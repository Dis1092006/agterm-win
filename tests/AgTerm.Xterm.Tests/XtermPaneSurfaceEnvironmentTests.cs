using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// A surface child never sees an AGTERM_* value inherited from the app process, for example when
/// the app was started from a pane of another agterm-win instance. Real ConPTY child.
/// </summary>
[Collection("process-environment")]
public class XtermPaneSurfaceEnvironmentTests
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;

    public XtermPaneSurfaceEnvironmentTests(WebView2Fixture webView)
    {
        _webView = webView;
    }

    private async Task<XtermPaneSurface> OpenAsync(XtermSurfaceOptions options)
    {
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
    public async Task Inherited_agterm_variables_never_reach_the_child()
    {
        var saved = SurfaceEnvironment.OwnedVariables.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var name in SurfaceEnvironment.OwnedVariables)
                Environment.SetEnvironmentVariable(name, "foreign");

            var windowId = Guid.NewGuid();
            var window = windowId.ToString("D").ToUpperInvariant();
            using var surface = await OpenAsync(new XtermSurfaceOptions
            {
                Argv = new[] { Cmd },
                ChildEnvironment = _ => SurfaceEnvironment.QuickTerminal(windowId, @"C:\s\agterm.sock"),
            });
            WaitForScreen(surface, ">");
            surface.Inject("echo [%AGTERM_ENABLED%][%AGTERM_SESSION_ID%][%AGTERM_WINDOW_ID%][%AGTERM_WORKSPACE_ID%]"
                + "[%AGTERM_SOCKET%][%AGTERM_PANE%][%AGTERM_PANE_ID%]\n");

            // cmd leaves an undefined %X% as is, so a removed name echoes its own reference. The window
            // id is not on the command line, so this matches the result line only.
            WaitForScreen(surface,
                $"[1][%AGTERM_SESSION_ID%][{window}][%AGTERM_WORKSPACE_ID%]"
                + @"[C:\s\agterm.sock][%AGTERM_PANE%][%AGTERM_PANE_ID%]");
        }
        finally
        {
            foreach (var (name, value) in saved)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
