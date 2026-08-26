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
    private int _disposed;
    private readonly Task<int> _exited;

    private TerminalPane(
        XtermPage page,
        XtermTerminal terminal,
        ConPtySession pty,
        TerminalOutputPump outputPump,
        SynchronizationContext ui)
    {
        Page = page;
        Terminal = terminal;
        Pty = pty;
        OutputPump = outputPump;
        _ui = ui;
        _exited = AwaitExitAndFlushAsync();
        terminal.DataReceived += pty.WriteText;
        terminal.Resized += pty.Resize;
        terminal.TitleChanged += t => TitleChanged?.Invoke(t);
        terminal.CwdChanged += c => CwdChanged?.Invoke(c);
    }

    /// <summary>The shared page hosting this pane's renderer (show/close address it by pane id).</summary>
    public XtermPage Page { get; }

    public XtermTerminal Terminal { get; }

    public ConPtySession Pty { get; }

    internal TerminalOutputPump OutputPump { get; }

    /// <summary>Completes with the child's exit code.</summary>
    public Task<int> Exited => _exited;

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
        TerminalOutputPump? outputPump = null;
        var openTearingDown = false;

        try
        {
            terminal = await page.OpenTerminalAsync(paneId);

            cancellationToken.ThrowIfCancellationRequested();

            outputPump = new TerminalOutputPump(
                new TerminalOutputPumpOptions(),
                chunk => PostBatchToUiAndFeedAsync(ui, terminal, chunk, () => pane is { IsDisposed: true }));

            pty = ConPtySession.Start(new ConPtyStartInfo
            {
                Argv = options.Argv,
                Cwd = options.Cwd,
                ExtraEnvironment = options.ExtraEnvironment,
                Cols = terminal.Cols > 0 ? terminal.Cols : 80,
                Rows = terminal.Rows > 0 ? terminal.Rows : 24,
                OnOutput = chunk =>
                {
                    try
                    {
                        outputPump.WriteAsync(chunk).AsTask().GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) when (openTearingDown || pane is { IsDisposed: true })
                    {
                    }
                    catch (ObjectDisposedException) when (openTearingDown || pane is { IsDisposed: true })
                    {
                    }
                },
            });
            pane = new TerminalPane(page, terminal, pty, outputPump, ui);

            cancellationToken.ThrowIfCancellationRequested();
            return pane;
        }
        catch (Exception)
        {
            openTearingDown = true;

            if (pane is not null)
            {
                pane.Dispose();
            }
            else
            {
                outputPump?.Dispose();
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        OutputPump.Dispose();
        Pty.Dispose();
    }

    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private async Task<int> AwaitExitAndFlushAsync()
    {
        var exitCode = await Pty.Exited.ConfigureAwait(false);
        await Pty.OutputCompleted.ConfigureAwait(false);

        try
        {
            await OutputPump.CompleteAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (IsDisposed)
        {
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
        }

        return exitCode;
    }

    private static Task PostBatchToUiAndFeedAsync(
        SynchronizationContext ui,
        XtermTerminal terminal,
        ReadOnlyMemory<byte> chunk,
        Func<bool> isPaneDisposed)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            ui.Post(static state =>
            {
                var callbackState = (OutputFeedCallbackState)state!;
                try
                {
                    if (!callbackState.IsPaneDisposed())
                    {
                        callbackState.Terminal.FeedOutput(callbackState.Chunk);
                    }

                    callbackState.Done.TrySetResult();
                }
                catch (Exception ex)
                {
                    callbackState.Done.TrySetException(ex);
                }
            }, new OutputFeedCallbackState(terminal, chunk, isPaneDisposed, done));
        }
        catch (Exception ex)
        {
            done.TrySetException(ex);
        }

        return done.Task;
    }

    private sealed record OutputFeedCallbackState(
        XtermTerminal Terminal,
        ReadOnlyMemory<byte> Chunk,
        Func<bool> IsPaneDisposed,
        TaskCompletionSource Done);

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
