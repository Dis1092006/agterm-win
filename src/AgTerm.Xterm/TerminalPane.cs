using AgTerm.Terminal;

namespace AgTerm.Xterm;

/// <summary>How a pane's child spawns (argv per the §6 --command contract; a plain shell is just
/// argv = [shell]).</summary>
public sealed record TerminalPaneOptions
{
    public required IReadOnlyList<string> Argv { get; init; }

    public string? Cwd { get; init; }
}

/// <summary>
/// One live terminal pane: a ConPTY child cross-wired with an xterm.js renderer.
/// Output pumps pty → xterm (marshaled to the WebView2 UI thread); keystrokes pump
/// xterm → pty; a fit-recomputed size resizes the pty. Created ON the WebView2 UI
/// thread (OpenAsync captures its SynchronizationContext for the output marshaling).
/// </summary>
public sealed class TerminalPane : IDisposable
{
    private readonly SynchronizationContext _ui;
    private bool _disposed;

    private TerminalPane(XtermTerminal terminal, ConPtySession pty, SynchronizationContext ui)
    {
        Terminal = terminal;
        Pty = pty;
        _ui = ui;
        terminal.DataReceived += pty.WriteText;
        terminal.Resized += pty.Resize;
        terminal.TitleChanged += t => TitleChanged?.Invoke(t);
        terminal.CwdChanged += c => CwdChanged?.Invoke(c);
    }

    public XtermTerminal Terminal { get; }

    public ConPtySession Pty { get; }

    /// <summary>Completes with the child's exit code.</summary>
    public Task<int> Exited => Pty.Exited;

    /// <summary>OSC 0/1/2 → Session.OscTitle (host wires it).</summary>
    public event Action<string>? TitleChanged;

    /// <summary>OSC 7 / OSC 9;9, parsed → Session.CurrentCwd (host wires it).</summary>
    public event Action<string>? CwdChanged;

    /// <summary>Boot the renderer, then spawn the child at the renderer's fitted size.
    /// Must run on the WebView2 UI thread.</summary>
    public static async Task<TerminalPane> OpenAsync(IWebViewScriptHost host, TerminalPaneOptions options)
    {
        var ui = SynchronizationContext.Current
            ?? throw new InvalidOperationException("TerminalPane.OpenAsync requires the WebView2 UI thread");
        var terminal = new XtermTerminal(host);
        await terminal.InitializeAsync();
        TerminalPane? pane = null;
        var pty = ConPtySession.Start(new ConPtyStartInfo
        {
            Argv = options.Argv,
            Cwd = options.Cwd,
            Cols = terminal.Cols > 0 ? terminal.Cols : 80,
            Rows = terminal.Rows > 0 ? terminal.Rows : 24,
            // reader-thread chunks marshal to the UI thread; drop what arrives after teardown
            OnOutput = chunk => ui.Post(_ =>
            {
                if (pane is not { _disposed: true }) terminal.FeedOutput(chunk);
            }, null),
        });
        pane = new TerminalPane(terminal, pty, ui);
        return pane;
    }

    public void Dispose()
    {
        _disposed = true;
        Pty.Dispose();
    }
}
