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
    private readonly HeadlessHost _host;
    private readonly LiveDispatchScopeFactory _scopes;
    private readonly Func<SurfaceOpenRequest, CancellationToken, Task<IHeadlessSurface>> _openSurface;
    private readonly TimeSpan _realizeBudget;
    private readonly Action<Guid, IHeadlessSurface>? _committed;
    private readonly Action<string>? _log;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _activeGate = new();
    private readonly HashSet<PendingTerminalSurface> _active = new();
    private int _disposed;

    public LiveSurfaceCoordinator(
        HeadlessHost host,
        LiveDispatchScopeFactory scopes,
        Func<SurfaceOpenRequest, CancellationToken, Task<IHeadlessSurface>> openSurface,
        TimeSpan realizeBudget,
        Action<Guid, IHeadlessSurface>? committed = null,
        Action<string>? log = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _openSurface = openSurface ?? throw new ArgumentNullException(nameof(openSurface));
        _realizeBudget = realizeBudget;
        _committed = committed;
        _log = log;
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

        if (!_scopes.TryRegister(ct => RealizeAsync(request, pending, ct)))
        {
            var scheduled = Task.Run(() => RealizeAsync(request, pending, CancellationToken.None));
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

        return pending;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        // Active fire-and-forget operations may not have linked the shutdown token yet. Keep the
        // source alive; cancellation is the lifetime signal and no WaitHandle is allocated.

        PendingTerminalSurface[] snapshot;
        lock (_activeGate)
        {
            snapshot = _active.ToArray();
        }

        foreach (var pending in snapshot)
        {
            pending.Teardown();
        }
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