using AgTerm.ControlServer;
using AgTerm.Core.Model;

namespace AgTerm.Xterm;

public interface ILiveStoreCoordinator : IDisposable
{
    /// <summary>Safe to call from UI code; the store callback is scheduled off UI before it can
    /// enter the host gate. Return only immutable/materialized values from read.</summary>
    Task<T> ReadAsync<T>(Func<AppStore, T> read, CancellationToken cancellationToken = default);

    /// <summary>Safe to call from UI code; mutation is scheduled off UI and serialized by the
    /// host gate.</summary>
    Task MutateAsync(Action<AppStore> mutate, CancellationToken cancellationToken = default);

    event Action? SidebarInvalidated;

    event Action<Guid?>? SelectionChanged;
}

public sealed class LiveStoreCoordinator : ILiveStoreCoordinator
{
    private const string UiThreadGateError = "Cannot enter host gate from UI thread.";

    private readonly HeadlessHost _host;
    private readonly Func<bool> _isUiThread;
    private readonly Func<Action, CancellationToken, Task> _scheduler;
    private readonly Action _onSidebarChanged;
    private readonly Action _onSidebarVisibilityChanged;
    private readonly Action _onSelectionChanged;
    private int _disposed;
    private long _sidebarGeneration;
    private long _selectionGeneration;

    public event Action? SidebarInvalidated;

    public event Action<Guid?>? SelectionChanged;

    public LiveStoreCoordinator(HeadlessHost host, Func<bool> isUiThread)
        : this(host, isUiThread, static (work, cancellationToken) => Task.Run(work, cancellationToken))
    {
    }

    internal LiveStoreCoordinator(
        HeadlessHost host,
        Func<bool> isUiThread,
        Func<Action, CancellationToken, Task> scheduler)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _isUiThread = isUiThread ?? throw new ArgumentNullException(nameof(isUiThread));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));

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

        var generation = Interlocked.Increment(ref _sidebarGeneration);
        QueueEvent(() =>
        {
            if (generation == Volatile.Read(ref _sidebarGeneration))
                SidebarInvalidated?.Invoke();
        });
    }

    private void OnSelectionChanged()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var selectedSessionId = _host.Store.SelectedSessionId;
        var generation = Interlocked.Increment(ref _selectionGeneration);
        QueueEvent(() =>
        {
            if (generation == Volatile.Read(ref _selectionGeneration))
                SelectionChanged?.Invoke(selectedSessionId);
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