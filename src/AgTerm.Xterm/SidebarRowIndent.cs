using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// How far a sidebar row's content sits in from the workspace rows (upstream's outline
/// indentationPerLevel). A session row nested under a workspace row (the tree and the flagged tree
/// layout) is indented by the chevron's column and its spacing, so its icon lines up under the
/// workspace icon; the flat flagged lists nest nothing. A session row
/// that hides its icon moves left by the icon's column and spacing, so the name takes the icon's
/// place, as upstream zeroes the icon's width and gap.
/// </summary>
public static class SidebarRowIndent
{
    private const double IconColumn = 22;

    public static double Of(SidebarRowKind kind, bool nested, bool iconShown = true)
    {
        if (kind != SidebarRowKind.Session) return 0;
        var indent = nested ? IconColumn : 0;
        return iconShown ? indent : indent - IconColumn;
    }
}
