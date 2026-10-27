namespace AgTerm.Xterm.Tests;

/// <summary>A named mutex shared by every process of the current Windows session. A mutex belongs to
/// the thread that took it, and an async test ends on another thread than it started on, so a
/// dedicated thread holds it from Enter to Exit. When a process dies holding it, Windows hands it to
/// the next waiter.</summary>
internal sealed class MachineWideLock(string name) : IDisposable
{
    private Thread? _holder;
    private ManualResetEventSlim? _release;

    public bool Enter(TimeSpan budget)
    {
        var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = new Thread(() =>
        {
            using var mutex = new Mutex(false, name);
            if (!Take(mutex, budget))
            {
                entered.SetResult(false);
                return;
            }
            entered.SetResult(true);
            release.Wait();
            mutex.ReleaseMutex();
        })
        {
            IsBackground = true,
        };
        holder.Start();
        if (!entered.Task.GetAwaiter().GetResult())
            return false;
        _holder = holder;
        _release = release;
        return true;
    }

    public void Exit()
    {
        _release?.Set();
        _holder?.Join();
        _holder = null;
        _release = null;
    }

    public void Dispose() => Exit();

    private static bool Take(Mutex mutex, TimeSpan budget)
    {
        try
        {
            return mutex.WaitOne(budget);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }
}
