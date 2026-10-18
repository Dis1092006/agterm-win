using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Terminal;
using System.Threading;

namespace AgTerm.Xterm;

internal interface ILiveShutdownSurface
{
    void StopOutput();
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

/// <summary>A surface whose output can be saved at shutdown and shown again after a relaunch.</summary>
internal interface IScrollbackSurface
{
    /// <summary>The normal-screen buffer as VT text; null when the renderer is gone.</summary>
    Task<string?> CaptureScrollbackAsync();
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

    public HeadlessThemeSnapshot? InitialTheme { get; init; }

    public string? HomeDirectory { get; init; }

    /// <summary>The configured default shell; null = the ShellResolver policy (pwsh → cmd).</summary>
    public string? DefaultShell { get; init; }

    /// <summary>
    /// Directory holding the per-shell cwd-reporting integration scripts (ShellIntegration).
    /// Null or missing = shells spawn bare: the terminal works but never learns the cwd, so a
    /// restored session reopens at its InitialCwd. See PLATFORM-NOTES.
    /// </summary>
    public string? ShellIntegrationDirectory { get; init; }

    /// <summary>System clipboard reader handed to every surface (session.paste).</summary>
    public Func<string?>? ReadClipboard { get; init; }

    /// <summary>Default base font size for newly realized surfaces.</summary>
    public double BaseFontSize { get; init; } = 14;

    public AppSettings NotificationSettings { get; init; } = new();

    /// <summary>When true, restored foreground/override commands are sent as startup input.</summary>
    public bool RestoreRunningCommand { get; init; }

    /// <summary>config.reload count callback forwarded to the headless control actions.</summary>
    public Func<int>? ConfigReload { get; init; }

    public Action<string>? Log { get; init; }

    public Action<LiveFailureNotice>? FailureReported { get; init; }

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

    internal Func<Action, Task> NotificationScheduler { get; init; } =
        static notification => Task.Run(notification);

    internal Func<TimeSpan>? NotificationClock { get; init; }
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
    // All panes are serialized in parallel; this keeps a stuck page from holding up the exit
    // (Dispose waits five seconds for the whole shutdown).
    private static readonly TimeSpan ScrollbackCaptureBudget = TimeSpan.FromSeconds(3);
    private readonly LiveDispatchScopeFactory _scopeFactory;
    private readonly LiveStoreCoordinator _storeCoordinator;
    private readonly LiveSurfaceCoordinator? _coordinator;
    private double _currentBaseFontSize;
    private readonly Lock _shutdownLock = new();
    private readonly Lock _focusRequestLock = new();
    private readonly HashSet<Task> _focusRequestTasks = new();
    private ILiveControlRuntime? _host;
    private Task? _shutdownTask;
    private int _shutdownStarted;
    private int _zoomActive;
    private bool _dashboardTransientActive;
    private readonly Queue<bool> _pendingZoomStates = new();
    private Task? _zoomNotificationTask;
    private readonly Queue<HeadlessThemeSnapshot> _pendingThemeStates = new();
    private Task? _themeNotificationTask;
    private AppSettings _notificationSettings;
    private readonly Func<TimeSpan> _notificationClock;
    private readonly SoundThrottle _soundThrottle = new(TimeSpan.FromMilliseconds(200));
    private readonly Queue<LiveNotificationDelivery> _pendingNotificationDeliveries = new();
    private Task? _notificationDeliveryTask;

    private abstract record LiveNotificationDelivery
    {
        public sealed record Banner(LiveBannerNotification Notification) : LiveNotificationDelivery;
        public sealed record Status(string SoundName) : LiveNotificationDelivery;
    }

    public LiveControlHost(LiveControlHostOptions options)
        : this(options, windowHost: null, applicationPipeName: null, useWindowHost: false)
    {
    }

    internal LiveControlHost(
        LiveControlHostOptions options,
        LiveWindowHost windowHost,
        string applicationPipeName)
        : this(options, windowHost, applicationPipeName, useWindowHost: true)
    {
    }

    private LiveControlHost(
        LiveControlHostOptions options,
        LiveWindowHost? windowHost,
        string? applicationPipeName,
        bool useWindowHost)
    {
        _ = useWindowHost;
        _options = options;
        _currentBaseFontSize = options.BaseFontSize;
        _notificationSettings = options.NotificationSettings;
        var notificationStopwatch = System.Diagnostics.Stopwatch.StartNew();
        _notificationClock = options.NotificationClock ?? (() => notificationStopwatch.Elapsed);
        _scopeFactory = new LiveDispatchScopeFactory();
        if (windowHost is null)
        {
            Host = new HeadlessControlRuntime(new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = options.StateDir,
                ConfigDir = options.ConfigDir,
                PipeName = options.PipeName,
                WindowName = options.WindowName,
                AvailableThemes = options.AvailableThemes,
                ConfigReload = options.ConfigReload,
                HomeDirectory = options.HomeDirectory,
                RestoreRunningCommand = options.RestoreRunningCommand,
                DashboardBaseFontSize = options.BaseFontSize,
                Log = options.Log,
                SurfaceRealizer = ReserveSurface,
                DispatchScopeFactory = _scopeFactory,
                EagerSurfaceRealization = true,
                DeferBootstrap = true,
                PaneFocusRequested = QueuePaneFocusRequested,
                SearchUpdated = QueueSearchUpdated,
                QuickChanged = QueueQuickChanged,
                ZoomActiveChanged = QueueZoomActiveChanged,
                ZoomChanged = QueueZoomChanged,
                DashboardChanged = QueueDashboardChanged,
                ThemeChanged = QueueThemeChanged,
                NotificationRequested = QueueNotificationRequested,
                StatusNotificationRequested = QueueStatusNotificationRequested,
                ZoomTargetAvailable = IsZoomTargetRealized,
            }));
        }
        else
        {
            var runtime = new WindowControlRuntime(
                windowHost,
                applicationPipeName ?? throw new ArgumentNullException(nameof(applicationPipeName)));
            Host = runtime;
            var configDirectory = options.ConfigDir
                ?? options.StateDir
                ?? PersistenceStore.DefaultDirectory;
            var actions = new HeadlessControlActions(
                runtime.Store,
                runtime.Ring,
                runtime.WindowId,
                runtime.WindowName,
                configDirectory,
                options.AvailableThemes,
                options.ConfigReload,
                options.RestoreRunningCommand,
                dashboardBaseFontSize: options.BaseFontSize,
                homeDirectory: options.HomeDirectory,
                paneFocusRequested: QueuePaneFocusRequested,
                searchUpdated: QueueSearchUpdated,
                quickChanged: QueueQuickChanged,
                zoomActiveChanged: QueueZoomActiveChanged,
                zoomChanged: QueueZoomChanged,
                dashboardChanged: QueueDashboardChanged,
                themeChanged: QueueThemeChanged,
                notificationRequested: QueueNotificationRequested,
                statusNotificationRequested: QueueStatusNotificationRequested,
                zoomTargetAvailable: IsZoomTargetRealized,
                pick: windowHost.Pick,
                picks: windowHost.Picks);
            windowHost.AttachLive(actions, ReserveSurface, _scopeFactory);
        }
        if (options.InitialTheme is { } initialTheme)
        {
            if (initialTheme.Sync != (initialTheme.Dark is not null))
                throw new ArgumentException("Initial theme sync state must match the dark slot.", nameof(options));
            Host.Actions.Theme.Light = initialTheme.Light;
            Host.Actions.Theme.Dark = initialTheme.Dark;
        }
        _storeCoordinator = new LiveStoreCoordinator(Host, options.IsUiThread, ReconcileSurfacesLocked);
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
                else if (request.Kind == LiveSurfaceKind.Overlay && request.SessionId is { } overlaySessionId)
                {
                    BindOverlay(overlaySessionId, paneSurface);
                }
                else if (request.Kind == LiveSurfaceKind.Quick)
                {
                    BindQuick(paneSurface);
                }

                SurfaceRealized?.Invoke(request, paneSurface);
                if (request.Kind == LiveSurfaceKind.Quick)
                {
                    Host.RunLocked(_storeCoordinator.PublishSelectionSnapshotLocked);
                }
            },
            options.Log,
            failureReported: options.FailureReported);
        _scopeFactory.SetBeforeComplete(() => Host.RunLocked(ReconcileSurfacesLocked));
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

    public ILiveControlRuntime Host
    {
        get => _host ?? throw new InvalidOperationException(
            "LiveControlHost invariant violated: Host is not initialized.");
        private set => _host = value;
    }

    public string PipeName => Host.PipeName;

    public Guid WindowId => Host.WindowId;

    public AppStore Store => Host.Store;

    public ControlEventRing Ring => Host.Ring;

    public HeadlessControlActions Actions => Host.Actions;

    public bool ZoomActive => Volatile.Read(ref _zoomActive) != 0;

    public ILiveStoreCoordinator StoreCoordinator => _storeCoordinator;

    public void Start() => Host.Start();

    public Task PerformFontActionAsync(string action, CancellationToken cancellationToken = default) =>
        StoreCoordinator.MutateAsync(
            _ => Host.Actions.Font(null, null, null, action),
            cancellationToken);

    public Task ToggleQuickTerminalAsync(CancellationToken cancellationToken = default) =>
        StoreCoordinator.MutateAsync(
            _ =>
            {
                if (Host.Actions.Zoom.Target is null)
                {
                    Host.Actions.SetQuickTerminal("toggle");
                }
            },
            cancellationToken);

    public Task ToggleTerminalZoomAsync(CancellationToken cancellationToken = default) =>
        StoreCoordinator.MutateAsync(
            _ => Host.Actions.SetSurfaceZoom(null, null, ControlToggleMode.Toggle),
            cancellationToken);

    public Task ToggleDashboardAsync(CancellationToken cancellationToken = default) =>
        StoreCoordinator.MutateAsync(
            _ =>
            {
                if (Host.Actions.Zoom.Target is not null)
                    return;
                Host.Actions.SetDashboard(
                    Array.Empty<string>(),
                    window: null,
                    close: Host.Actions.Dashboard.IsOpen,
                    new DashboardFontMode.Auto(),
                    mru: !Host.Actions.Dashboard.IsOpen);
            },
            cancellationToken);

    public Task CloseDashboardAsync(CancellationToken cancellationToken = default) =>
        StoreCoordinator.MutateAsync(
            _ => Host.Actions.SetDashboard(
                Array.Empty<string>(),
                window: null,
                close: true,
                new DashboardFontMode.Untouched(),
                mru: false),
            cancellationToken);

    public async Task<bool> HighlightDashboardMemberAsync(
        string controlRef,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(controlRef))
            return false;

        var highlighted = false;
        await StoreCoordinator.MutateAsync(_ =>
        {
            var member = Host.Actions.Dashboard.Members
                .FirstOrDefault(candidate => candidate.ControlRef == controlRef);
            if (member.ControlRef != controlRef || !Host.Actions.Dashboard.Highlight(member))
                return;
            highlighted = true;
            _storeCoordinator.PublishSelectionSnapshotLocked();
        }, cancellationToken).ConfigureAwait(false);
        return highlighted;
    }

    public async Task<bool> ActivateDashboardMemberAsync(
        string controlRef,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(controlRef))
            return false;

        var activated = false;
        await StoreCoordinator.MutateAsync(store =>
        {
            var member = Host.Actions.Dashboard.Members
                .FirstOrDefault(candidate => candidate.ControlRef == controlRef);
            if (member.ControlRef != controlRef || store.SessionWithId(member.Session) is not { } session)
                return;
            var paneId = member.Surface switch
            {
                TerminalZoomSurface.Primary => session.Surface?.PaneToken,
                TerminalZoomSurface.Split => session.SplitSurface?.PaneToken,
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(paneId))
                return;

            store.SelectSession(member.Session);
            session.SplitFocused = member.Surface == TerminalZoomSurface.Split;
            store.NotifySidebar();
            Host.Actions.Dashboard.Close();
            SyncDashboardTransientStateLocked();
            _storeCoordinator.PublishSelectionSnapshotLocked();
            activated = true;
        }, cancellationToken).ConfigureAwait(false);
        return activated;
    }

    public Task ApplyBaseFontSizeAsync(double fontSize, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));

        return StoreCoordinator.MutateAsync(store =>
        {
            _currentBaseFontSize = fontSize;
            Host.Actions.SetDashboardBaseFontSize(fontSize);
            store.ResetSessionFontSizes();

            foreach (var session in store.Workspaces.SelectMany(workspace => workspace.Sessions))
            {
                if (session.Surface is XtermPaneSurface paneSurface)
                {
                    paneSurface.ApplyBaseFontSize(fontSize);
                }
            }

            if (Host.Actions.Dashboard.IsOpen)
                _storeCoordinator.PublishSelectionSnapshotLocked();
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

    public async Task<bool> ApplyHudMetricsAsync(
        string paneId,
        HudSessionMetrics metrics,
        HudRect? singlePane,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        if (string.IsNullOrEmpty(paneId))
        {
            return false;
        }

        var changed = false;
        await StoreCoordinator.MutateAsync(
            store => changed = HudMetricsTarget.Apply(store, paneId, metrics, singlePane),
            cancellationToken).ConfigureAwait(false);
        return changed;
    }

    /// <summary>A surface realized after this host's own bootstrap (i.e. any control- or UI-driven
    /// show). The app uses it to mount the pane's view; fired on the realizing (dispatch) thread.</summary>
    public event Action<SurfaceOpenRequest, XtermPaneSurface>? SurfaceRealized;

    /// <summary>An explicit control request to move keyboard focus to an exact live pane token.</summary>
    public event Action<string>? PaneFocusRequested;

    public event Action<bool>? ZoomActiveChanged;

    public event Action<HeadlessThemeSnapshot>? ThemeChanged;

    public event Action<LiveBannerNotification>? NotificationRequested;

    public event Action<string>? StatusNotificationRequested;

    public void SetNotificationSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Volatile.Write(ref _notificationSettings, settings);
    }

    private void QueueZoomActiveChanged(bool active)
    {
        Volatile.Write(ref _zoomActive, active ? 1 : 0);
        lock (_focusRequestLock)
        {
            _pendingZoomStates.Enqueue(active);
            if (Volatile.Read(ref _shutdownStarted) != 0)
                return;
            if (_zoomNotificationTask is not null)
                return;

            var delivery = ScheduleZoomNotificationWorker();
            _zoomNotificationTask = delivery;
            _focusRequestTasks.Add(delivery);
        }
    }

    private Task ScheduleZoomNotificationWorker()
    {
        var schedulerReturned = 0;
        var invokedBeforeReturn = 0;
        Task? delivery = null;
        try
        {
            delivery = _options.NotificationScheduler(() =>
            {
                if (Volatile.Read(ref schedulerReturned) == 0)
                {
                    Interlocked.Exchange(ref invokedBeforeReturn, 1);
                    return;
                }

                DeliverZoomActiveChanges(delivery!);
            });
        }
        catch (Exception ex)
        {
            _options.Log?.Invoke($"Zoom notification scheduler failed: {ex}");
        }
        finally
        {
            Volatile.Write(ref schedulerReturned, 1);
        }

        if (delivery is not null &&
            !delivery.IsCompleted &&
            Volatile.Read(ref invokedBeforeReturn) == 0)
        {
            return delivery;
        }

        Task? deferred = null;
        deferred = Task.Run(() => DeliverZoomActiveChanges(deferred!));
        return deferred;
    }

    private void DeliverZoomActiveChanges(Task delivery)
    {
        while (true)
        {
            bool active;
            lock (_focusRequestLock)
            {
                if (Volatile.Read(ref _shutdownStarted) != 0)
                {
                    _pendingZoomStates.Clear();
                    _zoomNotificationTask = null;
                    _focusRequestTasks.Remove(delivery);
                    return;
                }

                if (_pendingZoomStates.Count == 0)
                {
                    _zoomNotificationTask = null;
                    _focusRequestTasks.Remove(delivery);
                    return;
                }

                active = _pendingZoomStates.Dequeue();
            }

            try
            {
                ZoomActiveChanged?.Invoke(active);
            }
            catch (Exception ex)
            {
                _options.Log?.Invoke($"Zoom active-state subscriber failed: {ex}");
            }
        }
    }

    private void QueueThemeChanged(HeadlessThemeSnapshot state)
    {
        lock (_focusRequestLock)
        {
            _pendingThemeStates.Enqueue(state);
            if (Volatile.Read(ref _shutdownStarted) != 0)
                return;
            if (_themeNotificationTask is not null)
                return;

            var delivery = ScheduleThemeNotificationWorker();
            _themeNotificationTask = delivery;
            _focusRequestTasks.Add(delivery);
        }
    }

    private Task ScheduleThemeNotificationWorker()
    {
        var schedulerReturned = 0;
        var invokedBeforeReturn = 0;
        Task? delivery = null;
        try
        {
            delivery = _options.NotificationScheduler(() =>
            {
                if (Volatile.Read(ref schedulerReturned) == 0)
                {
                    Interlocked.Exchange(ref invokedBeforeReturn, 1);
                    return;
                }

                DeliverThemeChanges(delivery!);
            });
        }
        catch (Exception ex)
        {
            _options.Log?.Invoke($"Theme notification scheduler failed: {ex}");
        }
        finally
        {
            Volatile.Write(ref schedulerReturned, 1);
        }

        if (delivery is not null &&
            !delivery.IsCompleted &&
            Volatile.Read(ref invokedBeforeReturn) == 0)
        {
            return delivery;
        }

        Task? deferred = null;
        deferred = Task.Run(() => DeliverThemeChanges(deferred!));
        return deferred;
    }

    private void DeliverThemeChanges(Task delivery)
    {
        while (true)
        {
            HeadlessThemeSnapshot state;
            lock (_focusRequestLock)
            {
                if (Volatile.Read(ref _shutdownStarted) != 0)
                {
                    _pendingThemeStates.Clear();
                    _themeNotificationTask = null;
                    _focusRequestTasks.Remove(delivery);
                    return;
                }

                if (_pendingThemeStates.Count == 0)
                {
                    _themeNotificationTask = null;
                    _focusRequestTasks.Remove(delivery);
                    return;
                }

                state = _pendingThemeStates.Dequeue();
            }

            try
            {
                ThemeChanged?.Invoke(state);
            }
            catch (Exception ex)
            {
                _options.Log?.Invoke($"Theme subscriber failed: {ex}");
            }
        }
    }

    private bool QueueNotificationRequested(LiveNotificationRequest request)
    {
        if (Volatile.Read(ref _shutdownStarted) != 0)
        {
            return false;
        }

        var notification = LiveNotificationPolicy.ForNotify(
            request,
            Volatile.Read(ref _notificationSettings));
        if (notification is null)
            return false;

        QueueNotificationDelivery(new LiveNotificationDelivery.Banner(notification));
        return true;
    }

    private void QueueStatusNotificationRequested(LiveStatusNotificationRequest request)
    {
        var soundName = LiveNotificationPolicy.StatusSound(
            request,
            Volatile.Read(ref _notificationSettings));
        if (soundName is not null && _soundThrottle.Allow(soundName, _notificationClock()))
            QueueNotificationDelivery(new LiveNotificationDelivery.Status(soundName));
    }

    private void QueueNotificationDelivery(LiveNotificationDelivery notification)
    {
        lock (_focusRequestLock)
        {
            if (Volatile.Read(ref _shutdownStarted) != 0)
                return;

            _pendingNotificationDeliveries.Enqueue(notification);
            if (_notificationDeliveryTask is not null)
                return;

            var delivery = ScheduleNotificationDeliveryWorker();
            _notificationDeliveryTask = delivery;
            _focusRequestTasks.Add(delivery);
        }
    }

    private Task ScheduleNotificationDeliveryWorker()
    {
        var schedulerReturned = 0;
        var invokedBeforeReturn = 0;
        Task? delivery = null;
        try
        {
            delivery = _options.NotificationScheduler(() =>
            {
                if (Volatile.Read(ref schedulerReturned) == 0)
                {
                    Interlocked.Exchange(ref invokedBeforeReturn, 1);
                    return;
                }

                DeliverNotifications(delivery!);
            });
        }
        catch (Exception ex)
        {
            _options.Log?.Invoke($"Notification scheduler failed: {ex}");
        }
        finally
        {
            Volatile.Write(ref schedulerReturned, 1);
        }

        if (delivery is not null &&
            !delivery.IsCompleted &&
            Volatile.Read(ref invokedBeforeReturn) == 0)
        {
            return delivery;
        }

        Task? deferred = null;
        deferred = Task.Run(() => DeliverNotifications(deferred!));
        return deferred;
    }

    private void DeliverNotifications(Task delivery)
    {
        while (true)
        {
            LiveNotificationDelivery notification;
            lock (_focusRequestLock)
            {
                if (Volatile.Read(ref _shutdownStarted) != 0)
                {
                    _pendingNotificationDeliveries.Clear();
                    _notificationDeliveryTask = null;
                    _focusRequestTasks.Remove(delivery);
                    return;
                }

                if (_pendingNotificationDeliveries.Count == 0)
                {
                    _notificationDeliveryTask = null;
                    _focusRequestTasks.Remove(delivery);
                    return;
                }

                notification = _pendingNotificationDeliveries.Dequeue();
            }

            try
            {
                switch (notification)
                {
                    case LiveNotificationDelivery.Banner banner:
                        NotificationRequested?.Invoke(banner.Notification);
                        break;
                    case LiveNotificationDelivery.Status status:
                        StatusNotificationRequested?.Invoke(status.SoundName);
                        break;
                }
            }
            catch (Exception ex)
            {
                _options.Log?.Invoke($"Notification subscriber failed: {ex}");
            }
        }
    }

    private void QueueQuickChanged() =>
        _storeCoordinator?.PublishSelectionSnapshotLocked();

    private void QueueZoomChanged()
    {
        if (Host.Actions.Zoom.Target is TerminalZoomTarget.Session
            && Host.Actions.Quick.Visible)
        {
            Host.Actions.SetQuickTerminal("hide");
        }

        if (Host.Actions.Zoom.Target is not null && Host.Actions.Dashboard.IsOpen)
            Host.Actions.Dashboard.Close();

        SyncDashboardTransientStateLocked();
        _storeCoordinator?.PublishSelectionSnapshotLocked();
    }

    private void QueueDashboardChanged()
    {
        ReconcileDashboardLocked();
        SyncDashboardTransientStateLocked();
        _storeCoordinator?.PublishSelectionSnapshotLocked();
    }

    private void SyncDashboardTransientStateLocked()
    {
        var isOpen = Host.Actions.Dashboard.IsOpen;
        if (isOpen == _dashboardTransientActive)
            return;

        _dashboardTransientActive = isOpen;
        if (isOpen)
        {
            Host.Store.SuppressAutoFollow();
            if (Host.Actions.Quick.Visible)
                Host.Actions.SetQuickTerminal("hide");
        }
        else
        {
            Host.Store.ResumeAutoFollow();
        }
    }

    private bool IsZoomTargetRealized(TerminalZoomTarget target)
    {
        var paneToken = target switch
        {
            TerminalZoomTarget.Quick => Host.Actions.Quick.Surface?.PaneToken,
            TerminalZoomTarget.Session sessionTarget => sessionTarget.Surface switch
            {
                TerminalZoomSurface.Primary => Host.Store.SessionWithId(sessionTarget.SessionId)?.Surface?.PaneToken,
                TerminalZoomSurface.Split => Host.Store.SessionWithId(sessionTarget.SessionId)?.SplitSurface?.PaneToken,
                TerminalZoomSurface.Scratch => Host.Store.SessionWithId(sessionTarget.SessionId)?.ScratchSurface?.PaneToken,
                TerminalZoomSurface.Overlay => Host.Store.SessionWithId(sessionTarget.SessionId)?.OverlaySurface?.PaneToken,
                _ => throw new ArgumentOutOfRangeException(nameof(target)),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        return !string.IsNullOrWhiteSpace(paneToken);
    }

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

    private void QueueSearchUpdated(HeadlessSearchUpdate update)
    {
        _scopeFactory.TryRegister(cancellationToken => ApplySearchUpdateAsync(update, cancellationToken));
    }

    private static async Task ApplySearchUpdateAsync(
        HeadlessSearchUpdate update,
        CancellationToken cancellationToken)
    {
        if (update.Surface is not XtermPaneSurface surface ||
            string.IsNullOrWhiteSpace(surface.PaneToken))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (update.Active)
        {
            await surface.Pane.Page.ApplySearchAsync(
                surface.PaneToken,
                update.Needle,
                update.DisplayText,
                update.Selected,
                update.FocusInput).ConfigureAwait(false);
        }
        else
        {
            await surface.Pane.Page.HideSearchAsync(
                surface.PaneToken,
                update.RestorePaneId).ConfigureAwait(false);
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
        Host.Actions.ReconcileAskAnchor();
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

        if (Host.Actions.Quick.Visible && Host.Actions.Quick.Surface is null)
        {
            var pending = coordinator.Reserve(CaptureQuickRequest(Host.Store.ActiveSession));
            _ = PublishQuickSettlementAsync(pending);
        }

        if (ReconcileDashboardLocked())
            _storeCoordinator.PublishSelectionSnapshotLocked();
    }

    private bool ReconcileDashboardLocked()
    {
        if (!Host.Actions.Dashboard.IsOpen)
            return false;

        var existing = new HashSet<DashboardMember>();
        foreach (var session in Host.Store.Workspaces.SelectMany(workspace => workspace.Sessions))
        {
            if (!string.IsNullOrWhiteSpace(session.Surface?.PaneToken))
                existing.Add(new DashboardMember(session.Id, TerminalZoomSurface.Primary));
            if (!string.IsNullOrWhiteSpace(session.SplitSurface?.PaneToken))
                existing.Add(new DashboardMember(session.Id, TerminalZoomSurface.Split));
        }

        if (!Host.Actions.Dashboard.Reconcile(existing))
            return false;

        Host.Actions.RecomputeDashboardFontSize();
        SyncDashboardTransientStateLocked();
        return true;
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

    private static bool IsTypeable(string[] capturedForeground) =>
        CommandRestore.IsReplayable(capturedForeground) && WindowsCommandLine.IsInertForShells(capturedForeground);

    private SurfaceOpenRequest? CaptureRequest(Session session, LiveSurfaceKind kind)
    {
        IReadOnlyList<string> argv;
        string? cwd;
        string? initialInput = null;
        Guid? sessionId;
        var baseFontSize = session.FontSize ?? _currentBaseFontSize;
        // The integration is attached HERE, to the shell argv only, and never to the argv branches
        // below that carries a user's own program (the InitialCommand branch): Inject is shape-aware and
        // returns those untouched. A captured foreground command or restore override never sits in
        // argv: it is typed into the shell, and only when CommandRestore accepts it.
        var shellArgv = ShellIntegration.Inject(
            [ShellResolver.Resolve(_options.DefaultShell)],
            _options.ShellIntegrationDirectory);
        var restoreRunningCommand = _options.RestoreRunningCommand;
        string? restoredOutput = null;

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
                    initialInput = restoreRunningCommand && pendingOverride.Length > 0 &&
                                   !CommandRestore.HasControlCharacter(pendingOverride)
                        ? pendingOverride + "\n"
                        : null;
                }
                else if (capturedForeground is { Length: > 0 })
                {
                    argv = shellArgv;
                    initialInput = restoreRunningCommand && IsTypeable(capturedForeground)
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
                restoredOutput = TakeRestoredOutput(session.Id, StatusPane.Left);
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
                    initialInput = restoreRunningCommand && pendingOverride.Length > 0 &&
                                   !CommandRestore.HasControlCharacter(pendingOverride)
                        ? pendingOverride + "\n"
                        : null;
                }
                else if (capturedForeground is { Length: > 0 })
                {
                    initialInput = restoreRunningCommand && IsTypeable(capturedForeground)
                        ? WindowsCommandLine.FromArgv(capturedForeground) + "\n"
                        : null;
                }

                cwd = session.InitialSplitCwd ?? session.EffectiveCwd;
                sessionId = session.Id;
                restoredOutput = TakeRestoredOutput(session.Id, StatusPane.Right);
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

                argv =
                [
                    Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                    "/c",
                    overlayCommand,
                ];
                cwd = session.OverlayCwd ?? session.EffectiveCwd;
                sessionId = session.Id;
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
                ThemeName = sessionId is { } owner ? PaneThemeAssignment.ForSession(Store, owner) : null,
                RestoredOutput = restoredOutput,
            });
    }

    // The saved file is consumed even when the setting is off, so stale output never shows up
    // later after the user turns the setting back on.
    private string? TakeRestoredOutput(Guid sessionId, StatusPane pane)
    {
        string? restored;
        try
        {
            restored = ScrollbackStorage.Take(sessionId, pane, Host.Store.StateDirectory);
        }
        catch (InvalidOperationException)
        {
            // No state directory resolves (a test run without AGTERM_STATE_DIR): nothing is saved.
            return null;
        }

        return Volatile.Read(ref _notificationSettings).EffectiveRestoreScrollback ? restored : null;
    }

    private SurfaceOpenRequest CaptureQuickRequest(Session? activeSession) =>
        new(
            null,
            LiveSurfaceKind.Quick,
            new XtermSurfaceOptions
            {
                // The quick terminal is always a plain shell, so it always takes the integration.
                Argv = ShellIntegration.Inject(
                    [ShellResolver.Resolve(_options.DefaultShell)],
                    _options.ShellIntegrationDirectory).ToArray(),
                Cwd = ExistingDirectoryOrNull(activeSession?.EffectiveCwd) ??
                    ExistingDirectoryOrNull(_options.HomeDirectory),
                BaseFontSize = _currentBaseFontSize,
                ReadClipboard = _options.ReadClipboard,
            });

    private async Task PublishQuickSettlementAsync(PendingTerminalSurface pending)
    {
        if (await pending.Ready.ConfigureAwait(false) is not null)
        {
            return;
        }

        if (Volatile.Read(ref _shutdownStarted) != 0)
        {
            return;
        }

        Host.RunLocked(() =>
        {
            if (Volatile.Read(ref _shutdownStarted) == 0)
            {
                _storeCoordinator.PublishSelectionSnapshotLocked();
            }
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

    private void BindOverlay(Guid sessionId, XtermPaneSurface surface) =>
        _ = CloseOverlayOnExitAsync(sessionId, surface);

    private void BindQuick(XtermPaneSurface surface) =>
        _ = CloseQuickOnExitAsync(surface);

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

    private static bool OwnsOverlayPane(Session? live, XtermPaneSurface surface) =>
        live is not null &&
        ReferenceEquals(live.OverlaySurface, surface);

    private bool OwnsQuickPane(XtermPaneSurface surface) =>
        ReferenceEquals(Host.Actions.Quick.Surface, surface);

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

    private async Task CloseOverlayOnExitAsync(Guid sessionId, XtermPaneSurface surface)
    {
        int exitCode;
        try
        {
            exitCode = await surface.Pane.Exited.ConfigureAwait(false);
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
            if (!OwnsOverlayPane(live, surface)) return;
            Host.Store.RecordOverlayExit(live!.Id, exitCode);
            if (!live.OverlayWait)
            {
                Host.Store.CloseOverlay(live.Id);
            }
            ReconcileSurfacesLocked();
        });
    }

    private async Task CloseQuickOnExitAsync(XtermPaneSurface surface)
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
            if (Volatile.Read(ref _shutdownStarted) != 0 || !OwnsQuickPane(surface))
            {
                return;
            }

            Host.Actions.Quick.Visible = false;
            Host.Actions.Quick.Surface = null;
            surface.Teardown();
            _storeCoordinator.PublishSelectionSnapshotLocked();
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
        var scrollbackTargets = new List<(Guid SessionId, StatusPane Pane, IScrollbackSurface Surface)>();
        var unsplitSessions = new List<Guid>();
        Host.RunLocked(() =>
        {
            var distinctSurfaces = new HashSet<ITerminalSurface>(ReferenceEqualityComparer.Instance);
            var orderedSurfaces = new List<ITerminalSurface>();

            foreach (var surface in Host.Store.PreparePendingClosesForShutdown())
                AddSurface(surface);

            foreach (var session in Host.Store.Workspaces.SelectMany(w => w.Sessions))
            {
                AddSurface(session.Surface);
                AddSurface(session.SplitSurface);
                AddSurface(session.ScratchSurface);
                AddSurface(session.OverlaySurface);

                if (session.Surface is IScrollbackSurface main)
                    scrollbackTargets.Add((session.Id, StatusPane.Left, main));
                if (!session.HasSplit)
                    unsplitSessions.Add(session.Id);
                else if (session.SplitSurface is IScrollbackSurface split)
                    scrollbackTargets.Add((session.Id, StatusPane.Right, split));
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

        await SaveScrollbackAsync(scrollbackTargets, unsplitSessions).ConfigureAwait(false);

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

        Host.DetachLive();
    }

    /// <summary>Write each realized main/split pane's output for the next launch (Windows-only, see
    /// docs/PLATFORM-NOTES.md). Runs before StopOutput, while the panes still answer. Best effort:
    /// a pane that does not answer within the budget is skipped.</summary>
    private async Task SaveScrollbackAsync(
        IReadOnlyList<(Guid SessionId, StatusPane Pane, IScrollbackSurface Surface)> targets,
        IReadOnlyList<Guid> unsplitSessions)
    {
        var stateDir = Host.Store.StateDirectory;
        try
        {
            foreach (var sessionId in unsplitSessions)
                ScrollbackStorage.Write(sessionId, StatusPane.Right, string.Empty, stateDir);

            if (!Volatile.Read(ref _notificationSettings).EffectiveRestoreScrollback || targets.Count == 0)
                return;

            var captures = targets.Select(target => target.Surface.CaptureScrollbackAsync()).ToArray();
            try
            {
                await Task.WhenAll(captures).WaitAsync(ScrollbackCaptureBudget).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _options.Log?.Invoke($"LiveControlHost scrollback capture incomplete: {ex.Message}");
            }

            for (var i = 0; i < targets.Count; i++)
            {
                if (captures[i].IsCompletedSuccessfully && captures[i].Result is { } text)
                    ScrollbackStorage.Write(targets[i].SessionId, targets[i].Pane, text, stateDir);
            }
        }
        catch (InvalidOperationException)
        {
            // No state directory resolves (a test run without AGTERM_STATE_DIR): nothing is saved.
        }
    }
}
