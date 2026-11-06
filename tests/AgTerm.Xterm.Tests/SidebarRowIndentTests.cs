using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

// a session row nested under a workspace row sits one level in, so its icon lines up under the
// workspace icon; the flat flagged lists nest nothing
public sealed class SidebarRowIndentTests
{
    [Fact]
    public void A_nested_session_row_is_indented_past_the_workspace_chevron()
    {
        Assert.Equal(22, SidebarRowIndent.Of(SidebarRowKind.Session, nested: true));
    }

    [Fact]
    public void A_session_row_in_a_flat_list_is_not_indented()
    {
        Assert.Equal(0, SidebarRowIndent.Of(SidebarRowKind.Session, nested: false));
    }

    [Fact]
    public void A_workspace_row_is_not_indented()
    {
        Assert.Equal(0, SidebarRowIndent.Of(SidebarRowKind.Workspace, nested: true));
    }

    // a session row without its icon moves left by the icon column and its spacing, so the name
    // takes the icon's place
    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, -22)]
    public void A_session_row_without_its_icon_moves_the_name_into_the_icons_place(bool nested, double indent)
    {
        Assert.Equal(indent, SidebarRowIndent.Of(SidebarRowKind.Session, nested, iconShown: false));
    }

    // the workspace icon sits in the name's own grid, so hiding it closes the gap there
    [Fact]
    public void A_workspace_row_without_its_icon_keeps_its_chevron_in_place()
    {
        Assert.Equal(0, SidebarRowIndent.Of(SidebarRowKind.Workspace, nested: true, iconShown: false));
    }

    // the flagged tree layout nests its flagged sessions under workspace rows like the ordinary tree
    [Theory]
    [InlineData(SidebarMode.Tree, FlaggedViewLayout.Flat, true)]
    [InlineData(SidebarMode.Flagged, FlaggedViewLayout.Flat, false)]
    [InlineData(SidebarMode.Flagged, FlaggedViewLayout.Plain, false)]
    [InlineData(SidebarMode.Flagged, FlaggedViewLayout.Tree, true)]
    public void A_snapshot_nests_session_rows_where_it_renders_workspace_rows(
        SidebarMode mode, FlaggedViewLayout layout, bool nested)
    {
        var snapshot = new SidebarSnapshot
        {
            Rows = [], ShapeKey = "", Workspaces = [], Mode = mode, FlaggedLayout = layout,
        };
        Assert.Equal(nested, snapshot.RendersWorkspaceRows);
    }
}