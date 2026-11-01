namespace AgTerm.Xterm.Tests;

/// <summary>Thread-pool numbers for timeout messages: a starved pool delays everything that the
/// live tests wait for, and the numbers tell that case apart from a hang.</summary>
internal static class ThreadPoolSnapshot
{
    public static string Describe()
    {
        ThreadPool.GetMinThreads(out var minWorkers, out _);
        return $"thread pool: threads={ThreadPool.ThreadCount}, min={minWorkers}, " +
            $"pending={ThreadPool.PendingWorkItemCount}, completed={ThreadPool.CompletedWorkItemCount}";
    }
}
