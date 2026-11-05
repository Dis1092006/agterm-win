using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

// upstream 91332589 (#865): workspaceIcons and sessionIcons hide a row kind's leading icon, and the
// name takes its slot
public sealed class SidebarRowIconTests
{
    [Theory]
    [InlineData(SidebarRowKind.Workspace)]
    [InlineData(SidebarRowKind.Session)]
    public void A_row_icon_is_shown_by_default(SidebarRowKind kind)
    {
        Assert.True(SidebarRowIcon.IsShown(kind, new AppSettings()));
    }

    [Fact]
    public void Hiding_workspace_icons_hides_only_the_workspace_rows_icon()
    {
        var settings = new AppSettings { HiddenInterfaceElements = ["workspaceIcons"] };

        Assert.False(SidebarRowIcon.IsShown(SidebarRowKind.Workspace, settings));
        Assert.True(SidebarRowIcon.IsShown(SidebarRowKind.Session, settings));
    }

    [Fact]
    public void Hiding_session_icons_hides_only_the_session_rows_icon()
    {
        var settings = new AppSettings { HiddenInterfaceElements = ["sessionIcons"] };

        Assert.False(SidebarRowIcon.IsShown(SidebarRowKind.Session, settings));
        Assert.True(SidebarRowIcon.IsShown(SidebarRowKind.Workspace, settings));
    }
}
