using System.Globalization;
using System.Runtime.ExceptionServices;
using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Terminal;

namespace AgTerm.Xterm;

/// <summary>How a live surface spawns. Argv per the §6 --command contract (a plain shell is
/// argv = [shell]); clipboard access is injected because the surface itself is UI-framework-free.</summary>
public sealed class XtermSurfaceOptions
{
    public required IReadOnlyList<string> Argv { get; init; }

    /// <summary>The exact CreateProcessW command line; null quotes Argv (see ConPtyStartInfo).</summary>
    public string? CommandLine { get; init; }

    public string? Cwd { get; init; }

    /// <summary>Optional startup keystrokes injected after the child is spawned and cancellation
    /// is re-checked. Newlines are normalized to terminal Enter (CR) via KeystrokeText.</summary>
    public string? InitialInput { get; init; }

    /// <summary>What reset_font_size returns to (upstream: the engine config's font size).</summary>
    public double BaseFontSize { get; init; } = 14;

    /// <summary>System clipboard reader for session.paste; null = paste is a no-op.</summary>
    public Func<string?>? ReadClipboard { get; init; }

    /// <summary>The theme of the pane's workspace, or null for the global theme. The host fills it
    /// when it captures the request; the window turns it into colors.</summary>
    public string? ThemeName { get; init; }
    /// <summary>Saved VT output of this pane from the previous run (see TerminalPaneOptions).</summary>
    public string? RestoredOutput { get; init; }

    /// <summary>The AGTERM_* values for this surface, given its spawn token. The host fills it when
    /// it captures the request; null sets none. A function of the token because OpenAsync creates
    /// the token.</summary>
    public Func<string, IReadOnlyDictionary<string, string>>? ChildEnvironment { get; init; }
}

/// <summary>
/// The REAL terminal surface: IHeadlessSurface (the contract every control arm drives) over a live
/// TerminalPane (ConPTY child ↔ xterm.js renderer). The interface is synchronous and called from
/// the control host's dispatch thread; every renderer touch marshals onto the WebView2 UI thread
/// and blocks (bounded) — a timeout or a torn-down pane reports as "not realized" (false/null),
/// which the arms map to the pinned wire errors, never a crash.
/// </summary>
public sealed class XtermPaneSurface : IHeadlessSurface, IFontSizeReportingSurface, ICompletedExitCodeSurface, ILiveShutdownSurface, IScrollbackSurface, IDisposable
{
    private static readonly TimeSpan UiCallBudget = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(5);

    private readonly Func<string?>? _readClipboard;
    private double _baseFontSize;
    private readonly Lock _shutdownGate = new();
    private readonly TaskCompletionSource _shutdownStarted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int _stopOutputStarted;
    private int _promotedToPrimaryPane;
    private Task? _shutdownCoreTask;

    private XtermPaneSurface(TerminalPane pane, string paneToken, XtermSurfaceOptions options)
    {
        Pane = pane;
        PaneToken = paneToken;
        _readClipboard = options.ReadClipboard;
        _baseFontSize = options.BaseFontSize;
        FontSize = options.BaseFontSize;
    }

    public TerminalPane Pane { get; }

    /// <summary>The stable spawn token, also the bridge pane id. The shell sees it as AGTERM_PANE_ID
    /// only when the host's ChildEnvironment puts it there (main, split, scratch); session.status /
    /// session.restore resolve panes by it.</summary>
    public string? PaneToken { get; }

    /// <summary>The pane's live font size (the tree read-back's fontSize source).</summary>
    public double FontSize { get; private set; }

    double? IHeadlessSurface.FontSize => FontSize;

    public event Action<double>? FontSizeChanged;

    /// <summary>Completes when teardown starts, which may be later than the child process exit for
    /// a retained --wait surface.</summary>
    public Task ShutdownStarted => _shutdownStarted.Task;

    public int? CompletedExitCode => Pane.Exited.IsCompletedSuccessfully
        ? Pane.Exited.Result
        : null;

    internal bool IsPromotedToPrimaryPane => Volatile.Read(ref _promotedToPrimaryPane) != 0;

    /// <summary>What the child gets on top of the inherited environment, and what it loses first.
    /// Every owned AGTERM_* name is removed even with no ChildEnvironment, so a value inherited from
    /// another agterm-win instance never reaches the shell. Removal runs before the extras.</summary>
    internal static (IReadOnlyDictionary<string, string> Extra, IReadOnlyList<string> Remove)
        SpawnEnvironment(XtermSurfaceOptions options, string paneToken)
    {
        var extra = new Dictionary<string, string>(TerminalIdentity.PaneEnvironment());
        if (options.ChildEnvironment?.Invoke(paneToken) is { } child)
        {
            foreach (var (name, value) in child) extra[name] = value;
        }
        var remove = TerminalIdentity.RemovedVariables.Concat(SurfaceEnvironment.OwnedVariables).ToArray();
        return (extra, remove);
    }

    /// <summary>Spawn the child with the host's environment, on the WebView2 UI thread. The pane
    /// token doubles as the bridge pane id — one identifier addresses the pane end to end.</summary>
    public static async Task<XtermPaneSurface> OpenAsync(
        XtermPage page,
        XtermSurfaceOptions options,
        CancellationToken cancellationToken = default,
        TerminalTheme? theme = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var token = Guid.NewGuid().ToString("N");
        var (extraEnvironment, removeEnvironment) = SpawnEnvironment(options, token);
        TerminalPane? pane = null;
        XtermPaneSurface? surface = null;
        try
        {
            pane = await TerminalPane.OpenAsync(page, token, new TerminalPaneOptions
            {
                Argv = options.Argv,
                CommandLine = options.CommandLine,
                Cwd = options.Cwd,
                ExtraEnvironment = extraEnvironment,
                RemoveEnvironment = removeEnvironment,
                Theme = theme,
                RestoredOutput = options.RestoredOutput,
            }, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(options.InitialInput))
            {
                pane.Pty.WriteText(KeystrokeText.ToPtyInput(options.InitialInput));
            }

            surface = new XtermPaneSurface(pane, token, options);
            if (Math.Abs(options.BaseFontSize - 14) > 0.001) // bridge default is 14
                FireAndForget(pane.Terminal.SetFontSizeAsync(options.BaseFontSize));

            cancellationToken.ThrowIfCancellationRequested();
            return surface;
        }
        catch (Exception)
        {
            if (surface is not null)
            {
                surface.Teardown();
            }
            else if (pane is not null)
            {
                pane.Dispose();
                FireAndForget(pane.RunOnUiAsync<object?>(async () =>
                {
                    await pane.Page.ClosePaneAsync(token);
                    return null;
                }));
            }

            throw;
        }
    }

    /// <summary>Bring this pane's renderer on screen (show it, hide the page's other panes).
    /// Must run on the WebView2 UI thread.</summary>
    public Task ShowAsync() => Pane.Page.ShowPaneAsync(PaneToken!);

    public bool Inject(string text)
    {
        if (IsOutputStopped) return false;
        KeystrokeText.Write(text, Pane.Pty.WriteText, Thread.Sleep);
        return true;
    }

    public string? ReadScreenText(bool all, int? lines) =>
        OnUi(() => Pane.Terminal.ReadTextAsync(all, lines));

    public async Task<string?> CaptureScrollbackAsync()
    {
        if (IsOutputStopped) return null;
        try
        {
            return await Pane.RunOnUiAsync<string?>(async () => await Pane.Terminal.SerializeForRestoreAsync())
                .WaitAsync(UiCallBudget).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool IsRealized => !IsOutputStopped;

    public string? SelectedText =>
        OnUi(() => Pane.Terminal.GetSelectionAsync()) is { Length: > 0 } text ? text : null;

    public void SelectAll() =>
        OnUi(async () => { await Pane.Terminal.SelectAllAsync(); return true; });

    public bool Paste()
    {
        if (IsOutputStopped) return false;
        if (_readClipboard?.Invoke() is not { Length: > 0 } text) return true; // realized, empty clipboard
        return OnUi(async () => { await Pane.Terminal.PasteAsync(text); return true; });
    }

    public bool PerformBindingAction(string action)
    {
        if (IsOutputStopped) return false;
        double target;
        if (action == "reset_font_size")
            target = _baseFontSize;
        else if (ParseFontStep(action, "increase_font_size:") is { } up)
            target = FontSize + up;
        else if (ParseFontStep(action, "decrease_font_size:") is { } down)
            target = FontSize - down;
        else
            return false; // not an action this surface knows — reads as unrealized upstream too
        var applied = OnUi(async () => (double?)await Pane.Terminal.SetFontSizeAsync(target));
        if (applied is not { } size) return false;
        FontSize = size;
        FontSizeChanged?.Invoke(size);
        return true;
    }

    public void ApplyBaseFontSize(double fontSize)
    {
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));

        // Page-global renderer apply runs separately; this updates reset/readback state in-place.
        _baseFontSize = fontSize;
        FontSize = fontSize;
    }

    public void Teardown()
    {
        _shutdownStarted.TrySetResult();
        StopOutput();
        ObserveFault(EnsureShutdownCoreTask());
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _shutdownStarted.TrySetResult();
        var coreTask = EnsureShutdownCoreTask();
        return cancellationToken.CanBeCanceled
            ? coreTask.WaitAsync(cancellationToken)
            : coreTask;
    }

    void ILiveShutdownSurface.StopOutput() => StopOutput();

    Task ILiveShutdownSurface.ShutdownAsync(CancellationToken cancellationToken) =>
        ShutdownAsync(cancellationToken);

    internal void StopOutput()
    {
        if (Interlocked.Exchange(ref _stopOutputStarted, 1) != 0)
        {
            return;
        }

        Pane.StopOutput();
    }

    /// <summary>Switch this surface's report and exit routing to the primary-pane role without
    /// recreating its ConPTY process or xterm instance.</summary>
    public void PromoteToPrimaryPane()
    {
        Interlocked.Exchange(ref _promotedToPrimaryPane, 1);
    }

    public void Dispose() => Teardown();

    private bool IsOutputStopped => Volatile.Read(ref _stopOutputStarted) != 0;

    private static double? ParseFontStep(string action, string prefix) =>
        action.StartsWith(prefix, StringComparison.Ordinal) &&
        double.TryParse(action.AsSpan(prefix.Length), CultureInfo.InvariantCulture, out var step)
            ? step
            : null;

    /// <summary>Run a renderer call on the UI thread and wait, bounded. Teardown, a dead renderer
    /// or a blown budget all surface as null — the arms' "session not realized" shape.</summary>
    private T? OnUi<T>(Func<Task<T>> work)
    {
        if (IsOutputStopped) return default;
        try
        {
            return Pane.RunOnUiAsync(work).WaitAsync(UiCallBudget).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return default;
        }
    }

    private Task EnsureShutdownCoreTask()
    {
        lock (_shutdownGate)
        {
            _shutdownCoreTask ??= Task.Run(ShutdownCoreAsync, CancellationToken.None);
            return _shutdownCoreTask;
        }
    }

    private async Task ShutdownCoreAsync()
    {
        StopOutput();
        Pane.Terminate();

        ExceptionDispatchInfo? exitFailure = null;
        ExceptionDispatchInfo? closeFailure = null;

        try
        {
            await Pane.Exited.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            exitFailure = ExceptionDispatchInfo.Capture(ex);
        }

        try
        {
            await Pane.RunOnUiAsync<object?>(async () =>
            {
                await Pane.Page.ClosePaneAsync(PaneToken!);
                return null;
            }).WaitAsync(ShutdownBudget).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            closeFailure = ExceptionDispatchInfo.Capture(ex);
        }

        if (exitFailure is not null && closeFailure is null)
        {
            exitFailure.Throw();
        }

        if (closeFailure is not null)
        {
            closeFailure.Throw();
        }
    }

    private static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static async void FireAndForget(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // best-effort initial font apply; the reset action re-applies it on demand
        }
    }
}
