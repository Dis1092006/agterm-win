namespace AgTerm.Core.Model;

// Sidebar row tints, ported from upstream AppStore+Tint (f3fea57b).

public sealed partial class AppStore
{
    /// <summary>Replaces a session's tint (null clears) and persists. Returns false, changing
    /// nothing, for an unknown id or the tint the session already has.</summary>
    public bool SetSessionTint(Guid sessionId, SidebarTint? tint)
    {
        if (SessionWithId(sessionId) is not { } session || session.Tint == tint) return false;
        session.Tint = tint;
        Save();
        ScheduleTreeChanged();
        return true;
    }

    /// <summary>Replaces a workspace's own tint (null clears) and persists, with the session form's
    /// no-op rule. The workspace's sessions keep their own tints.</summary>
    public bool SetWorkspaceTint(Guid workspaceId, SidebarTint? tint)
    {
        tint = SidebarTint.ForWorkspace(tint);
        if (Workspaces.Find(w => w.Id == workspaceId) is not { } workspace || workspace.Tint == tint) return false;
        workspace.Tint = tint;
        Save();
        ScheduleTreeChanged();
        return true;
    }

    /// <summary>Gives every session in ids the color (null clears), keeping each tinted row's own
    /// parts and starting an untinted one on the icon. One save for the whole batch.</summary>
    public void SetTintColor(TintColor? color, IEnumerable<Guid> ids) =>
        ApplyTints(ids, current => SidebarTint.Recolor(current, color));

    /// <summary>Shows or hides each tinted session's own color on one part. An untinted session is
    /// left alone, so no row stores a placement it does not show, and a row left with no part loses
    /// its tint.</summary>
    public void SetTintPart(TintParts part, bool shown, IEnumerable<Guid> ids) =>
        ApplyTints(ids, current => SidebarTint.WithPart(current, part, shown));

    private void ApplyTints(IEnumerable<Guid> ids, Func<SidebarTint?, SidebarTint?> next)
    {
        var changed = false;
        foreach (var id in ids)
        {
            if (SessionWithId(id) is not { } session) continue;
            var tint = next(session.Tint);
            if (tint == session.Tint) continue;
            session.Tint = tint;
            changed = true;
        }
        if (!changed) return;
        Save();
        ScheduleTreeChanged();
    }
}
