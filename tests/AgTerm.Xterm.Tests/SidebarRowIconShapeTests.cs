using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

// upstream WorkspaceSidebar.iconForSession (c63e5158): terminal for a session without a split,
// rectangle.split.2x1 or rectangle.split.1x2 by the split axis, all rounded; a workspace in the focus
// set draws the same square.grid.2x2 at a heavier weight (6d403ae7), not the filled grid
public sealed class SidebarRowIconShapeTests
{
    private const string RoundedFrame =
        "M2.5,2.5 H13.5 A2,2 0 0 1 15.5,4.5 V10.5 A2,2 0 0 1 13.5,12.5 H2.5 A2,2 0 0 1 0.5,10.5 V4.5 A2,2 0 0 1 2.5,2.5 Z";

    [Theory]
    [InlineData(SplitAxis.LeftRight)]
    [InlineData(SplitAxis.TopBottom)]
    public void A_session_without_a_split_draws_the_terminal_with_a_prompt(SplitAxis axis)
    {
        Assert.Equal(RoundedFrame + " M3.5,5.5 L5.5,7.5 L3.5,9.5 M7,9.5 H10.5",
            SidebarSessionIcon.PathData(hasSplit: false, axis));
    }

    [Fact]
    public void A_side_by_side_split_draws_a_vertical_divider()
    {
        Assert.Equal(RoundedFrame + " M8,2.5 V12.5", SidebarSessionIcon.PathData(hasSplit: true, SplitAxis.LeftRight));
    }

    [Fact]
    public void A_stacked_split_draws_a_horizontal_divider()
    {
        Assert.Equal(RoundedFrame + " M0.5,7.5 H15.5", SidebarSessionIcon.PathData(hasSplit: true, SplitAxis.TopBottom));
    }

    [Fact]
    public void A_focused_workspace_draws_the_grid_heavier()
    {
        Assert.Equal(1, SidebarWorkspaceIcon.GridStrokeThickness(focused: false));
        Assert.Equal(2, SidebarWorkspaceIcon.GridStrokeThickness(focused: true));
    }
}
