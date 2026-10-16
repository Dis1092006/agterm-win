namespace AgTerm.Core.Model;

// A theme per workspace: a Windows-only extension (PLATFORM-NOTES "Windows-only extensions").

public sealed partial class AppStore
{
    /// <summary>Sets or clears a workspace's own terminal theme; a blank name clears it. The store
    /// does not check the name against the theme catalog: the command layer does, and a stored
    /// name the catalog lacks shows the global theme. The theme is part of the tree, so a change
    /// schedules tree.changed; setting the theme the workspace already has changes nothing.</summary>
    public void SetWorkspaceTheme(Guid workspaceId, string? themeName)
    {
        var theme = string.IsNullOrWhiteSpace(themeName) ? null : themeName;
        if (Workspaces.Find(w => w.Id == workspaceId) is not { } workspace || workspace.ThemeName == theme)
            return;
        workspace.ThemeName = theme;
        ScheduleTreeChanged();
        Save();
    }
}
