using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using AgTerm.ControlServer;
using AgTerm.Core.Model;

namespace AgTerm.Xterm;

public enum LiveSurfaceKind
{
    Main,
    Split,
    Scratch,
    Overlay,
    Quick,
}

public sealed record SurfaceOpenRequest(Guid? SessionId, LiveSurfaceKind Kind, XtermSurfaceOptions Options);

internal sealed class LiveDispatchScopeFactory : IControlDispatchScopeFactory
{
    private readonly AsyncLocal<LiveDispatchScope?> _ambient = new();
    private Action? _beforeComplete;

    internal LiveDispatchScope? Current => _ambient.Value;

    internal void SetBeforeComplete(Action? beforeComplete)
    {
        _beforeComplete = beforeComplete;
    }

    public IControlDispatchScope Begin()
    {
        var existing = _ambient.Value;
        if (existing is not null && existing.IsAccepting)
        {
            throw new InvalidOperationException("Nested live dispatch scopes are not supported.");
        }

        var scope = new LiveDispatchScope(this);
        _ambient.Value = scope;
        return scope;
    }

    internal bool TryRegister(Func<CancellationToken, Task> operation)
    {
        var scope = _ambient.Value;
        if (scope is null)
        {
            return false;
        }

        return scope.TryRegister(operation);
    }

    internal void ClearAmbientIfCurrent(LiveDispatchScope scope)
    {
        if (ReferenceEquals(_ambient.Value, scope))
        {
            _ambient.Value = null;
        }
    }

    internal void InvokeBeforeComplete()
    {
        _beforeComplete?.Invoke();
    }

    internal sealed class LiveDispatchScope : IControlDispatchScope
    {
        private readonly LiveDispatchScopeFactory _owner;
        private readonly object _gate = new();
        private readonly List<Func<CancellationToken, Task>> _operations = new();
        private Task? _completion;
        private bool _accepting = true;

        internal LiveDispatchScope(LiveDispatchScopeFactory owner)
        {
            _owner = owner;
        }

        internal bool IsAccepting
        {
            get
            {
                lock (_gate)
                {
                    return _accepting;
                }
            }
        }

        internal bool TryRegister(Func<CancellationToken, Task> operation)
        {
            if (operation is null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            lock (_gate)
            {
                if (!_accepting)
                {
                    return false;
                }

                if (!ReferenceEquals(_owner.Current, this))
                {
                    return false;
                }

                _operations.Add(operation);
                return true;
            }
        }

        public Task CompleteAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_completion is not null)
                {
                    return _completion;
                }

                _completion = CompleteCoreAsync(cancellationToken);
                return _completion;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _accepting = false;
                _operations.Clear();
            }

            _owner.ClearAmbientIfCurrent(this);
        }

        private static async Task RunSnapshotAsync(
            IReadOnlyList<Func<CancellationToken, Task>> operations,
            CancellationToken cancellationToken,
            Exception? callbackFailure = null)
        {
            Exception? operationFailure = null;
            try
            {
                await Task.WhenAll(operations.Select(operation => operation(cancellationToken)))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                operationFailure = ex;
            }

            if (callbackFailure is not null && operationFailure is not null)
            {
                throw new AggregateException(callbackFailure, operationFailure);
            }

            if (callbackFailure is not null)
            {
                ExceptionDispatchInfo.Capture(callbackFailure).Throw();
            }

            if (operationFailure is not null)
            {
                ExceptionDispatchInfo.Capture(operationFailure).Throw();
            }
        }

        private Task CompleteCoreAsync(CancellationToken cancellationToken)
        {
            Exception? callbackFailure = null;
            try
            {
                _owner.InvokeBeforeComplete();
            }
            catch (Exception ex)
            {
                callbackFailure = ex;
            }

            IReadOnlyList<Func<CancellationToken, Task>> snapshot;
            lock (_gate)
            {
                _accepting = false;
                snapshot = _operations.ToArray();
                _operations.Clear();
                _owner.ClearAmbientIfCurrent(this);
            }

            return RunSnapshotAsync(snapshot, cancellationToken, callbackFailure);
        }
    }
}

internal sealed class LiveSurfaceCoordinator : IDisposable
{
    private static readonly TimeSpan CanceledOpenDrainWindow = TimeSpan.FromSeconds(1);

    private readonly record struct SurfaceSlot(LiveSurfaceKind Kind, Guid? SessionId);

    private static Task ScheduleWithTaskRun(Func<Task> work)
    {
        return Task.Run(work);
    }

    private readonly HeadlessHost _host;
    private readonly LiveDispatchScopeFactory _scopes;
    private readonly Func<SurfaceOpenRequest, CancellationToken, Task<IHeadlessSurface>> _openSurface;
    private readonly TimeSpan _realizeBudget;
    private readonly Action<SurfaceOpenRequest, IHeadlessSurface>? _committed;
    private readonly Action<string>? _log;
    private readonly Func<Func<Task>, Task> _schedule;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _activeGate = new();
    private readonly HashSet<PendingTerminalSurface> _active = new();
    private TaskCompletionSource<object?>? _idleCompletion;
    private int _activeRealizationCount;
    private int _disposed;

    public LiveSurfaceCoordinator(
        HeadlessHost host,
        LiveDispatchScopeFactory scopes,
        Func<SurfaceOpenRequest, CancellationToken, Task<IHeadlessSurface>> openSurface,
        TimeSpan realizeBudget,
        Action<Guid, IHeadlessSurface>? committed,
        Action<string>? log = null,
        Func<Func<Task>, Task>? schedule = null)
        : this(
            host,
            scopes,
            openSurface,
            realizeBudget,
            committed is null
                ? null
                : (request, surface) =>
                {
                    if (request.SessionId is { } sessionId)
                    {
                        committed(sessionId, surface);
                    }
                },
            log,
            schedule)
    {
    }

    public LiveSurfaceCoordinator(
        HeadlessHost host,
        LiveDispatchScopeFactory scopes,
        Func<SurfaceOpenRequest, CancellationToken, Task<IHeadlessSurface>> openSurface,
        TimeSpan realizeBudget,
        Action<SurfaceOpenRequest, IHeadlessSurface>? committed = null,
        Action<string>? log = null,
        Func<Func<Task>, Task>? schedule = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _openSurface = openSurface ?? throw new ArgumentNullException(nameof(openSurface));
        _realizeBudget = realizeBudget;
        _committed = committed;
        _log = log;
        _schedule = schedule ?? ScheduleWithTaskRun;
    }

    public PendingTerminalSurface Reserve(SurfaceOpenRequest request)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var slot = ResolveSlot(request);

        var pending = new PendingTerminalSurface();
        AssignSlot(slot, pending);
        Track(pending);

        if (!_scopes.TryRegister(ct => RunTrackedRealizeAsync(request, slot, pending, ct)))
        {
            ObserveUnscopedFaults(RunUnscopedTrackedRealizeAsync(request, slot, pending));
        }

        return pending;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        var idleTask = BeginShutdown();
        await idleTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _ = BeginShutdown();
    }

    private Task BeginShutdown()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _shutdown.Cancel();
            // Active fire-and-forget operations may not have linked the shutdown token yet. Keep the
            // source alive; cancellation is the lifetime signal and no WaitHandle is allocated.

            PendingTerminalSurface[] pendingSnapshot;
            lock (_activeGate)
            {
                pendingSnapshot = _active.ToArray();
            }

            foreach (var pending in pendingSnapshot)
            {
                pending.Teardown();
            }
        }

        lock (_activeGate)
        {
            if (_activeRealizationCount == 0)
            {
                return Task.CompletedTask;
            }

            return _idleCompletion?.Task ?? Task.CompletedTask;
        }
    }

    private Task RunTrackedRealizeAsync(
        SurfaceOpenRequest request,
        SurfaceSlot slot,
        PendingTerminalSurface pending,
        CancellationToken operationToken)
    {
        RegisterActiveRealization();
        return RunTrackedRegisteredRealizeAsync(request, slot, pending, operationToken);
    }

    private async Task RunTrackedRegisteredRealizeAsync(
        SurfaceOpenRequest request,
        SurfaceSlot slot,
        PendingTerminalSurface pending,
        CancellationToken operationToken)
    {
        try
        {
            await RunRealizeAsync(request, slot, pending, operationToken).ConfigureAwait(false);
        }
        finally
        {
            CompleteActiveRealization();
        }
    }

    private Task RunUnscopedTrackedRealizeAsync(
        SurfaceOpenRequest request,
        SurfaceSlot slot,
        PendingTerminalSurface pending)
    {
        RegisterActiveRealization();

        var workCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseActiveOnce = 0;

        void ReleaseActiveRegistration()
        {
            if (Interlocked.Exchange(ref releaseActiveOnce, 1) != 0)
            {
                return;
            }

            CompleteActiveRealization();
        }

        async Task RegisteredWorkAsync()
        {
            try
            {
                await RunRealizeAsync(request, slot, pending, CancellationToken.None).ConfigureAwait(false);
                workCompletion.TrySetResult(null);
            }
            catch (Exception ex)
            {
                workCompletion.TrySetException(ex);
                throw;
            }
            finally
            {
                ReleaseActiveRegistration();
            }
        }

        Task scheduled;
        try
        {
            scheduled = _schedule(RegisteredWorkAsync);
        }
        catch (Exception ex)
        {
            CancelPendingBeforeOpen(slot, pending);
            workCompletion.TrySetException(ex);
            ReleaseActiveRegistration();
            return Task.FromException(ex);
        }

        return AwaitScheduledAndWorkAsync(
            scheduled,
            workCompletion.Task,
            slot,
            pending,
            ReleaseActiveRegistration);
    }

    private async Task AwaitScheduledAndWorkAsync(
        Task scheduled,
        Task work,
        SurfaceSlot slot,
        PendingTerminalSurface pending,
        Action releaseActiveRegistration)
    {
        try
        {
            await scheduled.ConfigureAwait(false);
            await work.ConfigureAwait(false);
        }
        catch
        {
            if (!work.IsCompleted)
            {
                CancelPendingBeforeOpen(slot, pending);
                releaseActiveRegistration();
            }

            throw;
        }
    }

    private void ObserveUnscopedFaults(Task scheduled)
    {
        _ = scheduled.ContinueWith(
            t =>
            {
                _ = t.Exception;
                _log?.Invoke($"LiveSurfaceCoordinator unscoped operation faulted: {t.Exception}");
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void RegisterActiveRealization()
    {
        lock (_activeGate)
        {
            if (_activeRealizationCount == 0)
            {
                _idleCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            _activeRealizationCount++;
        }
    }

    private void CompleteActiveRealization()
    {
        TaskCompletionSource<object?>? idleToComplete = null;
        lock (_activeGate)
        {
            if (_activeRealizationCount <= 0)
            {
                return;
            }

            _activeRealizationCount--;
            if (_activeRealizationCount == 0)
            {
                idleToComplete = _idleCompletion;
                _idleCompletion = null;
            }
        }

        idleToComplete?.TrySetResult(null);
    }

    private async Task RunRealizeAsync(
        SurfaceOpenRequest request,
        SurfaceSlot slot,
        PendingTerminalSurface pending,
        CancellationToken operationToken)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            _shutdown.IsCancellationRequested ||
            operationToken.IsCancellationRequested ||
            pending.ReservationToken.IsCancellationRequested)
        {
            CancelPendingBeforeOpen(slot, pending);
            return;
        }

        await RealizeAsync(request, slot, pending, operationToken).ConfigureAwait(false);
    }

    private void CancelPendingBeforeOpen(SurfaceSlot slot, PendingTerminalSurface pending)
    {
        ClearPendingSlot(slot, pending);
        pending.Settle(null);

        Untrack(pending);
    }

    private async Task RealizeAsync(
        SurfaceOpenRequest request,
        SurfaceSlot slot,
        PendingTerminalSurface pending,
        CancellationToken operationToken)
    {
        using var budgetCts = new CancellationTokenSource(_realizeBudget);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            operationToken,
            _shutdown.Token,
            pending.ReservationToken,
            budgetCts.Token);

        Task<IHeadlessSurface> openTask;
        try
        {
            openTask = _openSurface(request, linked.Token);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"LiveSurfaceCoordinator opener failed for {DescribeSlot(slot)}: {ex}");
            ClearPendingSlot(slot, pending);
            pending.Settle(null);
            Untrack(pending);
            return;
        }

        var teardownOnce = 0;
        void TeardownSurfaceOnce(IHeadlessSurface? surface)
        {
            if (surface is null)
            {
                return;
            }

            if (Interlocked.Exchange(ref teardownOnce, 1) != 0)
            {
                return;
            }

            try
            {
                surface.Teardown();
            }
            catch (Exception ex)
            {
                _log?.Invoke($"LiveSurfaceCoordinator teardown failed for {DescribeSlot(slot)}: {ex}");
            }
        }

        IHeadlessSurface realized;
        try
        {
            realized = await openTask.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ClearPendingSlot(slot, pending);
            // Ready means this attempt settled. ReservationToken records slot teardown specifically;
            // timeout/server/shutdown cancellation is carried by the linked opener token instead.
            pending.Settle(null);
            Untrack(pending);

            var completedTask = await Task.WhenAny(openTask, Task.Delay(CanceledOpenDrainWindow))
                .ConfigureAwait(false);
            if (ReferenceEquals(completedTask, openTask))
            {
                try
                {
                    var surface = await openTask.ConfigureAwait(false);
                    TeardownSurfaceOnce(surface);
                }
                catch (OperationCanceledException)
                {
                    // Already canceled. Observe and continue shutdown.
                }
                catch (Exception)
                {
                    _ = openTask.Exception;
                }
            }
            else
            {
                _ = openTask.ContinueWith(
                    t =>
                    {
                        if (t.Status == TaskStatus.RanToCompletion)
                        {
                            TeardownSurfaceOnce(t.Result);
                        }
                        else
                        {
                            _ = t.Exception;
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            return;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"LiveSurfaceCoordinator realize failed for {DescribeSlot(slot)}: {ex}");
            ClearPendingSlot(slot, pending);
            pending.Settle(null);
            Untrack(pending);
            return;
        }

        var committed = false;
        _host.RunLocked(() =>
        {
            if (!TryGetSlotSurface(slot, out var current))
            {
                return;
            }

            if (!ReferenceEquals(current, pending))
            {
                return;
            }

            if (linked.IsCancellationRequested)
            {
                return;
            }

            AssignSlot(slot, realized);
            if (ShouldNotifySidebar(slot))
            {
                _host.Store.NotifySidebar();
            }
            committed = true;
        });

        if (!committed)
        {
            ClearPendingSlot(slot, pending);
            TeardownSurfaceOnce(realized);
            pending.Settle(null);
            Untrack(pending);
            return;
        }

        pending.Settle(realized);
        try
        {
            _committed?.Invoke(request, realized);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"LiveSurfaceCoordinator commit callback failed for {DescribeSlot(slot)}: {ex}");
        }

        Untrack(pending);
    }

    private void Track(PendingTerminalSurface pending)
    {
        lock (_activeGate)
        {
            _active.Add(pending);
        }
    }

    private void Untrack(PendingTerminalSurface pending)
    {
        lock (_activeGate)
        {
            _active.Remove(pending);
        }
    }

    private SurfaceSlot ResolveSlot(SurfaceOpenRequest request)
    {
        switch (request.Kind)
        {
            case LiveSurfaceKind.Main:
            {
                if (request.SessionId is not { } sessionId)
                {
                    throw new InvalidOperationException("Main surface requires a session id.");
                }

                EnsureSessionExists(sessionId);
                return new SurfaceSlot(request.Kind, sessionId);
            }

            case LiveSurfaceKind.Split:
            case LiveSurfaceKind.Scratch:
            case LiveSurfaceKind.Overlay:
            {
                if (request.SessionId is not { } sessionId)
                {
                    throw new InvalidOperationException($"{request.Kind} surface requires a session id.");
                }

                EnsureSessionExists(sessionId);
                return new SurfaceSlot(request.Kind, sessionId);
            }

            case LiveSurfaceKind.Quick:
            {
                if (request.SessionId is not null)
                {
                    throw new InvalidOperationException("Quick surface does not accept a session id.");
                }

                return new SurfaceSlot(request.Kind, null);
            }

            default:
                throw new NotSupportedException($"Unsupported surface kind: {request.Kind}");
        }
    }

    private void EnsureSessionExists(Guid sessionId)
    {
        _ = _host.Store.SessionWithId(sessionId)
            ?? throw new InvalidOperationException($"Session not found: {sessionId}");
    }

    private bool ShouldNotifySidebar(SurfaceSlot slot) =>
        slot.Kind is LiveSurfaceKind.Main or LiveSurfaceKind.Split;

    private bool TryGetSlotSurface(SurfaceSlot slot, out ITerminalSurface? surface)
    {
        switch (slot.Kind)
        {
            case LiveSurfaceKind.Main:
            case LiveSurfaceKind.Split:
            case LiveSurfaceKind.Scratch:
            case LiveSurfaceKind.Overlay:
            {
                if (slot.SessionId is not { } sessionId)
                {
                    surface = null;
                    return false;
                }

                var session = _host.Store.SessionWithId(sessionId);
                if (session is null)
                {
                    surface = null;
                    return false;
                }

                surface = slot.Kind switch
                {
                    LiveSurfaceKind.Main => session.Surface,
                    LiveSurfaceKind.Split => session.SplitSurface,
                    LiveSurfaceKind.Scratch => session.ScratchSurface,
                    LiveSurfaceKind.Overlay => session.OverlaySurface,
                    _ => null,
                };

                return true;
            }

            case LiveSurfaceKind.Quick:
                surface = _host.Actions.Quick.Surface;
                return true;

            default:
                surface = null;
                return false;
        }
    }

    private void AssignSlot(SurfaceSlot slot, ITerminalSurface? surface)
    {
        switch (slot.Kind)
        {
            case LiveSurfaceKind.Main:
            case LiveSurfaceKind.Split:
            case LiveSurfaceKind.Scratch:
            case LiveSurfaceKind.Overlay:
            {
                if (slot.SessionId is not { } sessionId)
                {
                    return;
                }

                var session = _host.Store.SessionWithId(sessionId);
                if (session is null)
                {
                    return;
                }

                switch (slot.Kind)
                {
                    case LiveSurfaceKind.Main:
                        session.Surface = surface;
                        break;
                    case LiveSurfaceKind.Split:
                        session.SplitSurface = surface;
                        break;
                    case LiveSurfaceKind.Scratch:
                        session.ScratchSurface = surface;
                        break;
                    case LiveSurfaceKind.Overlay:
                        session.OverlaySurface = surface;
                        break;
                }

                return;
            }

            case LiveSurfaceKind.Quick:
                _host.Actions.Quick.Surface = surface as IHeadlessSurface;
                return;
        }
    }

    private static string DescribeSlot(SurfaceSlot slot)
    {
        return slot.SessionId is { } sessionId
            ? $"{slot.Kind}:{sessionId}"
            : slot.Kind.ToString();
    }

    private void ClearPendingSlot(SurfaceSlot slot, PendingTerminalSurface pending)
    {
        _host.RunLocked(() =>
        {
            if (!TryGetSlotSurface(slot, out var current))
            {
                return;
            }

            if (!ReferenceEquals(current, pending))
            {
                return;
            }

            AssignSlot(slot, null);
            if (ShouldNotifySidebar(slot))
            {
                _host.Store.NotifySidebar();
            }
        });
    }
}