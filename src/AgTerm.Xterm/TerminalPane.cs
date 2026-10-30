using AgTerm.Core.Model;
using AgTerm.Terminal;

namespace AgTerm.Xterm;

/// <summary>How a pane's child spawns (argv per the §6 --command contract; a plain shell is just
/// argv = [shell]).</summary>
public sealed record TerminalPaneOptions
{
    public required IReadOnlyList<string> Argv { get; init; }

    /// <summary>The exact CreateProcessW command line; null quotes Argv (see ConPtyStartInfo).</summary>
    public string? CommandLine { get; init; }

    public string? Cwd { get; init; }

    /// <summary>Extra child environment (e.g. the terminal identity and the surface's AGTERM_* set).</summary>
    public IReadOnlyDictionary<string, string>? ExtraEnvironment { get; init; }

    /// <summary>Inherited variables to drop from the child (see TerminalIdentity).</summary>
    public IReadOnlyList<string>? RemoveEnvironment { get; init; }

    /// <summary>The pane's own theme (its workspace's), or null for the global theme.</summary>
    public TerminalTheme? Theme { get; init; }
    /// <summary>Saved VT output of this pane from the previous run, shown above the new child's
    /// output. Null = a fresh pane.</summary>
    public string? RestoredOutput { get; init; }
}

/// <summary>
/// One live terminal pane: a ConPTY child cross-wired with an xterm.js renderer.
/// Output pumps pty → xterm (marshaled to the WebView2 UI thread); keystrokes pump
/// xterm → pty; a fit-recomputed size resizes the pty. Created ON the WebView2 UI
/// thread (OpenAsync captures its SynchronizationContext for the output marshaling).
/// </summary>
public sealed class TerminalPane : IDisposable
{
    /// <summary>The dim line written between restored output and the new child's output.</summary>
    public const string RestoredBannerText = "-- saved output, new shell below --";

    /// <summary>The line written under the output of a pane held open after its child exited.</summary>
    public static string ExitedBannerText(int exitCode) =>
        $"[process exited with code {exitCode}; press any key to close]";

    private readonly SynchronizationContext _ui;
    private int _disposed;
    private int _outputStopped;
    private int _terminated;
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
            terminal = await page.OpenTerminalAsync(paneId, theme: options.Theme,
                readlineMarker: ShellIntegration.ReportsReadline(options.Argv));

            cancellationToken.ThrowIfCancellationRequested();

            var restored = !string.IsNullOrEmpty(options.RestoredOutput);
            if (restored)
            {
                // posted before any pty output, so the page renders it first
                terminal.FeedOutput(System.Text.Encoding.UTF8.GetBytes(
                    options.RestoredOutput + "\x1b[0m\r\n\x1b[2m" + RestoredBannerText + "\x1b[0m\r\n"));
            }
            outputPump = new TerminalOutputPump(
                new TerminalOutputPumpOptions(),
                chunk => PostBatchToUiAndFeedAsync(ui, terminal, chunk, () => pane is { IsOutputStopped: true }));

            pty = ConPtySession.Start(new ConPtyStartInfo
            {
                Argv = options.Argv,
                CommandLine = options.CommandLine,
                Cwd = options.Cwd,
                ExtraEnvironment = options.ExtraEnvironment,
                RemoveEnvironment = options.RemoveEnvironment,
                // ConPTY would otherwise clear the screen with the restored output on it; its
                // cursor-position request is answered by xterm.js through DataReceived
                InheritCursor = restored,
                Cols = terminal.Cols > 0 ? terminal.Cols : 80,
                Rows = terminal.Rows > 0 ? terminal.Rows : 24,
                OnOutput = chunk =>
                {
                    try
                    {
                        outputPump.WriteAsync(chunk).AsTask().GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) when (openTearingDown || pane is { IsOutputStopped: true })
                    {
                    }
                    catch (ObjectDisposedException) when (openTearingDown || pane is { IsOutputStopped: true })
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

    /// <summary>Writes the exit banner and completes on the next keystroke the pane passes to xterm.
    /// Mouse input, focus reports and terminal replies are not keystrokes and do not complete it.</summary>
    public async Task ShowExitAndWaitForKeystrokeAsync(int exitCode)
    {
        var pressed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnKeystroke(bool _) => pressed.TrySetResult();
        Terminal.UserKeystroke += OnKeystroke;
        try
        {
            await RunOnUiAsync<object?>(() =>
            {
                Terminal.FeedOutput(System.Text.Encoding.UTF8.GetBytes(
                    "\x1b[0m\r\n\x1b[2m" + ExitedBannerText(exitCode) + "\x1b[0m\r\n"));
                return Task.FromResult<object?>(null);
            }).ConfigureAwait(false);
            await pressed.Task.ConfigureAwait(false);
        }
        finally
        {
            Terminal.UserKeystroke -= OnKeystroke;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        StopOutput();
        Terminate();
    }

    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    internal bool IsOutputStopped => Volatile.Read(ref _outputStopped) != 0;

    internal void StopOutput()
    {
        if (Interlocked.Exchange(ref _outputStopped, 1) != 0)
        {
            return;
        }

        OutputPump.Dispose();
    }

    internal void Terminate()
    {
        if (Interlocked.Exchange(ref _terminated, 1) != 0)
        {
            return;
        }

        Pty.Dispose();
    }

    private async Task<int> AwaitExitAndFlushAsync()
    {
        var exitCode = await Pty.Exited.ConfigureAwait(false);
        await Pty.OutputCompleted.ConfigureAwait(false);

        try
        {
            await OutputPump.CompleteAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (IsOutputStopped)
        {
        }
        catch (ObjectDisposedException) when (IsOutputStopped)
        {
        }

        return exitCode;
    }

    private static Task PostBatchToUiAndFeedAsync(
        SynchronizationContext ui,
        XtermTerminal terminal,
        ReadOnlyMemory<byte> chunk,
        Func<bool> isOutputStopped)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            ui.Post(static state =>
            {
                var callbackState = (OutputFeedCallbackState)state!;
                try
                {
                    if (!callbackState.IsOutputStopped())
                    {
                        callbackState.Terminal.FeedOutput(callbackState.Chunk);
                    }

                    callbackState.Done.TrySetResult();
                }
                catch (Exception ex)
                {
                    callbackState.Done.TrySetException(ex);
                }
                }, new OutputFeedCallbackState(terminal, chunk, isOutputStopped, done));
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
        Func<bool> IsOutputStopped,
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
