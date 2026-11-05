namespace AgTerm.Core.Model;

// The session info line (port of upstream AppStore.setInfo and of the expiry timer in
// ControlServer+SessionInfo.swift, 80f822bf). Upstream arms the timer in the control server; the port
// arms it here, as it does the HUD auto-hide, so the store's teardown paths cancel it directly.

public sealed partial class AppStore
{
    private readonly Dictionary<Guid, IDisposable> _infoExpiries = new();

    /// <summary>Sets or clears the session's info line. Every set on a known session re-arms the expiry,
    /// an identical one included, so a caller refreshing its line keeps it alive. True only when the line
    /// changed, which is also the only case that emits tree.changed. Never saves: the line is runtime
    /// only.</summary>
    public bool SetInfo(SessionInfo? info, Guid sessionId)
    {
        if (SessionWithId(sessionId) is not { } session) return false;
        if (info is null)
        {
            if (session.Info is null) return false;
            ClearInfoOf(session);
            ScheduleTreeChanged();
            return true;
        }
        var changed = session.Info != info;
        session.Info = info;
        session.InfoRevision++;
        ArmInfoExpiry(session);
        if (changed) ScheduleTreeChanged();
        return changed;
    }

    /// <summary>Window close and application exit: a closed window's store must not see an expiry fire.</summary>
    public void ClearAllInfos()
    {
        foreach (var session in Workspaces.SelectMany(workspace => workspace.Sessions).ToArray())
            ClearInfoOf(session);
    }

    /// <summary>Takes the object, not the id: the pending-close paths reach sessions that are no longer
    /// in the tree.</summary>
    internal void ClearInfoOf(Session session)
    {
        CancelInfoExpiry(session.Id);
        if (session.Info is null) return;
        session.Info = null;
        session.InfoRevision++;
    }

    /// <summary>A soft close keeps the session object for undo, but an expiry cannot find a session outside
    /// the tree, so a timed line would come back on undo after its time was up. An untimed line stays,
    /// which is what undo restores.</summary>
    private void CloseTimedInfo(Session session)
    {
        if (session.Info is { HideAfter: > 0.0 }) ClearInfoOf(session);
    }

    private void ArmInfoExpiry(Session session)
    {
        CancelInfoExpiry(session.Id);
        // clamped as well as validated, the same cap as a HUD's auto-hide
        var seconds = Math.Min(session.Info?.HideAfter ?? 0, HudSpec.MaxHideAfter);
        if (!(seconds > 0)) return;
        var sessionId = session.Id;
        var revision = session.InfoRevision;
        var schedule = InfoExpiryScheduler ?? ScheduleOnHostLock;
        _infoExpiries[sessionId] = schedule(TimeSpan.FromSeconds(seconds), () => ExpireInfo(sessionId, revision));
    }

    private void CancelInfoExpiry(Guid sessionId)
    {
        if (_infoExpiries.Remove(sessionId, out var timer)) timer.Dispose();
    }

    private void ExpireInfo(Guid sessionId, int revision)
    {
        // an expiry already on its way when a later set or a clear moved the revision belongs to a line
        // that is gone
        if (SessionWithId(sessionId) is not { } session || session.InfoRevision != revision) return;
        ClearInfoOf(session);
        ScheduleTreeChanged();
    }
}
