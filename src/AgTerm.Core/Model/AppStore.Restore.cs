namespace AgTerm.Core.Model;

// Per-session restore-command override (port of AppStore+Restore.swift)

public sealed partial class AppStore
{
    /// <summary>
    /// Sets a pane's PERSISTED restore-command override, the single mutation point for the control
    /// channel's session.restore. Tri-state value: null = no override (auto-capture), "" = pinned
    /// to nothing (a plain shell), "cmd" = run that shell line on the next launch. Persists
    /// immediately — the override must survive a hard kill. Idempotent like SetFlag: an unchanged
    /// value writes nothing and skips the save. No-op for an unknown id.
    ///
    /// It deliberately does NOT touch the pending slots: a write during this run must not execute
    /// during this run. Scratch is rejected at the command layer, so it returns false here.
    ///
    /// Returns whether the requested value is now on disk, so the caller can refuse to acknowledge
    /// a write that never landed. A failed save is ROLLED BACK in memory, both so the value keeps
    /// matching disk and so the unchanged-value guard can't swallow the retry.
    /// </summary>
    public bool SetRestoreCommand(string? value, StatusPane pane, Guid forSession)
    {
        if (SessionWithId(forSession) is not { } session) return false;
        string? previous;
        switch (pane)
        {
            case StatusPane.Left:
                previous = session.RestoreCommand;
                if (previous == value) return true;
                session.RestoreCommand = value;
                if (!SaveChecked())
                {
                    session.RestoreCommand = previous;
                    return false;
                }
                return true;
            case StatusPane.Right:
                previous = session.SplitRestoreCommand;
                if (previous == value) return true;
                session.SplitRestoreCommand = value;
                if (!SaveChecked())
                {
                    session.SplitRestoreCommand = previous;
                    return false;
                }
                return true;
            default:
                return false;
        }
    }
}
