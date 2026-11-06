using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

// Windows-only: a flagged session row shows a flag after its name, in every sidebar mode; the
// sessionFlags interface element hides it
public sealed class SidebarSessionFlagTests
{
    [Fact]
    public void A_flagged_session_row_shows_the_flag()
    {
        Assert.True(SidebarSessionFlag.IsShown(SidebarRowKind.Session, flagged: true, new AppSettings()));
    }

    [Fact]
    public void An_unflagged_session_or_a_workspace_row_shows_no_flag()
    {
        Assert.False(SidebarSessionFlag.IsShown(SidebarRowKind.Session, flagged: false, new AppSettings()));
        Assert.False(SidebarSessionFlag.IsShown(SidebarRowKind.Workspace, flagged: true, new AppSettings()));
    }

    [Fact]
    public void Hiding_session_flags_hides_the_flag_and_hiding_session_icons_does_not()
    {
        Assert.False(SidebarSessionFlag.IsShown(SidebarRowKind.Session, flagged: true,
            new AppSettings { HiddenInterfaceElements = ["sessionFlags"] }));
        Assert.True(SidebarSessionFlag.IsShown(SidebarRowKind.Session, flagged: true,
            new AppSettings { HiddenInterfaceElements = ["sessionIcons"] }));
    }
}
