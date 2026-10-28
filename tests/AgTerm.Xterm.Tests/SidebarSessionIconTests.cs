using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

// upstream WorkspaceSidebar+RowRendering: the filled icon marks a flagged session in the tree only;
// in the flat flagged view every row is flagged, so the fill would be noise
public sealed class SidebarSessionIconTests
{
    [Fact]
    public void A_flagged_session_in_the_tree_has_a_filled_icon()
    {
        Assert.True(SidebarSessionIcon.IsFilled(flagged: true, SidebarMode.Tree));
    }

    [Fact]
    public void A_flagged_session_in_the_flagged_view_has_a_plain_icon()
    {
        Assert.False(SidebarSessionIcon.IsFilled(flagged: true, SidebarMode.Flagged));
    }

    [Fact]
    public void An_unflagged_session_has_a_plain_icon()
    {
        Assert.False(SidebarSessionIcon.IsFilled(flagged: false, SidebarMode.Tree));
    }
}
