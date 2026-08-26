using System.Collections.Generic;
using System.Threading;
using AgTerm.ControlServer;

namespace AgTerm.Xterm;

public enum LiveSurfaceKind
{
    Main,
}

public sealed record SurfaceOpenRequest(Guid? SessionId, LiveSurfaceKind Kind, XtermSurfaceOptions Options);

internal sealed class LiveDispatchScopeFactory : IControlDispatchScopeFactory
{
    private readonly AsyncLocal<LiveDispatchScope?> _ambient = new();

    internal LiveDispatchScope? Current => _ambient.Value;

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

                _accepting = false;
                var snapshot = _operations.ToArray();
                _operations.Clear();

                _owner.ClearAmbientIfCurrent(this);
                _completion = RunSnapshotAsync(snapshot, cancellationToken);
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
            CancellationToken cancellationToken)
        {
            await Task.WhenAll(operations.Select(operation => operation(cancellationToken)))
                .ConfigureAwait(false);
        }
    }
}

internal sealed class LiveSurfaceCoordinator : IDisposable
{
    private static readonly TimeSpan CanceledOpenDrainWindow = TimeSpan.FromSeconds(1);

    private static Task ScheduleWithTaskRun(Func<Task> work)
    {
        return Task.Run(work);
    }

    private readonly HeadlessHost _host;
    private readonly LiveDispatchScopeFactory _scopes;
    private readonly Func<SurfaceOpenRequest, CancellationToken, Task<IHeadlessSurface>> _openSurface;
    private readonly TimeSpan _realizeBudget;
    private readonly Action<Guid, IHeadlessSurface>? _committed;
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
        Action<Guid, IHeadlessSurface>? committed = null,
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

        if (request.Kind != LiveSurfaceKind.Main)
        {
            throw new NotSupportedException($"Unsupported surface kind: {request.Kind}");
        }

        if (request.SessionId is not { } sessionId)
        {
            throw new InvalidOperationException("Main surface requires a session id.");
        }

        var session = _host.Store.SessionWithId(sessionId)
            ?? throw new InvalidOperationException($"Session not found: {sessionId}");

        var pending = new PendingTerminalSurface();
        session.Surface = pending;
        Track(pending);

        if (!_scopes.TryRegister(ct => RunTrackedRealizeAsync(request, pending, ct)))
        {
            ObserveUnscopedFaults(RunUnscopedTrackedRealizeAsync(request, pending));
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
        PendingTerminalSurface pending,
        CancellationToken operationToken)
    {
        RegisterActiveRealization();
        return RunTrackedRegisteredRealizeAsync(request, pending, operationToken);
    }

    private async Task RunTrackedRegisteredRealizeAsync(
        SurfaceOpenRequest request,
        PendingTerminalSurface pending,
        CancellationToken operationToken)
    {
        try
        {
            await RunRealizeAsync(request, pending, operationToken).ConfigureAwait(false);
        }
        finally
        {
            CompleteActiveRealization();
        }
    }

    private Task RunUnscopedTrackedRealizeAsync(SurfaceOpenRequest request, PendingTerminalSurface pending)
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
                await RunRealizeAsync(request, pending, CancellationToken.None).ConfigureAwait(false);
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
            CancelPendingBeforeOpen(request, pending);
            workCompletion.TrySetException(ex);
            ReleaseActiveRegistration();
            return Task.FromException(ex);
        }

        return AwaitScheduledAndWorkAsync(scheduled, workCompletion.Task, request, pending, ReleaseActiveRegistration);
    }

    private async Task AwaitScheduledAndWorkAsync(
        Task scheduled,
        Task work,
        SurfaceOpenRequest request,
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
                CancelPendingBeforeOpen(request, pending);
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
        PendingTerminalSurface pending,
        CancellationToken operationToken)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            _shutdown.IsCancellationRequested ||
            operationToken.IsCancellationRequested ||
            pending.ReservationToken.IsCancellationRequested)
        {
            CancelPendingBeforeOpen(request, pending);
            return;
        }

        await RealizeAsync(request, pending, operationToken).ConfigureAwait(false);
    }

    private void CancelPendingBeforeOpen(SurfaceOpenRequest request, PendingTerminalSurface pending)
    {
        if (request.SessionId is { } sessionId)
        {
            ClearPendingSlot(sessionId, pending);
            pending.Settle(null);
        }
        else
        {
            pending.Teardown();
        }

        Untrack(pending);
    }

    private async Task RealizeAsync(
        SurfaceOpenRequest request,
        PendingTerminalSurface pending,
        CancellationToken operationToken)
    {
        if (request.SessionId is not { } sessionId)
        {
            pending.Teardown();
            Untrack(pending);
            return;
        }

        var stillCurrent = false;
        _host.RunLocked(() =>
        {
            var session = _host.Store.SessionWithId(sessionId);
            stillCurrent = session is not null && ReferenceEquals(session.Surface, pending);
        });

        if (!stillCurrent)
        {
            pending.Teardown();
            Untrack(pending);
            return;
        }

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
            _log?.Invoke($"LiveSurfaceCoordinator opener failed for {sessionId}: {ex}");
            ClearPendingSlot(sessionId, pending);
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
                _log?.Invoke($"LiveSurfaceCoordinator teardown failed for {sessionId}: {ex}");
            }
        }

        IHeadlessSurface realized;
        try
        {
            realized = await openTask.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ClearPendingSlot(sessionId, pending);
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
            _log?.Invoke($"LiveSurfaceCoordinator realize failed for {sessionId}: {ex}");
            ClearPendingSlot(sessionId, pending);
            pending.Settle(null);
            Untrack(pending);
            return;
        }

        var committed = false;
        _host.RunLocked(() =>
        {
            var session = _host.Store.SessionWithId(sessionId);
            if (session is null)
            {
                return;
            }

            if (!ReferenceEquals(session.Surface, pending))
            {
                return;
            }

            if (linked.IsCancellationRequested)
            {
                return;
            }

            session.Surface = realized;
            committed = true;
        });

        if (!committed)
        {
            ClearPendingSlot(sessionId, pending);
            TeardownSurfaceOnce(realized);
            pending.Settle(null);
            Untrack(pending);
            return;
        }

        pending.Settle(realized);
        try
        {
            _committed?.Invoke(sessionId, realized);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"LiveSurfaceCoordinator commit callback failed for {sessionId}: {ex}");
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

    private void ClearPendingSlot(Guid sessionId, PendingTerminalSurface pending)
    {
        _host.RunLocked(() =>
        {
            var session = _host.Store.SessionWithId(sessionId);
            if (session is not null && ReferenceEquals(session.Surface, pending))
            {
                session.Surface = null;
            }
        });
    }
}