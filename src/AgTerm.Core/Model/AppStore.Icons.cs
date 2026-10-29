namespace AgTerm.Core.Model;

// An icon per workspace: a Windows-only extension (PLATFORM-NOTES "Windows-only extensions").

public sealed partial class AppStore
{
    /// <summary>Sets or clears a workspace's own sidebar icon; a blank name clears it. The store
    /// does not check the name against the icon catalog: the command layer does. The icon is part
    /// of the tree, so a change schedules tree.changed; setting the icon the workspace already has
    /// changes nothing.</summary>
    public void SetWorkspaceIcon(Guid workspaceId, string? iconName)
    {
        var icon = string.IsNullOrWhiteSpace(iconName) ? null : iconName;
        if (Workspaces.Find(w => w.Id == workspaceId) is not { } workspace || workspace.IconName == icon)
            return;
        workspace.IconName = icon;
        ScheduleTreeChanged();
        Save();
    }
}
