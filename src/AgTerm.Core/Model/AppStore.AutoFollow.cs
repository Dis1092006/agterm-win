namespace AgTerm.Core.Model;

// Auto-follow attention (port of AppStore+AutoFollow.swift)
//
// After the user has been idle from input for AutoFollowTimeout, the window jumps its selection to
// the oldest blocked session, pulling the user to whatever agent is waiting for input.

public sealed partial class AppStore
{
    /// <summary>Raised right after an idle auto-follow moves this window's selection to a blocked
    /// session, carrying the target session id — the analogue of the upstream .agtermAutoFollowed
    /// notification. The host resolves the owning window and moves keyboard focus into the target.</summary>
    public event Action<Guid>? AutoFollowed;

    private Action<Action> _autoFollowDispatcher = static work => work();
    private long _autoFollowGeneration;

    /// <summary>The host serializes deferred work with every other store mutation.</summary>
    public void SetAutoFollowDispatcher(Action<Action> dispatch) =>
        _autoFollowDispatcher = dispatch ?? throw new ArgumentNullException(nameof(dispatch));

    /// <summary>Records user interaction with this window (a keystroke or a manual selection).
    /// Stamps LastActivityAt UNCONDITIONALLY so the idle metric is independent of the feature being
    /// enabled, then — only when a timeout is configured — arms the debouncer to fire AutoFollowFire
    /// after the idle window; when disabled it cancels any pending fire. Auto-follow's OWN
    /// SelectSession must NOT call this (only user entry points do), else the jump would keep
    /// resetting its own idle timer.</summary>
    public void NoteUserActivity()
    {
        LastActivityAt = DateTimeOffset.UtcNow;
        if (AutoFollowTimeout is not { } timeout)
        {
            CancelAutoFollow();
            return;
        }
        ArmAutoFollow(timeout);
    }

    /// <summary>Suppresses auto-follow for this window while a non-terminal editor or transient
    /// overlay owns keyboard focus. Balanced by ResumeAutoFollow. Bracketing (rather than a bare
    /// bool) lets the several suppressors overlap.</summary>
    public void SuppressAutoFollow() => AutoFollowSuppressionCount += 1;

    public bool IsAutoFollowSuppressed => AutoFollowSuppressionCount != 0;

    /// <summary>Lifts one auto-follow suppression. Clamped at zero so an unbalanced extra resume
    /// can't drive the count negative and wedge the gate. Does NOT re-arm the debouncer — the next
    /// keystroke or attention change re-arms normally.</summary>
    public void ResumeAutoFollow() => AutoFollowSuppressionCount = Math.Max(0, AutoFollowSuppressionCount - 1);

    /// <summary>The single debouncer-arming seam the activity stamp, the enable/grace-change path,
    /// and the status-change re-arm all share: schedule AutoFollowFire after delay seconds.</summary>
    private void ArmAutoFollow(double after)
    {
        var generation = ++_autoFollowGeneration;
        AutoFollowDebouncer.Schedule(after, () => _autoFollowDispatcher(() =>
        {
            if (generation == _autoFollowGeneration && AutoFollowTimeout is not null)
                AutoFollowFire();
        }));
    }

    private void CancelAutoFollow()
    {
        _autoFollowGeneration++;
        AutoFollowDebouncer.Cancel();
    }

    /// <summary>
    /// Enables or disables auto-follow for this window and reconciles the debouncer. A null timeout
    /// DISABLES: it drops any pending fire. A non-null timeout ENABLES: on the off→on transition it
    /// arms the debouncer from the CURRENT state, so a user already idle with a block waiting is
    /// pulled after the grace window; a timeout OR stayOnActive CHANGE while already enabled re-arms
    /// the debouncer so the next fire re-evaluates from the new config. Attention-set changes while
    /// enabled re-arm via SetAgentIndicator (the observation-tracking analogue).
    /// </summary>
    public void SetAutoFollow(double? timeout, bool stayOnActive)
    {
        var previousTimeout = AutoFollowTimeout;
        var previousStayOnActive = AutoFollowStayOnActive;
        AutoFollowStayOnActive = stayOnActive;
        AutoFollowTimeout = timeout;
        if (timeout is not { } enabled)
        {
            CancelAutoFollow();
            return;
        }
        // (re-)arm on the off->on enable, a changed grace, OR a changed stayOnActive: each can
        // change the outcome of the next fire for a block already waiting while the user is idle.
        if (previousTimeout != timeout || previousStayOnActive != stayOnActive) ArmAutoFollow(enabled);
    }

    /// <summary>Re-arms the auto-follow debouncer whenever the window's attention set changes while
    /// enabled, so a block that LANDS (or an active session that finishes) while the user is ALREADY
    /// idle gets the same idle grace a keystroke would buy. The Swift original registers a live
    /// observation tracker over AttentionSessions; here the single mutation point
    /// (SetAgentIndicator) calls this directly — a no-op while disabled.</summary>
    private void RearmAutoFollowOnAttentionChange()
    {
        if (AutoFollowTimeout is { } timeout) ArmAutoFollow(timeout);
    }

    /// <summary>The pure auto-follow decision (host-free, unit-tested): which session the window
    /// should jump to, or null to stay put. Suppresses when the current session is already blocked
    /// (you're on it) or — when stayOnActive is on — active (don't leave a running agent); skips any
    /// blocked session already AutoFollowConsumed; otherwise returns the oldest remaining blocked
    /// session by StatusChangedAt ascending (FIFO), or null when none remain. A missing stamp sorts
    /// last, so a stamped session is always preferred.</summary>
    internal Guid? AutoFollowTarget(Session? current, IReadOnlyList<Session> blocked, bool stayOnActive)
    {
        if (current?.AgentIndicator.Status == AgentStatus.Blocked) return null;
        if (stayOnActive && current?.AgentIndicator.Status == AgentStatus.Active) return null;
        return blocked.Where(s => !s.AutoFollowConsumed)
            .OrderBy(s => s.StatusChangedAt ?? DateTimeOffset.MaxValue)
            .FirstOrDefault()?.Id;
    }

    /// <summary>Fires one auto-follow step: computes the window-wide blocked set, consults
    /// AutoFollowTarget, and selects the target when there is one. A no-op (no target) does nothing
    /// and does NOT reschedule — being parked on the blocked session is itself the suppressor until
    /// a keystroke clears it and re-arms the timer. Selection here deliberately does NOT note
    /// activity. Marks the target AutoFollowConsumed so a later idle fire won't pull back to it
    /// until it re-enters blocked. Internal so tests can drive it directly.</summary>
    internal void AutoFollowFire()
    {
        // a non-terminal editor/overlay owns keyboard focus: no-op so the jump can't interrupt a
        // rename or reshuffle a palette's target. No reschedule — the next keystroke or attention
        // change re-arms once the editor closes and ResumeAutoFollow lifts this.
        if (AutoFollowSuppressionCount != 0) return;
        var blocked = AttentionSessions.Where(s => s.AgentIndicator.Status == AgentStatus.Blocked).ToList();
        if (AutoFollowTarget(ActiveSession, blocked, AutoFollowStayOnActive) is not { } target) return;
        SelectSession(target);
        // mute this block: a later idle fire won't yank the user back here after they leave, until
        // the session re-enters blocked (SetAgentIndicator resets the flag on that transition).
        if (SessionWithId(target) is { } session) session.AutoFollowConsumed = true;
        AutoFollowed?.Invoke(target);
    }

    /// <summary>Milliseconds since the last user activity (LastActivityAt), or null before any
    /// activity. Clamped to >= 0 so a clock skew can't yield a negative idle. asOf is injectable
    /// for deterministic tests.</summary>
    internal int? IdleMs(DateTimeOffset? asOf = null)
    {
        if (LastActivityAt is not { } last) return null;
        var now = asOf ?? DateTimeOffset.UtcNow;
        return Math.Max(0, (int)((now - last).TotalMilliseconds));
    }

    /// <summary>The configured auto-follow timeout in milliseconds, or null when disabled. The
    /// control projection of AutoFollowTimeout shared by the tree and window.list.</summary>
    internal int? AutoFollowMs => AutoFollowTimeout is { } timeout ? (int)(timeout * 1000) : null;
}
