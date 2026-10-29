using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// Whether a session row draws its icon filled (upstream's terminal.fill and
/// rectangle.split.2x1.fill). The fill marks a flagged session in the tree; the flagged view lists
/// only flagged sessions, so there it draws the plain icon.
/// </summary>
public static class SidebarSessionIcon
{
    public static bool IsFilled(bool flagged, SidebarMode mode) => flagged && mode == SidebarMode.Tree;
}
