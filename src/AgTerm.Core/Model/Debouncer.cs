namespace AgTerm.Core.Model;

/// <summary>
/// Coalesces rapid repeated calls into a single deferred action. Schedule cancels any pending work
/// and reschedules, so only the latest action runs once the quiet window elapses. Flush runs the
/// pending work synchronously (used by a commit/quit path that must capture the latest state now);
/// Cancel drops it.
///
/// The upstream Debouncer runs its deferred action on the main actor; here the timer fires on a
/// thread-pool thread and invokes the action directly — a host that needs the store confined to one
/// context must marshal in the scheduled action (see PLATFORM-NOTES).
/// </summary>
public sealed class Debouncer
{
    private readonly object _gate = new();
    private Timer? _timer;
    private Action? _action;

    /// <summary>Cancels any pending action and schedules the action to run after delay seconds. Only
    /// the most recently scheduled action survives, so a burst of calls collapses to one run.</summary>
    public void Schedule(double afterSeconds, Action action)
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _action = action;
            _timer = new Timer(_ => Fire(), null,
                TimeSpan.FromSeconds(Math.Max(0, afterSeconds)), Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Runs the pending action immediately (if any) and clears it. A no-op when nothing is
    /// pending. The deferred timer is cancelled so the action can't run twice.</summary>
    public void Flush() => Fire();

    /// <summary>Drops the pending action without running it.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _action = null;
        }
    }

    /// <summary>Runs and clears the pending action. Clearing first makes a re-entrant call a no-op.</summary>
    private void Fire()
    {
        Action? action;
        lock (_gate)
        {
            action = _action;
            _action = null;
            _timer?.Dispose();
            _timer = null;
        }
        action?.Invoke();
    }
}
