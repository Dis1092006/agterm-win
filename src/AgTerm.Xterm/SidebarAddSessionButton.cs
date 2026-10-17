using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// The workspace row's inline "+" (upstream SidebarCellView.addButton): revealed while the pointer is
/// over a workspace row, the Finder/Xcode hover convention, and never on a session row. The
/// workspaceAddSession interface element hides it.
/// </summary>
public static class SidebarAddSessionButton
{
    public static bool IsShown(SidebarRowKind kind, bool pointerOver, AppSettings settings) =>
        kind == SidebarRowKind.Workspace
        && pointerOver
        && !settings.IsInterfaceElementHidden(InterfaceElement.WorkspaceAddSession);
}
