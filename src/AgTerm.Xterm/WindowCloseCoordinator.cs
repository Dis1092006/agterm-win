namespace AgTerm.Xterm;

public sealed class WindowCloseCoordinator
{
    private readonly Func<Task> _shutdown;
    private readonly Action<Action> _runOnUi;
    private readonly Action _requestFinalClose;
    private readonly Action<Exception>? _log;
    private readonly object _gate = new();
    private readonly TaskCompletionSource<object?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _shutdownStarted;
    private bool _finalReentryEnabled;
    private bool _finalCloseActionExecuted;

    public WindowCloseCoordinator(
        Func<Task> shutdown,
        Action<Action> runOnUi,
        Action requestFinalClose,
        Action<Exception>? log = null)
    {
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
        _runOnUi = runOnUi ?? throw new ArgumentNullException(nameof(runOnUi));
        _requestFinalClose = requestFinalClose ?? throw new ArgumentNullException(nameof(requestFinalClose));
        _log = log;
    }

    public Task Completion => _completion.Task;

    public bool ShouldCancelClose()
    {
        bool shouldStartShutdown = false;

        lock (_gate)
        {
            if (_finalReentryEnabled)
            {
                return false;
            }

            if (!_shutdownStarted)
            {
                _shutdownStarted = true;
                shouldStartShutdown = true;
            }
        }

        if (shouldStartShutdown)
        {
            StartShutdownFlow();
        }

        return true;
    }

    private void StartShutdownFlow()
    {
        Task shutdownTask;
        try
        {
            shutdownTask = _shutdown();
            if (shutdownTask is null)
            {
                throw new InvalidOperationException("shutdown delegate returned null task");
            }
        }
        catch (Exception ex)
        {
            ObserveShutdownException(ex);
            shutdownTask = Task.FromException(ex);
        }

        _ = FinishAfterShutdownAsync(shutdownTask);
    }

    private async Task FinishAfterShutdownAsync(Task shutdownTask)
    {
        try
        {
            await shutdownTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ObserveShutdownException(ex);
        }

        var finalCloseAction = CreateFinalCloseAction();

        try
        {
            _runOnUi(finalCloseAction);
            _completion.TrySetResult(null);
        }
        catch (Exception ex)
        {
            ObserveRunOnUiException(ex);
            _completion.TrySetException(ex);
        }
    }

    private Action CreateFinalCloseAction()
    {
        return () =>
        {
            lock (_gate)
            {
                if (_finalCloseActionExecuted)
                {
                    return;
                }

                _finalCloseActionExecuted = true;
                _finalReentryEnabled = true;
            }

            _requestFinalClose();
        };
    }

    private void ObserveShutdownException(Exception ex)
    {
        _log?.Invoke(ex);
    }

    private void ObserveRunOnUiException(Exception ex)
    {
        _log?.Invoke(ex);
    }
}
