namespace AgTerm.Core.Model;

/// <summary>The session the window selects before the workspace theme palette opens, so the
/// preview shows on screen.</summary>
public static class WorkspaceThemeTarget
{
    /// <summary>The workspace's most recently selected session, or its first. Null when the
    /// selected session already belongs to the workspace, and when the workspace has no sessions
    /// or is not in the tree.</summary>
    public static Guid? SessionToSelect(AppStore store, Guid workspaceId)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (store.Workspaces.Find(w => w.Id == workspaceId) is not { Sessions.Count: > 0 } workspace)
            return null;
        var ids = workspace.Sessions.Select(s => s.Id).ToHashSet();
        if (store.SelectedSessionId is { } selected && ids.Contains(selected))
            return null;
        foreach (var id in store.SessionRecency.Items)
        {
            if (ids.Contains(id))
                return id;
        }
        return workspace.Sessions[0].Id;
    }
}
