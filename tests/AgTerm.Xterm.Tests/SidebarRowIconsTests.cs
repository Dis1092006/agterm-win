using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

public sealed class SidebarRowIconsTests
{
    private static readonly Guid Ws1 = Guid.NewGuid();
    private static readonly Guid Ws2 = Guid.NewGuid();

    private static SidebarRow Row(Guid workspaceId, string? iconName) =>
        new(SidebarRowKind.Session, Guid.NewGuid(), "s") { WorkspaceId = workspaceId, IconName = iconName };

    [Fact]
    public void A_row_shows_its_own_icon_name()
    {
        Assert.Equal("Home", new SidebarRowIcons().IconNameFor(Row(Ws1, "Home")));
        Assert.Null(new SidebarRowIcons().IconNameFor(Row(Ws1, null)));
    }

    [Fact]
    public void A_preview_changes_only_the_rows_of_its_workspace()
    {
        var icons = new SidebarRowIcons();

        icons.Preview(Ws1, "Globe");
        Assert.Equal("Globe", icons.IconNameFor(Row(Ws1, "Home")));
        Assert.Equal("Home", icons.IconNameFor(Row(Ws2, "Home")));
    }

    [Fact]
    public void A_preview_of_null_shows_the_default_icon()
    {
        var icons = new SidebarRowIcons();

        icons.Preview(Ws1, null);

        Assert.Null(icons.IconNameFor(Row(Ws1, "Home")));
    }

    [Fact]
    public void Ending_the_preview_shows_the_rows_own_name_again()
    {
        var icons = new SidebarRowIcons();
        icons.Preview(Ws1, "Globe");

        icons.EndPreview();

        Assert.Equal("Home", icons.IconNameFor(Row(Ws1, "Home")));
    }

    [Fact]
    public void A_saved_name_stays_until_a_snapshot_requested_after_the_save()
    {
        var icons = new SidebarRowIcons();
        icons.Observe(3);
        icons.Preview(Ws1, "Globe");
        icons.Saved(Ws1, "Globe", untilGeneration: 5);

        var stale = Row(Ws1, "Home");
        icons.Observe(4);
        Assert.Equal("Globe", icons.IconNameFor(stale));

        icons.Observe(5);
        Assert.Equal("Home", icons.IconNameFor(stale));
    }

    [Fact]
    public void A_saved_name_an_observed_snapshot_already_covers_is_not_held()
    {
        var icons = new SidebarRowIcons();
        icons.Observe(5);

        icons.Saved(Ws1, "Globe", untilGeneration: 5);

        Assert.Equal("Home", icons.IconNameFor(Row(Ws1, "Home")));
    }
}
