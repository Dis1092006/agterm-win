using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// Whether a sidebar row draws its leading icon (upstream InterfaceElement.workspaceIcons and
/// .sessionIcons). A hidden icon takes the state it shows with it: the focus fill of the workspace
/// grid, the split divider and the flag fill of the session icon.
/// </summary>
public static class SidebarRowIcon
{
    public static bool IsShown(SidebarRowKind kind, AppSettings settings) =>
        !settings.IsInterfaceElementHidden(
            kind == SidebarRowKind.Workspace ? InterfaceElement.WorkspaceIcons : InterfaceElement.SessionIcons);
}
