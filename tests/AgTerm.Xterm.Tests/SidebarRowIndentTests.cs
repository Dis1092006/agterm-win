using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

// a session row in the tree sits one level in, so its icon lines up under the workspace icon; the
// flagged view is a flat list and nothing is nested
public sealed class SidebarRowIndentTests
{
    [Fact]
    public void A_session_row_in_the_tree_is_indented_past_the_workspace_chevron()
    {
        Assert.Equal(22, SidebarRowIndent.Of(SidebarRowKind.Session, SidebarMode.Tree));
    }

    [Fact]
    public void A_session_row_in_the_flagged_view_is_not_indented()
    {
        Assert.Equal(0, SidebarRowIndent.Of(SidebarRowKind.Session, SidebarMode.Flagged));
    }

    [Fact]
    public void A_workspace_row_is_not_indented()
    {
        Assert.Equal(0, SidebarRowIndent.Of(SidebarRowKind.Workspace, SidebarMode.Tree));
    }
}
