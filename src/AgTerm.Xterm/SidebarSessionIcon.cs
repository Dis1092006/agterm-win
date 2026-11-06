using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// A session row's icon (upstream WorkspaceSidebar.iconForSession): a rounded terminal with a prompt,
/// or a rounded split frame divided by the split axis. Whether it is filled (upstream's terminal.fill
/// and rectangle.split.*.fill): the fill marks a flagged session in the tree; the flagged view lists
/// only flagged sessions, so there it draws the plain icon. The path figures fill even-odd, so a
/// filled terminal knocks out its prompt.
/// </summary>
public static class SidebarSessionIcon
{
    private const string Frame =
        "M2.5,2.5 H13.5 A2,2 0 0 1 15.5,4.5 V10.5 A2,2 0 0 1 13.5,12.5 H2.5 A2,2 0 0 1 0.5,10.5 V4.5 A2,2 0 0 1 2.5,2.5 Z";

    public static string PathData(bool hasSplit, SplitAxis axis) =>
        !hasSplit ? Frame + " M3.5,5.5 L5.5,7.5 L3.5,9.5 M7,9.5 H10.5"
        : axis == SplitAxis.TopBottom ? Frame + " M0.5,7.5 H15.5"
        : Frame + " M8,2.5 V12.5";

    public static bool IsFilled(bool flagged, SidebarMode mode) => flagged && mode == SidebarMode.Tree;
}
