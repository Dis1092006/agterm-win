namespace AgTerm.Core.Model;

// Open Recent restore (port of AppStore+RecentClosed.swift)

/// <summary>What a restore did: Restored is false when nothing was brought back; Complete is false
/// when a session of a restored workspace is still unplaced, so its entry must stay in the list.</summary>
public readonly record struct RecentClosedRestore(bool Restored, bool Complete)
{
    public static readonly RecentClosedRestore Failed = new(false, false);
}

public sealed partial class AppStore
{
    public bool RestoreRecentClosed(RecentClosedItem item) =>
        RestoreRecentClosed(item, new HashSet<Guid>()).Restored;

    /// <summary>Every session id this store holds: live in the tree, or kept by a pending close that
    /// an undo would reinsert. A host uses it to find which window already owns an entry's session.</summary>
    public HashSet<Guid> HeldSessionIds()
    {
        var held = Workspaces.SelectMany(w => w.Sessions).Select(s => s.Id).ToHashSet();
        held.UnionWith(PendingHeldSessionIds());
        return held;
    }

    /// <summary>Restore <paramref name="item"/> into this store, treating <paramref name="occupiedElsewhere"/>
    /// as session ids another open window already holds: they are never rebuilt here, because a second
    /// object under one id makes every id-keyed lookup answer with whichever window sorts first.</summary>
    public RecentClosedRestore RestoreRecentClosed(RecentClosedItem item, IReadOnlySet<Guid> occupiedElsewhere)
    {
        switch (item.Kind)
        {
            case RecentClosedKind.Session:
            {
                if (item.Session is not { } recent) return RecentClosedRestore.Failed;
                if (RestoreOrSelectExistingRecentSession(recent)) return new(true, true);
                // the routing sends the entry to the window already holding this session, so reaching
                // here with the id occupied means that lookup missed; refuse rather than build a second
                if (occupiedElsewhere.Contains(recent.Snapshot.Id)) return RecentClosedRestore.Failed;
                int index;
                var existing = Workspaces.FindIndex(w => w.Id == recent.WorkspaceId);
                if (existing >= 0)
                {
                    index = existing;
                }
                else
                {
                    var insertWorkspaceAt = Math.Max(0, Math.Min(recent.WorkspaceIndex, Workspaces.Count));
                    Workspaces.Insert(insertWorkspaceAt,
                        RebuiltWorkspaceShell(recent.WorkspaceId, recent.WorkspaceName));
                    index = insertWorkspaceAt;
                }
                var session = SessionFrom(recent.Snapshot);
                var insertAt = Math.Max(0, Math.Min(recent.SessionIndex, Workspaces[index].Sessions.Count));
                Workspaces[index].Sessions.Insert(insertAt, session);
                EmitSessionCreated(session, Workspaces[index].Id);
                SelectSession(session.Id);
                Save();
                return new(true, true);
            }
            case RecentClosedKind.Workspace:
            {
                if (item.Workspace is not { } recent) return RecentClosedRestore.Failed;
                if (RestoreOrSelectExistingRecentWorkspace(recent, occupiedElsewhere))
                    return WorkspaceRestoreOutcome(recent, occupiedElsewhere);
                var workspace = WorkspaceFrom(recent.Snapshot);
                // a session of this snapshot may have been moved into another workspace that is
                // itself pending a close. Its original object is alive in that record, so rebuild
                // everything except it.
                var taken = Workspaces.SelectMany(w => w.Sessions).Select(s => s.Id).ToHashSet();
                taken.UnionWith(PendingHeldSessionIds());
                taken.UnionWith(occupiedElsewhere);
                workspace.Sessions.RemoveAll(s => taken.Contains(s.Id));
                // Persistent Open Recent appends like most editors' recent-project flow: reopening
                // brings the workspace back without reshuffling current workspaces.
                Workspaces.Add(workspace);
                foreach (var session in workspace.Sessions) EmitSessionCreated(session, workspace.Id);
                if (workspace.Sessions.Count == 0) ScheduleTreeChanged();
                // re-mark BEFORE the reselect, so a restored member is inside the set when the disable runs;
                // the workspace is already in the tree, so the unguarded insert cannot leave a phantom member.
                // Entries that predate the field carry null and restore unmarked.
                if (recent.FocusMember == true) MarkFocusMember(workspace.Id);
                var target = (recent.SelectedSessionId is { } selected
                        && workspace.Sessions.Any(s => s.Id == selected) ? selected : (Guid?)null)
                    ?? workspace.Sessions.FirstOrDefault()?.Id;
                if (target is { } resolved) SelectSession(resolved);
                Save();
                return WorkspaceRestoreOutcome(recent, occupiedElsewhere);
            }
            default:
                return RecentClosedRestore.Failed;
        }
    }

    /// <summary>A workspace restore is complete when every session of its snapshot is placed: live in
    /// this store, held by one of its pending closes, or held by another open window.</summary>
    private RecentClosedRestore WorkspaceRestoreOutcome(RecentClosedWorkspace recent, IReadOnlySet<Guid> occupiedElsewhere)
    {
        var placed = Workspaces.SelectMany(w => w.Sessions).Select(s => s.Id).ToHashSet();
        placed.UnionWith(PendingHeldSessionIds());
        placed.UnionWith(occupiedElsewhere);
        return new(true, recent.Snapshot.Sessions.All(s => placed.Contains(s.Id)));
    }

    private bool RestoreOrSelectExistingRecentSession(RecentClosedSession recent)
    {
        if (PendingCloseIdContainingSession(recent.Snapshot.Id) is { } pendingId)
        {
            return UndoPendingClose(pendingId, selecting: recent.Snapshot.Id);
        }
        if (SessionWithId(recent.Snapshot.Id) is null) return false;
        SelectSession(recent.Snapshot.Id);
        return true;
    }

    private bool RestoreOrSelectExistingRecentWorkspace(RecentClosedWorkspace recent, IReadOnlySet<Guid> occupiedElsewhere)
    {
        var sessionIds = recent.Snapshot.Sessions.Select(s => s.Id).ToHashSet();
        // pending closes may hold this workspace, or any number of its sessions closed one at a
        // time. Undo every match, not only the newest: a session left pending would be rebuilt from
        // the snapshot beside its live original, two objects under one id. Each undo drops its own
        // record, so the loop drains them.
        while (PendingCloseIdForWorkspace(recent.Snapshot.Id, sessionIds) is { } pendingId)
        {
            UndoPendingClose(pendingId);
        }
        var index = Workspaces.FindIndex(w => w.Id == recent.Snapshot.Id);
        if (index >= 0)
        {
            // reopening a session first rebuilds this workspace as a shell holding only that
            // session, so selecting without merging would drop the snapshot's other sessions while
            // the caller deletes the recent entry on success — their only copy. Rebuild the ones
            // that exist nowhere: not in the tree, not held by a pending close, not in another window.
            var taken = Workspaces.SelectMany(w => w.Sessions).Select(s => s.Id).ToHashSet();
            taken.UnionWith(PendingHeldSessionIds());
            taken.UnionWith(occupiedElsewhere);
            var missing = recent.Snapshot.Sessions
                .Where(s => !taken.Contains(s.Id))
                .Select(s => SessionFrom(s))
                .ToList();
            Workspaces[index].Sessions.AddRange(missing);
            foreach (var session in missing) EmitSessionCreated(session, Workspaces[index].Id);
            if (recent.FocusMember == true) MarkFocusMember(Workspaces[index].Id); // before the reselect, as above
            var target = (recent.SelectedSessionId is { } selected
                    && Workspaces[index].Sessions.Any(s => s.Id == selected) ? selected : (Guid?)null)
                ?? Workspaces[index].Sessions.FirstOrDefault()?.Id;
            if (target is { } resolved) SelectSession(resolved);
            Save();
            return true;
        }
        var placed = Workspaces.SelectMany(w => w.Sessions).Select(s => s.Id).ToHashSet();
        placed.UnionWith(PendingHeldSessionIds());
        placed.UnionWith(occupiedElsewhere);
        // selecting one overlapping session must not answer success while a member present only in the
        // snapshot is never rebuilt: decline so the rebuild path restores the remainder
        if (!sessionIds.All(placed.Contains)) return false;
        if (Workspaces.SelectMany(w => w.Sessions).FirstOrDefault(s => sessionIds.Contains(s.Id))
            is { } existingSession)
        {
            SelectSession(existingSession.Id);
            return true;
        }
        return false;
    }

    private Guid? PendingCloseIdContainingSession(Guid sessionId)
    {
        for (var i = PendingCloseOrder.Count - 1; i >= 0; i--)
        {
            var id = PendingCloseOrder[i];
            if (!PendingCloseRecords.TryGetValue(id, out var record)) continue;
            switch (record)
            {
                // A grace-period reopen restores the grouped batch close as one undo record,
                // matching workspace close behavior while the record is still pending.
                case PendingCloseRecord.Sessions sessions
                    when sessions.Close.Sessions.Any(c => c.Session.Id == sessionId):
                    return id;
                case PendingCloseRecord.Workspace workspace
                    when workspace.Close.Workspace.Sessions.Any(s => s.Id == sessionId):
                    return id;
            }
        }
        return null;
    }

    /// <summary>A pending close this workspace's restore should consume. Only records of the
    /// workspace itself: a foreign workspace that merely holds one of the snapshot's sessions is a
    /// close the user meant, and undoing it would resurrect a workspace they deliberately
    /// dismissed. A grouped member qualifies only when it was closed FROM this workspace.</summary>
    private Guid? PendingCloseIdForWorkspace(Guid workspaceId, IReadOnlySet<Guid> sessionIds)
    {
        for (var i = PendingCloseOrder.Count - 1; i >= 0; i--)
        {
            var id = PendingCloseOrder[i];
            if (!PendingCloseRecords.TryGetValue(id, out var record)) continue;
            switch (record)
            {
                case PendingCloseRecord.Workspace workspace
                    when workspace.Close.Workspace.Id == workspaceId:
                    return id;
                case PendingCloseRecord.Sessions sessions
                    when sessions.Close.Sessions.Any(c =>
                        c.WorkspaceId == workspaceId && sessionIds.Contains(c.Session.Id)):
                    return id;
            }
        }
        return null;
    }

    internal Guid? RecordRecentClosedSession(Session session, Guid workspaceId, string workspaceName,
                                             int workspaceIndex, int sessionIndex, Guid? id = null)
    {
        if (RecentClosedStore is not { } store) return null;
        var itemId = id ?? Guid.NewGuid();
        store.Record(new RecentClosedItem
        {
            Id = itemId,
            Kind = RecentClosedKind.Session,
            Title = session.DisplayName,
            Subtitle = workspaceName,
            Session = new RecentClosedSession
            {
                WorkspaceId = workspaceId,
                WorkspaceName = workspaceName,
                WorkspaceIndex = workspaceIndex,
                SessionIndex = sessionIndex,
                Snapshot = SessionSnapshot(session),
            },
        });
        RecentClosedDidChange?.Invoke();
        return itemId;
    }

    internal Guid? RecordRecentClosedWorkspace(Workspace workspace, Guid? selectedSessionId, bool focusMember, Guid? id = null)
    {
        if (RecentClosedStore is not { } store) return null;
        var itemId = id ?? Guid.NewGuid();
        var sessionCount = workspace.Sessions.Count;
        store.Record(new RecentClosedItem
        {
            Id = itemId,
            Kind = RecentClosedKind.Workspace,
            Title = workspace.Name,
            Subtitle = $"{sessionCount} session{(sessionCount == 1 ? "" : "s")}",
            Workspace = new RecentClosedWorkspace
            {
                Snapshot = WorkspaceSnapshot(workspace),
                SelectedSessionId = selectedSessionId,
                FocusMember = focusMember,
            },
        });
        RecentClosedDidChange?.Invoke();
        return itemId;
    }

    internal void RemoveRecentClosedItem(Guid id)
    {
        if (RecentClosedStore is not { } store) return;
        store.Remove(id);
        RecentClosedDidChange?.Invoke();
    }
}
