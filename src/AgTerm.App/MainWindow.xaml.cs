using AgTerm.Terminal;
using AgTerm.Xterm;
using Microsoft.UI.Xaml;

namespace AgTerm.App;

/// <summary>
/// Phase-3 window: one terminal pane (WebView2 + xterm.js) running the default shell. The pane
/// logic lives in AgTerm.Xterm/AgTerm.Terminal and is covered by tests; this shell only wires
/// the control, the window title and teardown.
/// </summary>
public sealed partial class MainWindow : Window
{
    private TerminalPane? _pane;

    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => _pane?.Dispose();
        _ = OpenTerminalAsync();
    }

    private async Task OpenTerminalAsync()
    {
        await TerminalView.EnsureCoreWebView2Async();
        var shell = ShellResolver.Resolve();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _pane = await TerminalPane.OpenAsync(
            new WinRtScriptHost(TerminalView.CoreWebView2),
            new TerminalPaneOptions { Argv = new[] { shell }, Cwd = home });
        _pane.TitleChanged += title => DispatcherQueue.TryEnqueue(() => Title = $"{title} — agterm");
        _ = CloseOnExitAsync(_pane);
    }

    private async Task CloseOnExitAsync(TerminalPane pane)
    {
        await pane.Exited;
        DispatcherQueue.TryEnqueue(Close);
    }
}
