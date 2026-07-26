namespace AgTerm.Core.Model;

// Port of AppStore+CloseReselection.swift

public sealed partial class AppStore
{
    /// <summary>
    /// Picks the next selection after CLOSING the active session at location: the
    /// most-recently-active surviving session, else the positional ReselectionTarget. The MRU scope
    /// is deliberately narrowed to the closing session's own workspace; once the close leaves that
    /// workspace with nothing in scope, the scope widens rather than falling straight to a
    /// positional jump into the first workspace. The flagged sidebar filter DOES scope the pick
    /// (and the positional fallback); the FOCUS filter deliberately does NOT — focus is a property
    /// of the TREE, not of the selection, and every caller runs AutoUnfocusIfOutsideFocus on the
    /// pick. The scope is built from the TREE, so a session already removed there cannot come back
    /// even when it survives in SessionRecency (the soft-close paths keep it there for undo).
    /// </summary>
    internal Guid? CloseReselectionTarget((int WorkspaceIndex, int SessionIndex) after)
    {
        var filtered = SidebarMode == SidebarMode.Flagged
            ? FlaggedSessions.Select(s => s.Id).ToHashSet()
            : Workspaces.SelectMany(w => w.Sessions).Select(s => s.Id).ToHashSet();
        var inWorkspace = Workspaces[after.WorkspaceIndex].Sessions.Select(s => s.Id).ToHashSet();
        var sameWorkspace = new HashSet<Guid>(inWorkspace);
        sameWorkspace.IntersectWith(filtered);
        var scope = sameWorkspace.Count == 0 ? filtered : sameWorkspace;
        var recent = SessionRecency.Top(1, scope);
        if (recent.Count > 0) return recent[0];
        // ReselectionTarget walks the tree positionally, so in flagged mode it can land on an
        // unflagged sibling the sidebar isn't rendering. Keep the fallback inside the filter, but
        // keep it POSITIONAL: the nearest in-scope neighbor. When the close took the LAST flagged
        // session anywhere, scope is empty and the positional pick stands.
        if (SidebarMode == SidebarMode.Flagged && NearestInScopeTarget(after, scope) is { } inScope)
        {
            return inScope;
        }
        return ReselectionTarget(after);
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
