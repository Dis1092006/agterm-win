using System.Diagnostics;

namespace AgTerm.Terminal.Tests;

internal static class ShellCompletionWait
{
    internal static Task<int> WaitAsync(
        Task<int> exited,
        Task outputCompleted,
        TimeSpan budget,
        Func<string> snapshot)
    {
        var elapsed = Stopwatch.StartNew();
        return Task.Factory.StartNew(() =>
        {
            Wait(exited);
            Wait(outputCompleted);
            return exited.GetAwaiter().GetResult();

            void Wait(Task task)
            {
                var remaining = budget - elapsed.Elapsed;
                // The source task can finish while its async continuations remain stuck in the pool.
                if (!task.IsCompleted && (remaining <= TimeSpan.Zero
                    || !((IAsyncResult)task).AsyncWaitHandle.WaitOne(remaining)))
                {
                    throw new TimeoutException(
                        $"Shell did not complete within {budget}. Exited={exited.Status}; "
                        + $"OutputCompleted={outputCompleted.Status}; output:\n{snapshot()}");
                }
                task.GetAwaiter().GetResult();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
}
