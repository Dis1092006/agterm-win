using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

// the flat flagged list labels a row "name : workspace"; a rename starts from the bare name so the
// tail is never committed into the custom name
public sealed class SidebarRenameSeedTests
{
    [Fact]
    public void The_flat_flagged_list_drops_the_workspace_tail()
    {
        Assert.Equal("api", SidebarRenameSeed.Of("api : w2", SidebarMode.Flagged, FlaggedViewLayout.Flat));
    }

    [Theory]
    [InlineData(SidebarMode.Flagged, FlaggedViewLayout.Plain)]
    [InlineData(SidebarMode.Flagged, FlaggedViewLayout.Tree)]
    [InlineData(SidebarMode.Tree, FlaggedViewLayout.Flat)]
    public void A_label_without_the_tail_keeps_a_name_that_contains_the_separator(
        SidebarMode mode, FlaggedViewLayout layout)
    {
        Assert.Equal("db : prod", SidebarRenameSeed.Of("db : prod", mode, layout));
    }
}