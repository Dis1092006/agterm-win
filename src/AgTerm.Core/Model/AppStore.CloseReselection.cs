namespace AgTerm.Core.Model;

// Port of AppStore+CloseReselection.swift

public sealed partial class AppStore
{
    /// <summary>
    /// Picks the next selection after CLOSING the active session at location: the
    /// most-recently-active surviving session, else a positional walk. The MRU scope narrows to the
    /// closing session's own workspace intersected with the VISIBLE set (NavigableSessions, so both
    /// the flagged list and the focus filter apply): an unscoped survivor could pull the user into
    /// another workspace, and a pick the sidebar is not drawing would strand the selection.
    /// Exhausting a scope widens through three levels: that workspace's visible sessions, everything
    /// visible, the whole tree. Only a widened scope with no recency falls to a positional walk, and
    /// while any narrowing is applied that is the removed session's neighbour in the saved flagged
    /// order under the flat and plain layouts, else the flattened in-scope walk over the widened
    /// scope, never ReselectionTarget; narrowed keys on the mode, so it stays true after the widening.
    /// A pick outside the marked set meets each caller's DisableFocusIfSelectionOutsideSet. The scope
    /// is built from the TREE, so a session already removed there cannot come back even when it
    /// survives in SessionRecency (the soft-close paths keep it there for undo).
    /// </summary>
    internal Guid? CloseReselectionTarget((int WorkspaceIndex, int SessionIndex) after, Guid removing)
    {
        var visible = NavigableSessions.Select(s => s.Id).ToHashSet();
        var everything = Workspaces.SelectMany(w => w.Sessions).Select(s => s.Id).ToHashSet();
        var sameWorkspace = Workspaces[after.WorkspaceIndex].Sessions.Select(s => s.Id).ToHashSet();
        sameWorkspace.IntersectWith(visible);
        // the whole-tree level is what makes widen mean widen: without it an emptied visible scope
        // falls to a positional jump into the first workspace
        var scope = sameWorkspace.Count > 0 ? sameWorkspace : visible.Count > 0 ? visible : everything;
        var recent = SessionRecency.Top(1, scope);
        if (recent.Count > 0) return recent[0];
        // reachable with no recency at all, e.g. the first close after a restore
        var narrowed = SidebarMode == SidebarMode.Flagged || FocusEnabled;
        if (narrowed && NearestInSavedFlaggedOrder(removing, scope) is { } inOrder)
        {
            return inOrder;
        }
        if (narrowed && NearestInScopeTarget(after, scope) is { } inScope)
        {
            return inScope;
        }
        return ReselectionTarget(after);
    }

    // the flat and plain lists show the saved order, so their positional neighbour is found there; the
    // removed id is still in the order here, since the deletion paths drop it only after reselecting
    private Guid? NearestInSavedFlaggedOrder(Guid removing, IReadOnlySet<Guid> scope)
    {
        if (SidebarMode != SidebarMode.Flagged || FlaggedLayout == FlaggedViewLayout.Tree) return null;
        var slot = _flaggedOrder.IndexOf(removing);
        if (slot < 0) return null;
        for (var i = slot + 1; i < _flaggedOrder.Count; i++)
        {
            if (scope.Contains(_flaggedOrder[i])) return _flaggedOrder[i];
        }
        for (var i = slot - 1; i >= 0; i--)
        {
            if (scope.Contains(_flaggedOrder[i])) return _flaggedOrder[i];
        }
        return null;
    }

    /// <summary>ReselectionTarget's walk restricted to scope, over the tree FLATTENED in sidebar
    /// order: the in-scope session that shifted into the removed slot, else the nearest one before
    /// it. The walk spans workspaces because the scope can.</summary>
    private Guid? NearestInScopeTarget((int WorkspaceIndex, int SessionIndex) after, IReadOnlySet<Guid> scope)
    {
        var sessions = Workspaces[after.WorkspaceIndex].Sessions;
        var before = Workspaces.Take(after.WorkspaceIndex).Sum(w => w.Sessions.Count);
        var removedSlot = before + Math.Min(after.SessionIndex, sessions.Count);
        var flattened = Workspaces.SelectMany(w => w.Sessions).ToList();
        for (var i = removedSlot; i < flattened.Count; i++)
        {
            if (scope.Contains(flattened[i].Id)) return flattened[i].Id;
        }
        for (var i = Math.Min(removedSlot, flattened.Count) - 1; i >= 0; i--)
        {
            if (scope.Contains(flattened[i].Id)) return flattened[i].Id;
        }
        return null;
    }
}
