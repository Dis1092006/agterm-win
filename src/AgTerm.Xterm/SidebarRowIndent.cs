using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// How far a sidebar row's content sits in from the workspace rows (upstream's outline
/// indentationPerLevel). A session row in the tree is indented by the chevron's column and its
/// spacing, so its icon lines up under the workspace icon; the flagged view is flat.
/// </summary>
public static class SidebarRowIndent
{
    public static double Of(SidebarRowKind kind, SidebarMode mode) =>
        kind == SidebarRowKind.Session && mode == SidebarMode.Tree ? 22 : 0;
}
