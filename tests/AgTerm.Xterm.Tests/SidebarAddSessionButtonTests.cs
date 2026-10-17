using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

// upstream SidebarCellView: a workspace row reveals its "+" while the pointer is over it, unless the
// workspaceAddSession interface element is hidden; session rows have no button
public sealed class SidebarAddSessionButtonTests
{
    [Fact]
    public void A_hovered_workspace_row_shows_the_button()
    {
        Assert.True(SidebarAddSessionButton.IsShown(SidebarRowKind.Workspace, pointerOver: true, new AppSettings()));
    }

    [Fact]
    public void An_idle_workspace_row_hides_the_button()
    {
        Assert.False(SidebarAddSessionButton.IsShown(SidebarRowKind.Workspace, pointerOver: false, new AppSettings()));
    }

    [Fact]
    public void A_session_row_never_shows_the_button()
    {
        Assert.False(SidebarAddSessionButton.IsShown(SidebarRowKind.Session, pointerOver: true, new AppSettings()));
    }

    [Fact]
    public void Hiding_the_interface_element_hides_the_button_on_hover()
    {
        var settings = new AppSettings { HiddenInterfaceElements = ["workspaceAddSession"] };

        Assert.False(SidebarAddSessionButton.IsShown(SidebarRowKind.Workspace, pointerOver: true, settings));
    }
}
