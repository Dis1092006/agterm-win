using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Terminal;
using System.Threading;

namespace AgTerm.Xterm;

internal interface ILiveShutdownSurface
{
    void StopOutput();
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

/// <summary>Construction options for <see cref="LiveControlHost"/> — the HeadlessHostOptions
/// subset the app configures, plus the live-surface machinery.</summary>
public sealed record LiveControlHostOptions
{
    public string? StateDir { get; init; }

    public string? ConfigDir { get; init; }

    public string? PipeName { get; init; }

    public string WindowName { get; init; } = "main";

    public IReadOnlyList<string> AvailableThemes { get; init; } = Array.Empty<string>();

    public string? HomeDirectory { get; init; }

    /// <summary>The configured default shell; null = the ShellResolver policy (pwsh → cmd).</summary>
    public string? DefaultShell { get; init; }

    /// <summary>System clipboard reader handed to every surface (session.paste).</summary>
    public Func<string?>? ReadClipboard { get; init; }

    /// <summary>Default base font size for newly realized surfaces.</summary>
    public double BaseFontSize { get; init; } = 14;

    /// <summary>When true, restored foreground/override commands are sent as startup input.</summary>
    public bool RestoreRunningCommand { get; init; }

    /// <summary>config.reload count callback forwarded to the headless control actions.</summary>
    public Func<int>? ConfigReload { get; init; }

    public Action<string>? Log { get; init; }

    /// <summary>Reports whether the current callback is running on the owning UI thread.</summary>
    public Func<bool> IsUiThread { get; init; } = static () => false;

    /// <summary>The UI half of realization: create a renderer (a WebView2 pane) and open the
    /// surface over it, ON the WebView2 UI thread. The host calls it from the dispatch thread and
    /// blocks (bounded) — the implementation must marshal internally and must NOT touch the host
    /// gate (RunLocked) on the UI thread.</summary>
    public required Func<SurfaceOpenRequest, CancellationToken, Task<IHeadlessSurface>> OpenSurface { get; init; }

    /// <summary>How long a realization may take before the show reports "session not realized".</summary>
    public TimeSpan RealizeBudget { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Optional cancellation signal for constructor-time bootstrap only.</summary>
    public CancellationToken BootstrapCancellationToken { get; init; } = CancellationToken.None;
}

/// <summary>
/// The app's control host: the headless host's control plane (one window, one AppStore, named-pipe
/// server, event ring, snapshot bootstrap/persistence) with sessions realizing REAL surfaces —
/// ConPTY children rendered by xterm.js — through the store's SurfaceRealizer seam. Also owns the
/// live-surface lifecycle: OSC title/cwd reports flow into the session fields (serialized via
/// RunLocked off the UI thread), and a primary shell's exit closes its pane the upstream way
/// (ClosePrimaryPane; a --wait command session is left open, showing its final screen).
/// </summary>
public sealed class LiveControlHost : IDisposable, IAsyncDisposable
{
    private readonly LiveControlHostOptions _options;
    private readonly LiveDispatchScopeFactory _scopeFactory;
    private readonly LiveStoreCoordinator _storeCoordinator;
    private readonly LiveSurfaceCoordinator? _coordinator;
    private double _currentBaseFontSize;
    private readonly object _shutdownLock = new();
    private readonly object _focusRequestLock = new();
    private readonly HashSet<Task> _focusRequestTasks = new();
    private HeadlessHost? _host;
    private Task? _shutdownTask;
    private int _shutdownStarted;

    public LiveControlHost(LiveControlHostOptions options)
    {
        _options = options;
        _currentBaseFontSize = options.BaseFontSize;
        _scopeFactory = new LiveDispatchScopeFactory();
        Host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = options.StateDir,
            ConfigDir = options.ConfigDir,
            PipeName = options.PipeName,
            WindowName = options.WindowName,
            AvailableThemes = options.AvailableThemes,
            ConfigReload = options.ConfigReload,
            HomeDirectory = options.HomeDirectory,
            RestoreRunningCommand = options.RestoreRunningCommand,
            Log = options.Log,
            SurfaceRealizer = ReserveSurface,
            DispatchScopeFactory = _scopeFactory,
            // the live app's eager deck: every session's pane exists from creation/restore, so
            // session.type works into background sessions without a select (phase 4 DoD)
            EagerSurfaceRealization = true,
            DeferBootstrap = true,
            PaneFocusRequested = QueuePaneFocusRequested,
        });
        _coordinator = new LiveSurfaceCoordinator(
            Host,
            _scopeFactory,
            options.OpenSurface,
            options.RealizeBudget,
            (request, realizedSurface) =>
            {
                if (realizedSurface is not XtermPaneSurface paneSurface)
                {
                    return;
                }

                if (request.Kind == LiveSurfaceKind.Main && request.SessionId is { } sessionId)
                {
                    Bind(sessionId, paneSurface);
                }
                else if (request.Kind == LiveSurfaceKind.Split && request.SessionId is { } splitSessionId)
                {
                    BindSplit(splitSessionId, paneSurface);
                }
                else if (request.Kind == LiveSurfaceKind.Scratch && request.SessionId is { } scratchSessionId)
                {
                    BindScratch(scratchSessionId, paneSurface);
                }

                SurfaceRealized?.Invoke(request, paneSurface);
            },
            options.Log);
        _scopeFactory.SetBeforeComplete(() => Host.RunLocked(ReconcileSurfacesLocked));
        _storeCoordinator = new LiveStoreCoordinator(Host, options.IsUiThread, ReconcileSurfacesLocked);
        var bootstrapCancellationToken = options.BootstrapCancellationToken;
        var coordinator = _coordinator;
        using var bootstrapScope = _scopeFactory.Begin();
        using var bootstrapCancellationRegistration = bootstrapCancellationToken.CanBeCanceled
            ? bootstrapCancellationToken.Register(() => coordinator.Dispose())
            : default;
        if (!bootstrapCancellationToken.IsCancellationRequested)
        {
            Host.Bootstrap();
        }
        bootstrapScope.CompleteAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public HeadlessHost Host
    {
        get => _host ?? throw new InvalidOperationException(
            "LiveControlHost invariant violated: Host is not initialized.");
        private set => _host = value;
    }

    public string PipeName => Host.PipeName;

    public ILiveStoreCoordinator StoreCoordinator => _storeCoordinator;

    public void Start() => Host.Start();

    public Task PerformFontActionAsync(string action, CancellationToken cancellationToken = default) =>
        StoreCoordinator.MutateAsync(
            _ => Host.Actions.Font(null, null, null, action),
            cancellationToken);

    public Task ApplyBaseFontSizeAsync(double fontSize, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));

        return StoreCoordinator.MutateAsync(store =>
        {
            _currentBaseFontSize = fontSize;
            store.ResetSessionFontSizes();

            foreach (var session in store.Workspaces.SelectMany(workspace => workspace.Sessions))
            {
                if (session.Surface is XtermPaneSurface paneSurface)
                {
                    paneSurface.ApplyBaseFontSize(fontSize);
                }
            }
        }, cancellationToken);
    }

    public async Task<bool> ApplySplitRatioAsync(
        string leftPaneId,
        string rightPaneId,
        double ratio,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(leftPaneId) || string.IsNullOrEmpty(rightPaneId))
        {
            return false;
        }

        var applied = false;
        await StoreCoordinator.MutateAsync(store =>
        {
            var session = store.Workspaces
                .SelectMany(workspace => workspace.Sessions)
                .FirstOrDefault(candidate =>
                    candidate.Surface?.PaneToken == leftPaneId &&
                    candidate.SplitSurface?.PaneToken == rightPaneId);
            if (session is not null)
            {
                applied = store.ApplySplitRatio(ratio, session.Id) is not null;
            }
        }, cancellationToken).ConfigureAwait(false);
        return applied;
    }

    public async Task<bool> ApplyPaneFocusAsync(
        string paneId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(paneId))
        {
            return false;
        }

        var changed = false;
        await StoreCoordinator.MutateAsync(store =>
        {
            var matches = 0;
            Session? targetSession = null;
            var targetSplitFocused = false;

            foreach (var session in store.Workspaces.SelectMany(workspace => workspace.Sessions))
            {
                if (session.Surface?.PaneToken == paneId)
                {
                    matches++;
                    targetSession = session;
                    targetSplitFocused = false;
                }

                if (session.SplitSurface?.PaneToken == paneId)
                {
                    matches++;
                    targetSession = session;
                    targetSplitFocused = true;
                }
            }

            if (matches != 1 || targetSession is null)
            {
                return;
            }

            if (targetSession.SplitFocused == targetSplitFocused)
            {
                return;
            }

            targetSession.SplitFocused = targetSplitFocused;
            store.NotifySidebar();
            changed = true;
        }, cancellationToken).ConfigureAwait(false);

        return changed;
    }

    /// <summary>A surface realized after this host's own bootstrap (i.e. any control- or UI-driven
    /// show). The app uses it to mount the pane's view; fired on the realizing (dispatch) thread.</summary>
    public event Action<SurfaceOpenRequest, XtermPaneSurface>? SurfaceRealized;

    /// <summary>An explicit control request to move keyboard focus to an exact live pane token.</summary>
    public event Action<string>? PaneFocusRequested;

    private void QueuePaneFocusRequested(string paneId)
    {
        lock (_focusRequestLock)
        {
            if (Volatile.Read(ref _shutdownStarted) != 0)
            {
                return;
            }

            Task? delivery = null;
            delivery = Task.Run(() =>
            {
                try
                {
                    if (Volatile.Read(ref _shutdownStarted) == 0)
                    {
                        PaneFocusRequested?.Invoke(paneId);
                    }
                }
                catch (Exception ex)
                {
                    _options.Log?.Invoke($"Pane focus request subscriber failed: {ex}");
                }
                finally
                {
                    lock (_focusRequestLock)
                    {
                        _focusRequestTasks.Remove(delivery!);
                    }
                }
            });
            _focusRequestTasks.Add(delivery);
        }
    }

    /// <summary>Reserve a pending surface under the host gate. The coordinator opens and commits
    /// the real surface asynchronously after bootstrap dispatch completes.</summary>
    private ITerminalSurface? ReserveSurface(Session session)
    {
        if (_host is null)
            throw new InvalidOperationException(
                "LiveControlHost invariant violated: Host is not initialized.");

        var request = CaptureRequest(session, LiveSurfaceKind.Main);
        if (request is null)
        {
            return null;
        }

        var coordinator = _coordinator ?? throw new InvalidOperationException(
            "LiveControlHost invariant violated: coordinator is not initialized.");
        return coordinator.Reserve(request);
    }

    private void ReconcileSurfacesLocked()
    {
        var coordinator = _coordinator;
        if (coordinator is null)
        {
            return;
        }

        foreach (var session in Host.Store.Workspaces.SelectMany(workspace => workspace.Sessions))
        {
            ReserveIfMissing(coordinator, session, LiveSurfaceKind.Main, session.Surface);
            if (session.HasSplit)
            {
                ReserveIfMissing(coordinator, session, LiveSurfaceKind.Split, session.SplitSurface);
            }
            else if (session.SplitSurface is { } splitSurface)
            {
                session.SplitSurface = null;
                splitSurface.Teardown();
            }

            if (session.ScratchActive)
            {
                ReserveIfMissing(coordinator, session, LiveSurfaceKind.Scratch, session.ScratchSurface);
            }

            if (session.OverlayActive)
            {
                ReserveIfMissing(coordinator, session, LiveSurfaceKind.Overlay, session.OverlaySurface);
            }
            else if (session.OverlaySurface is { } overlaySurface)
            {
                session.OverlaySurface = null;
                overlaySurface.Teardown();
            }
        }

        if (Host.Actions.Quick.Visible &&
            Host.Store.ActiveSession is { } activeSession &&
            Host.Actions.Quick.Surface is null)
        {
            var quickRequest = CaptureRequest(activeSession, LiveSurfaceKind.Quick);
            if (quickRequest is not null)
            {
                coordinator.Reserve(quickRequest);
            }
        }

    }

    private void ReserveIfMissing(
        LiveSurfaceCoordinator coordinator,
        Session session,
        LiveSurfaceKind kind,
        ITerminalSurface? slot)
    {
        if (slot is not null)
        {
            return;
        }

        var request = CaptureRequest(session, kind);
        if (request is not null)
        {
            coordinator.Reserve(request);
        }
    }

    private SurfaceOpenRequest? CaptureRequest(Session session, LiveSurfaceKind kind)
    {
        IReadOnlyList<string> argv;
        string? cwd;
        string? initialInput = null;
        Guid? sessionId;
        var baseFontSize = session.FontSize ?? _currentBaseFontSize;
        var shellArgv = new[] { ShellResolver.Resolve(_options.DefaultShell) };
        var restoreRunningCommand = _options.RestoreRunningCommand;

        switch (kind)
        {
            case LiveSurfaceKind.Main:
            {
                var pendingOverride = session.TakePendingRestoreOverride(StatusPane.Left);
                var capturedForeground = session.ForegroundCommand;
                session.ForegroundCommand = null;

                if (pendingOverride is not null)
                {
                    argv = shellArgv;
                    initialInput = restoreRunningCommand && pendingOverride.Length > 0
                        ? pendingOverride + "\n"
                        : null;
                }
                else if (capturedForeground is { Length: > 0 })
                {
                    argv = capturedForeground;
                    initialInput = restoreRunningCommand
                        ? WindowsCommandLine.FromArgv(capturedForeground) + "\n"
                        : null;
                }
                else if (session.InitialCommand is { } initialCommand &&
                         (!session.WasRestored || restoreRunningCommand))
                {
                    argv = CommandTokenizer.Tokenize(initialCommand);
                }
                else
                {
                    argv = shellArgv;
                }

                cwd = session.EffectiveCwd;
                sessionId = session.Id;
                break;
            }

            case LiveSurfaceKind.Split:
            {
                var pendingOverride = session.TakePendingRestoreOverride(StatusPane.Right);
                var capturedForeground = session.SplitForegroundCommand;
                session.SplitForegroundCommand = null;

                argv = shellArgv;
                if (pendingOverride is not null)
                {
                    initialInput = restoreRunningCommand && pendingOverride.Length > 0
                        ? pendingOverride + "\n"
                        : null;
                }
                else if (capturedForeground is { Length: > 0 })
                {
                    initialInput = restoreRunningCommand
                        ? WindowsCommandLine.FromArgv(capturedForeground) + "\n"
                        : null;
                }

                cwd = session.InitialSplitCwd ?? session.EffectiveCwd;
                sessionId = session.Id;
                break;
            }

            case LiveSurfaceKind.Scratch:
                var scratchCommand = session.ScratchCommand;
                session.ScratchCommand = null;
                argv = scratchCommand is { } command
                    ? CommandTokenizer.Tokenize(command)
                    : shellArgv;
                cwd = session.EffectiveCwd;
                sessionId = session.Id;
                break;

            case LiveSurfaceKind.Overlay:
                if (session.OverlayCommand is not { } overlayCommand)
                {
                    return null;
                }

                argv = CommandTokenizer.Tokenize(overlayCommand);
                cwd = session.OverlayCwd ?? session.EffectiveCwd;
                sessionId = session.Id;
                break;

            case LiveSurfaceKind.Quick:
                argv = shellArgv;
                cwd = session.EffectiveCwd;
                sessionId = null;
                baseFontSize = _currentBaseFontSize;
                break;

            default:
                throw new NotSupportedException($"Unsupported surface kind: {kind}");
        }

        if (argv.Count == 0)
        {
            return null;
        }

        return new SurfaceOpenRequest(
            sessionId,
            kind,
            new XtermSurfaceOptions
            {
                Argv = argv.ToArray(),
                Cwd = ExistingDirectoryOrNull(cwd),
                InitialInput = initialInput,
                BaseFontSize = baseFontSize,
                ReadClipboard = _options.ReadClipboard,
            });
    }

    /// <summary>Live reports → session fields. Every mutation hops to the thread pool and takes
    /// the host gate; the UI thread never enters RunLocked.</summary>
    private void Bind(Guid sessionId, XtermPaneSurface surface)
    {
        surface.Pane.TitleChanged += title => Task.Run(() => Host.RunLocked(() =>
        {
            var live = Host.Store.SessionWithId(sessionId);
            if (OwnsMainPane(live, surface))
            {
                live!.OscTitle = title;
                Host.Store.NotifySidebar();
            }
        }));
        surface.Pane.CwdChanged += cwd => Task.Run(() => Host.RunLocked(() =>
        {
            var live = Host.Store.SessionWithId(sessionId);
            if (OwnsMainPane(live, surface))
            {
                live!.CurrentCwd = cwd;
                Host.Store.NotifySidebar();
            }
        }));
        _ = CloseOnExitAsync(sessionId, surface);
    }

    private void BindSplit(Guid sessionId, XtermPaneSurface surface)
    {
        surface.Pane.TitleChanged += title => Task.Run(() => Host.RunLocked(() =>
        {
            var live = Host.Store.SessionWithId(sessionId);
            if (OwnsSplitPane(live, surface))
            {
                live!.SplitTitle = title;
                Host.Store.NotifySidebar();
                return;
            }

            if (surface.IsPromotedToPrimaryPane && OwnsMainPane(live, surface))
            {
                live!.OscTitle = title;
                Host.Store.NotifySidebar();
            }
        }));
        surface.Pane.CwdChanged += cwd => Task.Run(() => Host.RunLocked(() =>
        {
            var live = Host.Store.SessionWithId(sessionId);
            if (OwnsSplitPane(live, surface))
            {
                live!.SplitCwd = cwd;
                Host.Store.NotifySidebar();
                return;
            }

            if (surface.IsPromotedToPrimaryPane && OwnsMainPane(live, surface))
            {
                live!.CurrentCwd = cwd;
                Host.Store.NotifySidebar();
            }
        }));
        _ = CloseSplitOnExitAsync(sessionId, surface);
    }

    private void BindScratch(Guid sessionId, XtermPaneSurface surface) =>
        _ = CloseScratchOnExitAsync(sessionId, surface);

    /// <summary>The realized surface still is the session's current main pane.</summary>
    private static bool OwnsMainPane(Session? live, XtermPaneSurface surface) =>
        live is not null &&
        ReferenceEquals(live.Surface, surface);

    /// <summary>The realized surface still is the session's current split pane.</summary>
    private static bool OwnsSplitPane(Session? live, XtermPaneSurface surface) =>
        live is not null &&
        ReferenceEquals(live.SplitSurface, surface);

    private static bool OwnsScratchPane(Session? live, XtermPaneSurface surface) =>
        live is not null &&
        ReferenceEquals(live.ScratchSurface, surface);

    private async Task CloseOnExitAsync(Guid sessionId, XtermPaneSurface surface)
    {
        try
        {
            await surface.Pane.Exited.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return; // a torn-down pane may fault the exit task; nothing to close then
        }

        if (Volatile.Read(ref _shutdownStarted) != 0)
        {
            return;
        }

        Host.RunLocked(() =>
        {
            if (Volatile.Read(ref _shutdownStarted) != 0)
            {
                return;
            }

            var live = Host.Store.SessionWithId(sessionId);
            if (!OwnsMainPane(live, surface)) return; // closed via control, or pane replaced
            // session.new --command --wait: hold the pane open after the command exits
            if (live!.InitialCommand is not null && live.CommandWait) return;
            Host.Store.ClosePrimaryPane(live.Id);
            ReconcileSurfacesLocked();
        });
    }

    private async Task CloseSplitOnExitAsync(Guid sessionId, XtermPaneSurface surface)
    {
        try
        {
            await surface.Pane.Exited.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return; // a torn-down pane may fault the exit task; nothing to close then
        }

        if (Volatile.Read(ref _shutdownStarted) != 0)
        {
            return;
        }

        Host.RunLocked(() =>
        {
            if (Volatile.Read(ref _shutdownStarted) != 0)
            {
                return;
            }

            var live = Host.Store.SessionWithId(sessionId);
            if (OwnsSplitPane(live, surface))
            {
                Host.Store.CloseSplitPane(live!.Id);
                ReconcileSurfacesLocked();
                return;
            }

            if (surface.IsPromotedToPrimaryPane && OwnsMainPane(live, surface))
            {
                Host.Store.ClosePrimaryPane(live!.Id);
                ReconcileSurfacesLocked();
            }
        });
    }

    private async Task CloseScratchOnExitAsync(Guid sessionId, XtermPaneSurface surface)
    {
        try
        {
            await surface.Pane.Exited.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        if (Volatile.Read(ref _shutdownStarted) != 0)
        {
            return;
        }

        Host.RunLocked(() =>
        {
            if (Volatile.Read(ref _shutdownStarted) != 0)
            {
                return;
            }

            var live = Host.Store.SessionWithId(sessionId);
            if (!OwnsScratchPane(live, surface)) return;
            Host.Store.CloseScratch(live!.Id);
            ReconcileSurfacesLocked();
        });
    }

    /// <summary>A session restored with a stale cwd must still spawn (upstream falls back to home).</summary>
    private string? ExistingDirectoryOrNull(string? cwd) =>
        cwd is not null && Directory.Exists(cwd) ? cwd : null;

    /// <summary>Stop serving, flush the snapshot, and tear down every live pane (killing the
    /// children) — the app-exit path (§3: save on exit).</summary>
    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        Task shutdownTask = GetOrStartShutdownTask();

        return cancellationToken.CanBeCanceled
            ? shutdownTask.WaitAsync(cancellationToken)
            : shutdownTask;
    }

    public void Dispose()
    {
        Task shutdownTask = GetOrStartShutdownTask();

        Task completedTask;
        try
        {
            completedTask = Task.WhenAny(shutdownTask, Task.Delay(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _options.Log?.Invoke($"LiveControlHost.Dispose failed to wait for shutdown: {ex}");
            return;
        }

        if (!ReferenceEquals(completedTask, shutdownTask))
        {
            _options.Log?.Invoke("LiveControlHost.Dispose timed out waiting for shutdown after 5 seconds.");
            return;
        }

        if (!shutdownTask.IsCompletedSuccessfully)
        {
            var exception = shutdownTask.Exception?.GetBaseException();
            _options.Log?.Invoke($"LiveControlHost.Dispose observed shutdown failure: {exception}");
        }
    }

    private Task GetOrStartShutdownTask()
    {
        lock (_shutdownLock)
        {
            _shutdownTask ??= Task.Run(ShutdownCoreAsync, CancellationToken.None);
            return _shutdownTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ShutdownCoreAsync()
    {
        Interlocked.Exchange(ref _shutdownStarted, 1);

        Host.Stop();

        Task[] focusRequests;
        lock (_focusRequestLock)
        {
            focusRequests = _focusRequestTasks.ToArray();
        }
        await Task.WhenAll(focusRequests).ConfigureAwait(false);

        if (_coordinator is not null)
        {
            await _coordinator.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        }

        _storeCoordinator.Dispose();

        ITerminalSurface[] surfacesToShutdown = Array.Empty<ITerminalSurface>();
        Host.RunLocked(() =>
        {
            var distinctSurfaces = new HashSet<ITerminalSurface>(ReferenceEqualityComparer.Instance);
            var orderedSurfaces = new List<ITerminalSurface>();

            foreach (var session in Host.Store.Workspaces.SelectMany(w => w.Sessions))
            {
                AddSurface(session.Surface);
                AddSurface(session.SplitSurface);
                AddSurface(session.ScratchSurface);
                AddSurface(session.OverlaySurface);
            }

            AddSurface(Host.Actions.Quick.Surface);

            surfacesToShutdown = orderedSurfaces.ToArray();

            void AddSurface(ITerminalSurface? surface)
            {
                if (surface is null)
                {
                    return;
                }

                if (distinctSurfaces.Add(surface))
                {
                    orderedSurfaces.Add(surface);
                }
            }
        });

        foreach (var surface in surfacesToShutdown)
        {
            if (surface is not ILiveShutdownSurface liveShutdownSurface)
            {
                continue;
            }

            try
            {
                liveShutdownSurface.StopOutput();
            }
            catch (Exception ex)
            {
                _options.Log?.Invoke($"LiveControlHost shutdown stop-output failure: {ex}");
            }
        }

        Host.RunLocked(() =>
        {
            Host.Store.CancelPendingSave();
            Host.Store.Save();
        });

        foreach (var surface in surfacesToShutdown)
        {
            try
            {
                if (surface is ILiveShutdownSurface liveShutdownSurface)
                {
                    await liveShutdownSurface.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    surface.Teardown();
                }
            }
            catch (Exception ex)
            {
                _options.Log?.Invoke($"LiveControlHost shutdown teardown failure: {ex}");
            }
        }
    }
}
