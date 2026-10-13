namespace AgTerm.App;

public sealed class ContentDialogCoordinator : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private int _disposed;

    /// <summary>Raised after each dialog run ends, also when the run faults, before the next queued
    /// dialog starts.</summary>
    public event Action? DialogClosed;

    public async Task<T> RunAsync<T>(Func<Task<T>> showAsync)
    {
        ArgumentNullException.ThrowIfNull(showAsync);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(_shutdown.Token);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return await showAsync();
        }
        finally
        {
            try
            {
                DialogClosed?.Invoke();
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _shutdown.Cancel();
    }
}