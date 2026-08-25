using AgTerm.Terminal;

namespace AgTerm.Xterm;

/// <summary>How a pane's child spawns (argv per the §6 --command contract; a plain shell is just
/// argv = [shell]).</summary>
public sealed record TerminalPaneOptions
{
    public required IReadOnlyList<string> Argv { get; init; }

    public string? Cwd { get; init; }

    /// <summary>Extra child environment (e.g. the surface's AGTERM_PANE_ID token).</summary>
    public IReadOnlyDictionary<string, string>? ExtraEnvironment { get; init; }
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

    private TerminalPane(XtermPage page, XtermTerminal terminal, ConPtySession pty, SynchronizationContext ui)
    {
        Page = page;
        Terminal = terminal;
        Pty = pty;
        _ui = ui;
        terminal.DataReceived += pty.WriteText;
        terminal.Resized += pty.Resize;
        terminal.TitleChanged += t => TitleChanged?.Invoke(t);
        terminal.CwdChanged += c => CwdChanged?.Invoke(c);
    }

    /// <summary>The shared page hosting this pane's renderer (show/close address it by pane id).</summary>
    public XtermPage Page { get; }

    public XtermTerminal Terminal { get; }

    public ConPtySession Pty { get; }

    /// <summary>Completes with the child's exit code.</summary>
    public Task<int> Exited => Pty.Exited;

    /// <summary>OSC 0/1/2 → Session.OscTitle (host wires it).</summary>
    public event Action<string>? TitleChanged;

    /// <summary>OSC 7 / OSC 9;9, parsed → Session.CurrentCwd (host wires it).</summary>
    public event Action<string>? CwdChanged;

    /// <summary>Open a renderer pane on the (already booted) shared page, then spawn the child at
    /// the pane's fitted size. Must run on the WebView2 UI thread.</summary>
    public static async Task<TerminalPane> OpenAsync(
        XtermPage page,
        string paneId,
        TerminalPaneOptions options,
        CancellationToken cancellationToken = default)
    {
        var ui = SynchronizationContext.Current
            ?? throw new InvalidOperationException("TerminalPane.OpenAsync requires the WebView2 UI thread");

        cancellationToken.ThrowIfCancellationRequested();

        XtermTerminal? terminal = null;
        TerminalPane? pane = null;
        ConPtySession? pty = null;

        try
        {
            terminal = await page.OpenTerminalAsync(paneId);

            cancellationToken.ThrowIfCancellationRequested();

            pty = ConPtySession.Start(new ConPtyStartInfo
            {
                Argv = options.Argv,
                Cwd = options.Cwd,
                ExtraEnvironment = options.ExtraEnvironment,
                Cols = terminal.Cols > 0 ? terminal.Cols : 80,
                Rows = terminal.Rows > 0 ? terminal.Rows : 24,
                // reader-thread chunks marshal to the UI thread; drop what arrives after teardown
                OnOutput = chunk => ui.Post(_ =>
                {
                    if (pane is not { _disposed: true }) terminal.FeedOutput(chunk);
                }, null),
            });
            pane = new TerminalPane(page, terminal, pty, ui);

            cancellationToken.ThrowIfCancellationRequested();
            return pane;
        }
        catch (Exception)
        {
            if (pane is not null)
            {
                pane.Dispose();
            }
            else
            {
                pty?.Dispose();
            }

            if (terminal is not null)
            {
                await ClosePaneBestEffortAsync(page, paneId);
            }

            throw;
        }
    }

    /// <summary>Marshal work onto the WebView2 UI thread (all XtermTerminal calls are thread-affine
    /// there). Callers off the UI thread may block on the returned task; blocking ON the UI thread
    /// would deadlock its own queue.</summary>
    public Task<T> RunOnUiAsync<T>(Func<Task<T>> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(async void (_) =>
        {
            try
            {
                done.TrySetResult(await work());
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        }, null);
        return done.Task;
    }

    public void Dispose()
    {
        _disposed = true;
        Pty.Dispose();
    }

    private static async Task ClosePaneBestEffortAsync(XtermPage page, string paneId)
    {
        try
        {
            await page.ClosePaneAsync(paneId);
        }
        catch (Exception)
        {
        }
    }
}
