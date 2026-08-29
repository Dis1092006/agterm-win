using AgTerm.Core.Model;

namespace AgTerm.App;

public sealed class SettingsReloadCoordinator : IDisposable
{
    private readonly Func<CancellationToken, Task<AppSettings>> load;
    private readonly Action<Action> runOnUi;
    private readonly Func<AppSettings, Task> apply;
    private readonly Action<Exception>? error;
    private readonly CancellationTokenSource disposeCts = new();
    private readonly SemaphoreSlim applyGate = new(1, 1);
    private long latestGeneration;
    private int disposed;

    public SettingsReloadCoordinator(
        Func<CancellationToken, Task<AppSettings>> load,
        Action<Action> runOnUi,
        Func<AppSettings, Task> apply,
        Action<Exception>? error = null)
    {
        this.load = load ?? throw new ArgumentNullException(nameof(load));
        this.runOnUi = runOnUi ?? throw new ArgumentNullException(nameof(runOnUi));
        this.apply = apply ?? throw new ArgumentNullException(nameof(apply));
        this.error = error;
    }

    public void Request()
    {
        if (IsDisposed)
        {
            return;
        }

        var generation = Interlocked.Increment(ref latestGeneration);
        _ = ProcessRequestAsync(generation);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        disposeCts.Cancel();
        disposeCts.Dispose();
        // Do not dispose applyGate while in-flight requests may still release it. SemaphoreSlim
        // allocates no wait handle on this path and becomes collectible with the coordinator.
    }

    private bool IsDisposed => Volatile.Read(ref disposed) != 0;

    private bool IsStale(long generation)
    {
        return generation != Volatile.Read(ref latestGeneration);
    }

    private bool IsDisposedOrStale(long generation)
    {
        return IsDisposed || IsStale(generation);
    }

    private async Task ProcessRequestAsync(long generation)
    {
        try
        {
            var settings = await load(disposeCts.Token).ConfigureAwait(false);
            if (IsDisposedOrStale(generation))
            {
                return;
            }

            try
            {
                await applyGate.WaitAsync(disposeCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (disposeCts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (IsDisposedOrStale(generation))
                {
                    return;
                }

                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    runOnUi(() => StartApplyOnUi(settings, completion));
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }

                try
                {
                    await completion.Task.WaitAsync(disposeCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (disposeCts.IsCancellationRequested)
                {
                    return;
                }
            }
            finally
            {
                applyGate.Release();
            }
        }
        catch (OperationCanceledException) when (disposeCts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    private void StartApplyOnUi(AppSettings settings, TaskCompletionSource completion)
    {
        Task applyTask;
        try
        {
            applyTask = apply(settings);
        }
        catch (OperationCanceledException) when (disposeCts.IsCancellationRequested)
        {
            completion.TrySetResult();
            return;
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            return;
        }

        if (applyTask.IsCompleted)
        {
            CompleteFromApplyTask(applyTask, completion);
            return;
        }

        _ = AwaitApplyAndSettleAsync(applyTask, completion);
    }

    private static void CompleteFromApplyTask(Task applyTask, TaskCompletionSource completion)
    {
        if (applyTask.IsCanceled)
        {
            completion.TrySetCanceled();
            return;
        }

        if (applyTask.IsFaulted)
        {
            completion.TrySetException(applyTask.Exception!.InnerExceptions);
            return;
        }

        completion.TrySetResult();
    }

    private static async Task AwaitApplyAndSettleAsync(Task applyTask, TaskCompletionSource completion)
    {
        try
        {
            await applyTask.ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private void Report(Exception ex)
    {
        if (error is null)
        {
            return;
        }

        try
        {
            error(ex);
        }
        catch
        {
            // Ignore failures from the observer callback.
        }
    }
}