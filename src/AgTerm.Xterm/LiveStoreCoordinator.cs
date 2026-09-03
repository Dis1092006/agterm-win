using AgTerm.ControlServer;
using AgTerm.Core.Model;

namespace AgTerm.Xterm;

public sealed record LiveSelectionSnapshot(
    Guid? SessionId,
    string? DisplayName,
    string? WorkspaceName = null,
    string? MainPaneId = null,
    string? SplitPaneId = null,
    bool IsSplit = false,
    bool SplitFocused = false,
    double? SplitRatio = null,
    string? ScratchPaneId = null,
    bool ScratchActive = false,
    string? OverlayPaneId = null,
    bool OverlayActive = false,
    int? OverlaySizePercent = null,
    string? OverlayBackgroundColor = null,
    string? SearchOwnerPaneId = null,
    bool SearchActive = false,
    string SearchNeedle = "",
    string SearchDisplayText = "",
    int? SearchTotal = null,
    int? SearchSelected = null,
    string? QuickPaneId = null,
    bool QuickVisible = false,
    string? ZoomPaneId = null,
    bool DashboardOpen = false,
    IReadOnlyList<TerminalDashboardCell>? DashboardCells = null,
    string? DashboardHighlighted = null,
    double? DashboardFontSize = null)
{
    public TerminalLayout ToTerminalLayout(long generation)
    {
        TerminalLayout baseLayout;
        if (SessionId is null || string.IsNullOrWhiteSpace(DisplayName) || string.IsNullOrWhiteSpace(MainPaneId))
        {
            baseLayout = new TerminalLayout.Empty(generation);
        }
        else if (IsSplit && !string.IsNullOrWhiteSpace(SplitPaneId))
        {
            var ratio = SplitRatio ?? AppStore.SplitRatioDefault;
            var focusedPaneId = SplitFocused ? SplitPaneId : MainPaneId;
            baseLayout = new TerminalLayout.Split(
                generation,
                MainPaneId,
                SplitPaneId,
                ratio,
                focusedPaneId,
                DisplayName,
                WorkspaceName);
        }
        else
        {
            var paneId = !IsSplit && SplitFocused && !string.IsNullOrWhiteSpace(SplitPaneId)
                ? SplitPaneId
                : MainPaneId;
            baseLayout = new TerminalLayout.Single(generation, paneId, DisplayName, WorkspaceName);
        }

        if (DashboardCells is { Count: > 0 } dashboardCells
            && !string.IsNullOrWhiteSpace(DashboardHighlighted))
        {
            return new TerminalLayout.Dashboard(
                generation,
                dashboardCells,
                DashboardHighlighted,
                DashboardFontSize);
        }

        if (!string.IsNullOrWhiteSpace(ZoomPaneId))
        {
            return new TerminalLayout.Zoomed(
                generation,
                ZoomPaneId,
                DisplayName,
                WorkspaceName);
        }

        if (QuickVisible && !string.IsNullOrWhiteSpace(QuickPaneId))
        {
            return new TerminalLayout.Layered(
                generation,
                baseLayout,
                new TerminalLayer(QuickPaneId, sizePercent: 90));
        }

        if (baseLayout is TerminalLayout.Empty)
        {
            return baseLayout;
        }

        if (OverlayActive && !string.IsNullOrWhiteSpace(OverlayPaneId))
        {
            return new TerminalLayout.Layered(
                generation,
                baseLayout,
                new TerminalLayer(OverlayPaneId, OverlaySizePercent, OverlayBackgroundColor));
        }

        if (ScratchActive && !string.IsNullOrWhiteSpace(ScratchPaneId))
        {
            return new TerminalLayout.Layered(generation, baseLayout, new TerminalLayer(ScratchPaneId));
        }

        return baseLayout;
    }
}

public interface ILiveStoreCoordinator : IDisposable
{
    /// <summary>Safe to call from UI code; the store callback is scheduled off UI before it can
    /// enter the host gate. Return only immutable/materialized values from read.</summary>
    Task<T> ReadAsync<T>(Func<AppStore, T> read, CancellationToken cancellationToken = default);

    /// <summary>Capture external immutable state off UI and before the host gate, then combine it
    /// with AppStore under serialized access.</summary>
    Task<TResult> ReadAsync<TSnapshot, TResult>(
        Func<TSnapshot> captureOutsideGate,
        Func<AppStore, TSnapshot, TResult> readUnderGate,
        CancellationToken cancellationToken = default);

    /// <summary>Safe to call from UI code; mutation is scheduled off UI and serialized by the
    /// host gate.</summary>
    Task MutateAsync(Action<AppStore> mutate, CancellationToken cancellationToken = default);

    event Action? SidebarInvalidated;

    event Action<Guid?>? SelectionChanged;

    event Action<LiveSelectionSnapshot>? SelectionSnapshotChanged;
}

public sealed class LiveStoreCoordinator : ILiveStoreCoordinator
{
    private const string UiThreadGateError = "Cannot enter host gate from UI thread.";

    private readonly ILiveControlRuntime _host;
    private readonly Func<bool> _isUiThread;
    private readonly Func<Action, CancellationToken, Task> _scheduler;
    private readonly Action? _afterMutation;
    private readonly Action _onSidebarChanged;
    private readonly Action _onSidebarVisibilityChanged;
    private readonly Action _onSelectionChanged;
    private int _disposed;
    private long _sidebarGeneration;
    private long _selectionNotificationGeneration;
    private long _selectionSnapshotGeneration;

    public event Action? SidebarInvalidated;

    public event Action<Guid?>? SelectionChanged;

    public event Action<LiveSelectionSnapshot>? SelectionSnapshotChanged;

    public LiveStoreCoordinator(HeadlessHost host, Func<bool> isUiThread, Action? afterMutation = null)
        : this(new HeadlessControlRuntime(host), isUiThread,
            static (work, cancellationToken) => Task.Run(work, cancellationToken), afterMutation)
    {
    }

    internal LiveStoreCoordinator(
        ILiveControlRuntime host,
        Func<bool> isUiThread,
        Action? afterMutation = null)
        : this(host, isUiThread,
            static (work, cancellationToken) => Task.Run(work, cancellationToken), afterMutation)
    {
    }

    internal LiveStoreCoordinator(
        HeadlessHost host,
        Func<bool> isUiThread,
        Func<Action, CancellationToken, Task> scheduler,
        Action? afterMutation = null)
        : this(new HeadlessControlRuntime(host), isUiThread, scheduler, afterMutation)
    {
    }

    internal LiveStoreCoordinator(
        ILiveControlRuntime host,
        Func<bool> isUiThread,
        Func<Action, CancellationToken, Task> scheduler,
        Action? afterMutation = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _isUiThread = isUiThread ?? throw new ArgumentNullException(nameof(isUiThread));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _afterMutation = afterMutation;

        _onSidebarChanged = OnSidebarInvalidated;
        _onSidebarVisibilityChanged = OnSidebarInvalidated;
        _onSelectionChanged = OnSelectionChanged;

        _host.RunLocked(() =>
        {
            _host.Store.SidebarChanged += _onSidebarChanged;
            _host.Store.SidebarVisibilityChanged += _onSidebarVisibilityChanged;
            _host.Store.SelectionChanged += _onSelectionChanged;
        });
    }

    public async Task<T> ReadAsync<T>(
        Func<AppStore, T> read,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        T result = default!;
        await _scheduler(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            // The public API is callable from UI. This guard validates the scheduled callback,
            // immediately before it can enter the host gate.
            ThrowIfUiThread();

            _host.RunLocked(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                result = read(_host.Store);
            });
        }, cancellationToken).ConfigureAwait(false);

        return result;
    }

    public async Task<TResult> ReadAsync<TSnapshot, TResult>(
        Func<TSnapshot> captureOutsideGate,
        Func<AppStore, TSnapshot, TResult> readUnderGate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captureOutsideGate);
        ArgumentNullException.ThrowIfNull(readUnderGate);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        TResult result = default!;
        await _scheduler(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            ThrowIfUiThread();

            var snapshot = captureOutsideGate();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            _host.RunLocked(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                result = readUnderGate(_host.Store, snapshot);
            });
        }, cancellationToken).ConfigureAwait(false);

        return result;
    }

    public async Task MutateAsync(
        Action<AppStore> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        await _scheduler(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            ThrowIfUiThread();

            _host.RunLocked(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                mutate(_host.Store);
                _afterMutation?.Invoke();
            });
        }, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _host.RunLocked(() =>
        {
            _host.Store.SidebarChanged -= _onSidebarChanged;
            _host.Store.SidebarVisibilityChanged -= _onSidebarVisibilityChanged;
            _host.Store.SelectionChanged -= _onSelectionChanged;
        });
    }

    private void OnSidebarInvalidated()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var snapshot = CaptureSelectionSnapshot();

        var generation = Interlocked.Increment(ref _sidebarGeneration);
        var sidebarInvalidated = SidebarInvalidated;
        if (sidebarInvalidated is not null)
        {
            QueueEvent(() =>
            {
                if (generation == Volatile.Read(ref _sidebarGeneration))
                    sidebarInvalidated();
            });
        }

        PublishSelectionSnapshot(snapshot);
    }

    private void OnSelectionChanged()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var snapshot = CaptureSelectionSnapshot();

        var generation = Interlocked.Increment(ref _selectionNotificationGeneration);
        var selectionChanged = SelectionChanged;
        if (selectionChanged is not null)
        {
            QueueEvent(() =>
            {
                if (generation == Volatile.Read(ref _selectionNotificationGeneration))
                {
                    selectionChanged(snapshot.SessionId);
                }
            });
        }

        PublishSelectionSnapshot(snapshot);
    }

    private LiveSelectionSnapshot CaptureSelectionSnapshot()
    {
        _host.Actions.ReconcileSurfaceZoom();
        var selectedSessionId = _host.Store.SelectedSessionId;
        var dashboardCells = CaptureDashboardCells();
        var dashboardHighlighted = _host.Actions.Dashboard.IsOpen
            ? _host.Actions.Dashboard.Highlighted?.ControlRef
            : null;
        var dashboardFontSize = _host.Actions.Dashboard.IsOpen
            ? _host.Actions.Dashboard.AppliedFontSize
            : null;
        if (selectedSessionId is not { } id)
        {
            return new LiveSelectionSnapshot(
                selectedSessionId,
                null,
                null,
                QuickPaneId: _host.Actions.Quick.Surface?.PaneToken,
                QuickVisible: _host.Actions.Quick.Visible,
                ZoomPaneId: CaptureZoomPaneId(),
                DashboardOpen: _host.Actions.Dashboard.IsOpen,
                DashboardCells: dashboardCells,
                DashboardHighlighted: dashboardHighlighted,
                DashboardFontSize: dashboardFontSize);
        }

        var selectedSession = _host.Store.SessionWithId(id);
        var selectedDisplayName = selectedSession?.DisplayName;
        var selectedWorkspaceName = _host.Store.WorkspaceForSession(id)?.Name;

        return new LiveSelectionSnapshot(
            selectedSessionId,
            selectedDisplayName,
            selectedWorkspaceName,
            selectedSession?.Surface?.PaneToken,
            selectedSession?.SplitSurface?.PaneToken,
            selectedSession?.IsSplit ?? false,
            selectedSession?.SplitFocused ?? false,
            selectedSession?.SplitRatio,
            selectedSession?.ScratchSurface?.PaneToken,
            selectedSession?.ScratchActive ?? false,
            selectedSession?.OverlaySurface?.PaneToken,
            selectedSession?.OverlayActive ?? false,
            selectedSession?.OverlaySizePercent,
            selectedSession?.OverlayBackgroundColor,
            selectedSession?.SearchSurface?.PaneToken,
            selectedSession?.SearchActive ?? false,
            selectedSession?.SearchNeedle ?? "",
            selectedSession?.SearchDisplayText ?? "",
            selectedSession?.SearchTotal,
            selectedSession?.SearchSelected,
            _host.Actions.Quick.Surface?.PaneToken,
            _host.Actions.Quick.Visible,
            CaptureZoomPaneId(),
            _host.Actions.Dashboard.IsOpen,
            dashboardCells,
            dashboardHighlighted,
            dashboardFontSize);
    }

    private IReadOnlyList<TerminalDashboardCell> CaptureDashboardCells()
    {
        if (!_host.Actions.Dashboard.IsOpen)
            return Array.Empty<TerminalDashboardCell>();

        var cells = new List<TerminalDashboardCell>(_host.Actions.Dashboard.Members.Count);
        foreach (var member in _host.Actions.Dashboard.Members)
        {
            var session = _host.Store.SessionWithId(member.Session);
            var paneId = member.Surface switch
            {
                TerminalZoomSurface.Primary => session?.Surface?.PaneToken,
                TerminalZoomSurface.Split => session?.SplitSurface?.PaneToken,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(paneId))
                cells.Add(new TerminalDashboardCell(paneId, member.ControlRef));
        }
        return cells.ToArray();
    }

    private string? CaptureZoomPaneId() => _host.Actions.Zoom.Target switch
    {
        TerminalZoomTarget.Quick => _host.Actions.Quick.Surface?.PaneToken,
        TerminalZoomTarget.Session target => CaptureSessionZoomPaneId(target),
        null => null,
        _ => throw new InvalidOperationException("Unknown terminal zoom target."),
    };

    private string? CaptureSessionZoomPaneId(TerminalZoomTarget.Session target)
    {
        var session = _host.Store.SessionWithId(target.SessionId);
        return target.Surface switch
        {
            TerminalZoomSurface.Primary => session?.Surface?.PaneToken,
            TerminalZoomSurface.Split => session?.SplitSurface?.PaneToken,
            TerminalZoomSurface.Scratch => session?.ScratchSurface?.PaneToken,
            TerminalZoomSurface.Overlay => session?.OverlaySurface?.PaneToken,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
    }

    internal void PublishSelectionSnapshotLocked() =>
        PublishSelectionSnapshot(CaptureSelectionSnapshot());

    private void PublishSelectionSnapshot(LiveSelectionSnapshot snapshot)
    {
        var generation = Interlocked.Increment(ref _selectionSnapshotGeneration);
        var selectionSnapshotChanged = SelectionSnapshotChanged;
        if (selectionSnapshotChanged is null)
        {
            return;
        }

        QueueEvent(() =>
        {
            if (generation == Volatile.Read(ref _selectionSnapshotGeneration))
            {
                selectionSnapshotChanged(snapshot);
            }
        });
    }

    private void QueueEvent(Action delivery)
    {
        Task task;
        try
        {
            task = _scheduler(() =>
            {
                if (Volatile.Read(ref _disposed) == 0)
                    delivery();
            }, CancellationToken.None);
        }
        catch
        {
            return;
        }

        _ = task.ContinueWith(
            static task => System.Diagnostics.Debug.WriteLine(
                $"LiveStoreCoordinator event subscriber failed: {task.Exception}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(LiveStoreCoordinator));
        }
    }

    private void ThrowIfUiThread()
    {
        if (_isUiThread())
        {
            throw new InvalidOperationException(UiThreadGateError);
        }
    }
}