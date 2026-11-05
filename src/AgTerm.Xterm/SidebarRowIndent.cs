using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// How far a sidebar row's content sits in from the workspace rows (upstream's outline
/// indentationPerLevel). A session row in the tree is indented by the chevron's column and its
/// spacing, so its icon lines up under the workspace icon; the flagged view is flat. A session row
/// that hides its icon moves left by the icon's column and spacing, so the name takes the icon's
/// place, as upstream zeroes the icon's width and gap.
/// </summary>
public static class SidebarRowIndent
{
    private const double IconColumn = 22;

    public static double Of(SidebarRowKind kind, SidebarMode mode, bool iconShown = true)
    {
        if (kind != SidebarRowKind.Session) return 0;
        var indent = mode == SidebarMode.Tree ? IconColumn : 0;
        return iconShown ? indent : indent - IconColumn;
    }
}
