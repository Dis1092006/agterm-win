using System.Threading;

namespace AgTerm.Xterm;

public sealed class TerminalLayoutCoordinator : IDisposable
{
    private readonly Func<TerminalLayout, Task<bool>> _applyLayout;
    private readonly object _gate = new();
    private TaskCompletionSource _idleTcs = CompletedIdle();
    private TerminalLayout? _pending;
    private long _highestSubmittedGeneration = long.MinValue;
    private bool _workerActive;
    private bool _disposed;

    public TerminalLayoutCoordinator(Func<TerminalLayout, Task<bool>> applyLayout)
    {
        _applyLayout = applyLayout ?? throw new ArgumentNullException(nameof(applyLayout));
    }

    public event EventHandler<TerminalLayout>? LayoutAccepted;

    public void Submit(TerminalLayout layout)
    {
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        var shouldStartWorker = false;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (layout.Generation < _highestSubmittedGeneration)
            {
                return;
            }

            if (layout.Generation > _highestSubmittedGeneration)
            {
                _highestSubmittedGeneration = layout.Generation;
            }

            _pending = layout;
            if (_idleTcs.Task.IsCompleted)
            {
                _idleTcs = NewIdleTcs();
            }

            if (!_workerActive)
            {
                _workerActive = true;
                shouldStartWorker = true;
            }
        }

        if (shouldStartWorker)
        {
            _ = ProcessLoopAsync();
        }
    }

    public Task WhenIdleAsync()
    {
        lock (_gate)
        {
            return _idleTcs.Task;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending = null;
            if (!_workerActive)
            {
                _idleTcs.TrySetResult();
            }
        }
    }

    private async Task ProcessLoopAsync()
    {
        // Yield once so Submit never runs apply work inline on the caller's stack.
        await Task.Yield();

        Exception? firstFailure = null;

        while (true)
        {
            TerminalLayout? next;
            TaskCompletionSource idle;

            lock (_gate)
            {
                if (_disposed)
                {
                    _pending = null;
                    _workerActive = false;
                    _idleTcs.TrySetResult();
                    return;
                }

                if (_pending is null)
                {
                    _workerActive = false;
                    idle = _idleTcs;
                    if (firstFailure is null)
                    {
                        idle.TrySetResult();
                    }
                    else
                    {
                        idle.TrySetException(firstFailure);
                    }

                    return;
                }

                next = _pending;
                _pending = null;
            }

            try
            {
                var accepted = await _applyLayout(next);
                firstFailure = null;
                if (accepted)
                {
                    PublishAccepted(next);
                }
            }
            catch (Exception ex)
            {
                firstFailure = ex;
            }
        }
    }

    private void PublishAccepted(TerminalLayout layout)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            LayoutAccepted?.Invoke(this, layout);
        }
    }

    private static TaskCompletionSource NewIdleTcs() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource CompletedIdle()
    {
        var tcs = NewIdleTcs();
        tcs.TrySetResult();
        return tcs;
    }
}