namespace AgTerm.Core.Model;

// Undoable (soft) close (port of AppStore+PendingClose.swift)

/// <summary>The kind of close currently available to undo.</summary>
public enum PendingCloseKind { Session, Sessions, Workspace }

/// <summary>Observable domain summary for the latest undoable close. The host decides how to
/// present it.</summary>
public sealed record PendingCloseSummary(Guid Id, PendingCloseKind Kind, string Title);

internal abstract record PendingCloseRecord
{
    private PendingCloseRecord()
    {
    }

    public sealed record Sessions(PendingSessionsClose Close) : PendingCloseRecord;

    public sealed record Workspace(PendingWorkspaceClose Close) : PendingCloseRecord;
}

internal sealed record PendingSessionClose(
    Session Session, Guid WorkspaceId, string WorkspaceName,
    int WorkspaceIndex, int SessionIndex, Guid RecentId);

internal sealed record PendingSessionsClose(
    IReadOnlyList<PendingSessionClose> Sessions, Guid? SelectedSessionId);

internal sealed record PendingWorkspaceClose(
    Workspace Workspace, int WorkspaceIndex, Guid? SelectedSessionId);

public sealed partial class AppStore
{
    public const double PendingCloseGraceInterval = 3;

    /// <summary>Hide a session from the visible tree but keep its surfaces alive for a short undo
    /// window. If the grace expires, FinalizePendingClose performs the same teardown as CloseSession.</summary>
    public bool SoftCloseSession(Guid sessionId, double grace = PendingCloseGraceInterval)
    {
        if (LocationOfSession(sessionId) is not { } location) return false;
        var workspace = Workspaces[location.WorkspaceIndex];
        var wasActive = SelectedSessionId == sessionId;
        var session = workspace.Sessions[location.SessionIndex];
        // a subscriber told the session closed must not still see its question pending
        CancelPendingAsk(session);
        CloseTimedHud(session);
        workspace.Sessions.RemoveAt(location.SessionIndex);
        EmitSessionClosed(session, workspace.Id);
        // undo reinserts THIS object, so an override payload armed at bootstrap and never consumed
        // would survive the round trip and fire when the restored session's surface is built. Drop
        // it here; the persisted pin is untouched and still fires on the next launch.
        session.ClearPendingRestoreOverrides();
        var closeId = Guid.NewGuid();
        var close = new PendingSessionClose(session, workspace.Id, workspace.Name,
            location.WorkspaceIndex, location.SessionIndex, closeId);
        PendingCloseRecords[closeId] = new PendingCloseRecord.Sessions(
            new PendingSessionsClose(new[] { close }, session.Id));
        PendingCloseOrder.Add(closeId);
        RecordRecentClosedSession(session, workspace.Id, workspace.Name,
            location.WorkspaceIndex, location.SessionIndex, closeId);
        if (wasActive)
        {
            SelectedSessionId = CloseReselectionTarget(location);
            ReplaceSidebarSelection(SelectedSessionId);
            AutoUnfocusIfOutsideFocus(SelectedSessionId);
            RecordRecency();
        }
        else
        {
            PruneSidebarSelection();
        }
        ShowPendingCloseSummary(closeId);
        SchedulePendingCloseFinalization(closeId, grace);
        CancelPendingSave();
        return true;
    }

    /// <summary>Hide multiple sessions as one undoable operation. The removed sessions keep their
    /// surfaces alive until the one shared grace timer finalizes, and a single undo restores every
    /// session in the group.</summary>
    public bool SoftCloseSessions(IReadOnlyList<Guid> sessionIds, double grace = PendingCloseGraceInterval)
    {
        var targetIds = new HashSet<Guid>(sessionIds);
        if (targetIds.Count <= 1)
        {
            if (sessionIds.Count == 0) return false;
            return SoftCloseSession(sessionIds[0], grace);
        }

        var closes = new List<PendingSessionClose>();
        for (var workspaceIndex = 0; workspaceIndex < Workspaces.Count; workspaceIndex++)
        {
            var workspace = Workspaces[workspaceIndex];
            for (var sessionIndex = 0; sessionIndex < workspace.Sessions.Count; sessionIndex++)
            {
                var session = workspace.Sessions[sessionIndex];
                if (!targetIds.Contains(session.Id)) continue;
                closes.Add(new PendingSessionClose(session, workspace.Id, workspace.Name,
                    workspaceIndex, sessionIndex, Guid.NewGuid()));
            }
        }
        if (closes.Count == 0) return false;
        foreach (var close in closes)
        {
            close.Session.ClearPendingRestoreOverrides(); // same undo hazard as the singular close
            CancelPendingAsk(close.Session);
            CloseTimedHud(close.Session);
        }

        var previousSelection = SelectedSessionId;
        var removingActive = previousSelection is { } prev && targetIds.Contains(prev);
        foreach (var close in closes
                     .OrderByDescending(c => c.WorkspaceIndex)
                     .ThenByDescending(c => c.SessionIndex))
        {
            if (close.WorkspaceIndex >= Workspaces.Count) continue;
            var sessions = Workspaces[close.WorkspaceIndex].Sessions;
            if (close.SessionIndex >= sessions.Count
                || sessions[close.SessionIndex].Id != close.Session.Id) continue;
            sessions.RemoveAt(close.SessionIndex);
        }
        foreach (var close in closes)
        {
            RecordRecentClosedSession(close.Session, close.WorkspaceId, close.WorkspaceName,
                close.WorkspaceIndex, close.SessionIndex, close.RecentId);
            EmitSessionClosed(close.Session, close.WorkspaceId);
        }
        if (removingActive)
        {
            var activeClose = previousSelection is { } activeId
                ? closes.FirstOrDefault(c => c.Session.Id == activeId) : null;
            if (activeClose is not null)
            {
                var removedBeforeActive = closes.Count(c =>
                    c.WorkspaceIndex == activeClose.WorkspaceIndex && c.SessionIndex < activeClose.SessionIndex);
                SelectedSessionId = CloseReselectionTarget(
                    (activeClose.WorkspaceIndex, activeClose.SessionIndex - removedBeforeActive));
            }
            else
            {
                SelectedSessionId = Workspaces.FirstOrDefault(w => w.Sessions.Count > 0)?.Sessions[0].Id;
            }
            ReplaceSidebarSelection(SelectedSessionId);
            AutoUnfocusIfOutsideFocus(SelectedSessionId);
            RecordRecency();
        }
        else
        {
            PruneSidebarSelection();
        }

        var closeId = Guid.NewGuid();
        PendingCloseRecords[closeId] = new PendingCloseRecord.Sessions(new PendingSessionsClose(
            closes, removingActive ? previousSelection : closes[0].Session.Id));
        PendingCloseOrder.Add(closeId);
        ShowPendingCloseSummary(closeId);
        SchedulePendingCloseFinalization(closeId, grace);
        CancelPendingSave();
        return true;
    }

    /// <summary>Hide a workspace from the visible tree but keep all of its sessions alive for a
    /// short undo window. The last workspace cannot be soft-closed, matching RemoveWorkspace.</summary>
    public bool SoftRemoveWorkspace(Guid workspaceId, double grace = PendingCloseGraceInterval)
    {
        if (!CanRemoveWorkspace) return false;
        var index = Workspaces.FindIndex(w => w.Id == workspaceId);
        if (index < 0) return false;
        var visibleWorkspace = Workspaces[index];
        foreach (var session in visibleWorkspace.Sessions)
        {
            CancelPendingAsk(session);
            CloseTimedHud(session);
        }
        Workspaces.RemoveAt(index);
        ForgetFreshWorkspace(workspaceId);
        foreach (var session in visibleWorkspace.Sessions) EmitSessionClosed(session, visibleWorkspace.Id);
        if (visibleWorkspace.Sessions.Count == 0) ScheduleTreeChanged();
        var workspace = FoldingPendingCloses(visibleWorkspace);
        foreach (var session in workspace.Sessions)
        {
            session.ClearPendingRestoreOverrides(); // same undo hazard
        }
        var removingActive = SelectedSessionId is { } active && workspace.Sessions.Any(s => s.Id == active);
        var restoringSelection = removingActive ? SelectedSessionId : null;
        if (FocusedWorkspaceId == workspaceId) FocusedWorkspaceId = null;
        if (removingActive)
        {
            var fallbackIndex = Math.Min(index, Workspaces.Count - 1);
            SelectedSessionId = Workspaces[fallbackIndex].Sessions.FirstOrDefault()?.Id
                ?? Workspaces.FirstOrDefault(w => w.Sessions.Count > 0)?.Sessions[0].Id;
            ReplaceSidebarSelection(SelectedSessionId);
            AutoUnfocusIfOutsideFocus(SelectedSessionId);
            RecordRecency();
        }
        else
        {
            PruneSidebarSelection();
        }

        var closeId = Guid.NewGuid();
        PendingCloseRecords[closeId] = new PendingCloseRecord.Workspace(
            new PendingWorkspaceClose(workspace, index, restoringSelection));
        PendingCloseOrder.Add(closeId);
        RecordRecentClosedWorkspace(workspace, restoringSelection, closeId);
        ShowPendingCloseSummary(closeId);
        SchedulePendingCloseFinalization(closeId, grace);
        CancelPendingSave();
        return true;
    }

    /// <summary>Undo the latest pending close, or a specific pending-close record when id is supplied.</summary>
    public bool UndoPendingClose(Guid? id = null, Guid? selecting = null)
    {
        var closeId = id ?? PendingCloseSummary?.Id;
        if (closeId is not { } key || !PendingCloseRecords.Remove(key, out var record)) return false;
        if (PendingCloseTasks.Remove(key, out var task)) task.Cancel();
        PendingCloseOrder.RemoveAll(x => x == key);
        switch (record)
        {
            case PendingCloseRecord.Sessions sessions:
                RestorePendingSessions(sessions.Close, selecting);
                foreach (var session in sessions.Close.Sessions) RemoveRecentClosedItem(session.RecentId);
                break;
            case PendingCloseRecord.Workspace workspace:
                RestorePendingWorkspace(workspace.Close);
                RemoveRecentClosedItem(key);
                break;
        }
        if (PendingCloseSummary?.Id == key) PromotePendingCloseSummary();
        Save();
        return true;
    }

    internal void FinalizePendingClose(Guid id) =>
        FinalizePendingClose(id, teardownSurfaces: true, save: true);

    private void FinalizePendingClose(Guid id, bool teardownSurfaces, bool save)
    {
        if (!PendingCloseRecords.Remove(id, out var record)) return;
        if (PendingCloseTasks.Remove(id, out var task)) task.Cancel();
        PendingCloseOrder.RemoveAll(x => x == id);
        switch (record)
        {
            case PendingCloseRecord.Sessions sessions:
                foreach (var close in sessions.Close.Sessions)
                    HardFinalizePendingSession(close.Session, teardownSurfaces);
                break;
            case PendingCloseRecord.Workspace workspace:
                HardFinalizePendingWorkspace(workspace.Close.Workspace, teardownSurfaces);
                break;
        }
        if (PendingCloseSummary?.Id == id) PromotePendingCloseSummary();
        if (save) Save();
    }

    public void FinalizeAllPendingCloses()
    {
        foreach (var id in PendingCloseRecords.Keys.ToArray())
        {
            FinalizePendingClose(id);
        }
    }

    public IReadOnlyList<ITerminalSurface> PreparePendingClosesForShutdown()
    {
        var surfaces = new HashSet<ITerminalSurface>(ReferenceEqualityComparer.Instance);
        foreach (var record in PendingCloseRecords.Values)
        {
            var sessions = record switch
            {
                PendingCloseRecord.Sessions pending =>
                    pending.Close.Sessions.Select(close => close.Session),
                PendingCloseRecord.Workspace pending => pending.Close.Workspace.Sessions,
                _ => Array.Empty<Session>(),
            };
            foreach (var session in sessions)
            {
                Add(session.Surface);
                Add(session.SplitSurface);
                Add(session.ScratchSurface);
                Add(session.OverlaySurface);
            }
        }

        foreach (var id in PendingCloseRecords.Keys.ToArray())
            FinalizePendingClose(id, teardownSurfaces: false, save: false);
        return surfaces.ToArray();

        void Add(ITerminalSurface? surface)
        {
            if (surface is not null) surfaces.Add(surface);
        }
    }

    private void ShowPendingCloseSummary(Guid id)
    {
        if (!PendingCloseRecords.TryGetValue(id, out var record)) return;
        PendingCloseSummary = Summary(id, record);
        NotifySidebar();
    }

    private void PromotePendingCloseSummary()
    {
        if (PendingCloseOrder.Count == 0
            || !PendingCloseRecords.TryGetValue(PendingCloseOrder[^1], out var record))
        {
            PendingCloseSummary = null;
            NotifySidebar();
            return;
        }
        PendingCloseSummary = Summary(PendingCloseOrder[^1], record);
        NotifySidebar();
    }

    private static PendingCloseSummary Summary(Guid id, PendingCloseRecord record)
    {
        switch (record)
        {
            case PendingCloseRecord.Sessions sessions:
                if (sessions.Close.Sessions.Count == 1)
                {
                    return new PendingCloseSummary(id, PendingCloseKind.Session,
                        sessions.Close.Sessions[0].Session.DisplayName);
                }
                return new PendingCloseSummary(id, PendingCloseKind.Sessions,
                    $"{sessions.Close.Sessions.Count} sessions");
            case PendingCloseRecord.Workspace workspace:
                return new PendingCloseSummary(id, PendingCloseKind.Workspace, workspace.Close.Workspace.Name);
            default:
                throw new ArgumentOutOfRangeException(nameof(record));
        }
    }

    private void SchedulePendingCloseFinalization(Guid id, double grace)
    {
        if (PendingCloseTasks.TryGetValue(id, out var existing)) existing.Cancel();
        var cts = new CancellationTokenSource();
        PendingCloseTasks[id] = cts;
        var delay = TimeSpan.FromSeconds(Math.Max(0, grace));
        _ = Task.Delay(delay, cts.Token).ContinueWith(
            t =>
            {
                if (t.IsCanceled) return;
                var finalize = () => FinalizePendingClose(id);
                if (PendingCloseFinalizationScheduler is { } schedule)
                    schedule(finalize);
                else
                    finalize();
            },
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>Absorb any pending close of the same workspace into the copy being closed now,
    /// dropping the superseded record. Undoing a session close rebuilds a missing workspace as a
    /// shell, so closing that shell while the earlier record still waits out its grace would leave
    /// two pending records sharing a workspace id. The copy closed now is the newer state, so its
    /// name, expansion and session order lead; the superseded record's sessions follow.</summary>
    private Workspace FoldingPendingCloses(Workspace workspace)
    {
        var folded = workspace;
        foreach (var closeId in PendingCloseOrder.ToArray())
        {
            if (!PendingCloseRecords.TryGetValue(closeId, out var record)
                || record is not PendingCloseRecord.Workspace pending
                || pending.Close.Workspace.Id != workspace.Id) continue;
            PendingCloseRecords.Remove(closeId);
            if (PendingCloseTasks.Remove(closeId, out var task)) task.Cancel();
            var present = folded.Sessions.Select(s => s.Id).ToHashSet();
            folded.Sessions.AddRange(pending.Close.Workspace.Sessions.Where(s => !present.Contains(s.Id)));
        }
        PendingCloseOrder.RemoveAll(id => !PendingCloseRecords.ContainsKey(id));
        return folded;
    }

    /// <summary>Session ids a pending close still holds. They are absent from the tree, but their
    /// live objects are intact and an undo reinserts them, so a restore that rebuilt one from a
    /// snapshot would put two objects under a single id. Callers union this with the tree's ids to
    /// decide what is already taken.</summary>
    internal HashSet<Guid> PendingHeldSessionIds()
    {
        var held = new HashSet<Guid>();
        foreach (var record in PendingCloseRecords.Values)
        {
            switch (record)
            {
                case PendingCloseRecord.Sessions sessions:
                    held.UnionWith(sessions.Close.Sessions.Select(c => c.Session.Id));
                    break;
                case PendingCloseRecord.Workspace workspace:
                    held.UnionWith(workspace.Close.Workspace.Sessions.Select(s => s.Id));
                    break;
            }
        }
        return held;
    }

    /// <summary>A workspace to stand in for one a restore needs but the tree no longer holds. Prefer
    /// the newest description of it: a pending close of that same workspace carries its live name,
    /// expansion state and theme, and once those finalize an Open Recent snapshot still does. name
    /// is the caller's older copy, used only when neither describes the workspace.</summary>
    internal Workspace RebuiltWorkspaceShell(Guid id, string name)
    {
        for (var i = PendingCloseOrder.Count - 1; i >= 0; i--)
        {
            if (!PendingCloseRecords.TryGetValue(PendingCloseOrder[i], out var record)
                || record is not PendingCloseRecord.Workspace pending
                || pending.Close.Workspace.Id != id) continue;
            return new Workspace(id, pending.Close.Workspace.Name,
                isExpanded: pending.Close.Workspace.IsExpanded)
            {
                ThemeName = pending.Close.Workspace.ThemeName,
                IconName = pending.Close.Workspace.IconName,
            };
        }
        var snapshot = RecentClosedStore?.Load()
            .Select(item => item.Workspace)
            .FirstOrDefault(w => w?.Snapshot.Id == id)?.Snapshot;
        if (snapshot is not null)
        {
            return new Workspace(id, snapshot.Name, isExpanded: !(snapshot.Collapsed ?? false))
            {
                ThemeName = snapshot.Theme,
                IconName = snapshot.Icon,
            };
        }
        return new Workspace(id, name);
    }

    private void RestorePendingSession(PendingSessionClose close)
    {
        int workspaceIndex;
        var existing = Workspaces.FindIndex(w => w.Id == close.WorkspaceId);
        if (existing >= 0)
        {
            workspaceIndex = existing;
        }
        else
        {
            var insertWorkspaceAt = Math.Max(0, Math.Min(close.WorkspaceIndex, Workspaces.Count));
            Workspaces.Insert(insertWorkspaceAt, RebuiltWorkspaceShell(close.WorkspaceId, close.WorkspaceName));
            workspaceIndex = insertWorkspaceAt;
        }
        var insertAt = Math.Max(0, Math.Min(close.SessionIndex, Workspaces[workspaceIndex].Sessions.Count));
        Workspaces[workspaceIndex].Sessions.Insert(insertAt, close.Session);
        EmitSessionCreated(close.Session, close.WorkspaceId);
    }

    private void RestorePendingSessions(PendingSessionsClose close, Guid? requestedSessionId)
    {
        foreach (var session in close.Sessions)
        {
            RestorePendingSession(session);
        }
        var target = (requestedSessionId is { } requested
                && close.Sessions.Any(c => c.Session.Id == requested) ? requested : (Guid?)null)
            ?? (close.SelectedSessionId is { } selected
                && close.Sessions.Any(c => c.Session.Id == selected) ? selected : (Guid?)null)
            ?? (close.Sessions.Count > 0 ? close.Sessions[0].Session.Id : null);
        if (target is { } resolved)
        {
            SelectSession(resolved);
        }
    }

    private void RestorePendingWorkspace(PendingWorkspaceClose close)
    {
        // an undone session close, or an Open Recent restore, rebuilds a missing workspace by id as
        // a shell holding just that session. Merge into the shell instead of inserting a second
        // workspace sharing its id: every id-keyed lookup resolves the first match, so a duplicate
        // would strand the other copy's sessions.
        var existing = Workspaces.FindIndex(w => w.Id == close.Workspace.Id);
        if (existing >= 0)
        {
            var live = Workspaces.SelectMany(w => w.Sessions).Select(s => s.Id).ToHashSet();
            var restored = close.Workspace.Sessions.Where(s => !live.Contains(s.Id)).ToList();
            Workspaces[existing].Sessions.AddRange(restored);
            foreach (var session in restored) EmitSessionCreated(session, Workspaces[existing].Id);
        }
        else
        {
            var insertAt = Math.Max(0, Math.Min(close.WorkspaceIndex, Workspaces.Count));
            Workspaces.Insert(insertAt, close.Workspace);
            foreach (var session in close.Workspace.Sessions) EmitSessionCreated(session, close.Workspace.Id);
            if (close.Workspace.Sessions.Count == 0) ScheduleTreeChanged();
        }
        var target = close.SelectedSessionId ?? close.Workspace.Sessions.FirstOrDefault()?.Id;
        if (target is not { } resolved) return;
        SelectSession(resolved);
    }

    private void HardFinalizePendingSession(Session session, bool teardownSurfaces = true)
    {
        // an untimed HUD rode out the undo window on this object; once the close is final nothing
        // can reach it any more
        CloseHudOf(session);
        if (teardownSurfaces)
        {
            session.Surface?.Teardown();
            session.SplitSurface?.Teardown();
            session.OverlaySurface?.Teardown();
            session.ScratchSurface?.Teardown();
        }
        WatermarkStorage.RemoveRenderedText(session.Id, _stateDirectory);
        ScrollbackStorage.Remove(session.Id, _stateDirectory);
        RemoveFromRecency(session.Id);
    }

    private void HardFinalizePendingWorkspace(Workspace workspace, bool teardownSurfaces = true)
    {
        foreach (var session in workspace.Sessions)
        {
            HardFinalizePendingSession(session, teardownSurfaces);
        }
    }
}
