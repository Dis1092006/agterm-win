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

    // a session row without its icon moves left by the icon column and its spacing, so the name
    // takes the icon's place
    [Theory]
    [InlineData(SidebarMode.Tree, 0)]
    [InlineData(SidebarMode.Flagged, -22)]
    public void A_session_row_without_its_icon_moves_the_name_into_the_icons_place(SidebarMode mode, double indent)
    {
        Assert.Equal(indent, SidebarRowIndent.Of(SidebarRowKind.Session, mode, iconShown: false));
    }

    // the workspace icon sits in the name's own grid, so hiding it closes the gap there
    [Fact]
    public void A_workspace_row_without_its_icon_keeps_its_chevron_in_place()
    {
        Assert.Equal(0, SidebarRowIndent.Of(SidebarRowKind.Workspace, SidebarMode.Tree, iconShown: false));
    }
}
