using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

/// <summary>The row menu's Tint submenu (upstream WorkspaceSidebar+TintMenu.swift): what it lists, checks
/// and enables for the tints of the rows it acts on.</summary>
public sealed class SidebarTintMenuTests
{
    private static SidebarTintMenuEntry Entry(IReadOnlyList<SidebarTintMenuEntry> entries, string title) =>
        Assert.Single(entries, e => e.Title == title);

    [Fact]
    public void Colors_list_none_and_each_color_in_order()
    {
        var colors = SidebarTintMenu.Colors([null]);

        Assert.Equal(["None", "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Gray"], colors.Select(e => e.Title));
        Assert.Null(colors[0].Choice.Color);
        Assert.Equal(TintColor.Gray, colors[^1].Choice.Color);
        Assert.All(colors, e => Assert.True(e.IsEnabled));
    }

    [Fact]
    public void None_is_checked_when_every_target_is_untinted()
    {
        Assert.True(Entry(SidebarTintMenu.Colors([null, null]), "None").IsChecked);
        Assert.False(Entry(SidebarTintMenu.Colors([null, new SidebarTint(TintColor.Red)]), "None").IsChecked);
    }

    [Fact]
    public void A_batch_checks_only_what_every_target_shares()
    {
        IReadOnlyList<SidebarTint?> mixed = [new SidebarTint(TintColor.Red, TintParts.Name), new SidebarTint(TintColor.Green)];
        Assert.DoesNotContain(SidebarTintMenu.Colors(mixed), e => e.IsChecked);
        Assert.DoesNotContain(SidebarTintMenu.Placements(mixed, SidebarTint.SessionParts), e => e.IsChecked);

        IReadOnlyList<SidebarTint?> shared = [new SidebarTint(TintColor.Blue, TintParts.Name), new SidebarTint(TintColor.Blue, TintParts.Name | TintParts.Flag)];
        Assert.Equal(["Blue"], SidebarTintMenu.Colors(shared).Where(e => e.IsChecked).Select(e => e.Title));
        Assert.Equal(["Name"], SidebarTintMenu.Placements(shared, SidebarTint.SessionParts).Where(e => e.IsChecked).Select(e => e.Title));
    }

    // Windows-only: one checkbox per part; a workspace row has no flag.
    [Fact]
    public void Placements_list_one_checkbox_per_part_the_row_has()
    {
        var tint = new SidebarTint(TintColor.Red, TintParts.Icon | TintParts.Flag);

        var session = SidebarTintMenu.Placements([tint], SidebarTint.SessionParts);
        var workspace = SidebarTintMenu.Placements([new SidebarTint(TintColor.Red)], SidebarTint.WorkspaceParts);

        Assert.Equal(["Icon", "Name", "Flag"], session.Select(e => e.Title));
        Assert.Equal([TintParts.Icon, TintParts.Name, TintParts.Flag], session.Select(e => e.Choice.Part!.Value));
        Assert.Equal([true, false, true], session.Select(e => e.IsChecked));
        Assert.Equal(["Icon", "Name"], workspace.Select(e => e.Title));
    }

    [Fact]
    public void A_checked_part_is_removed_and_an_unchecked_one_added_to_every_target()
    {
        IReadOnlyList<SidebarTint?> rows = [new SidebarTint(TintColor.Red), new SidebarTint(TintColor.Blue, TintParts.Name)];

        var placements = SidebarTintMenu.Placements(rows, SidebarTint.SessionParts);

        Assert.All(placements, e => Assert.True(e.Choice.Shown));
        var allIcon = SidebarTintMenu.Placements([new SidebarTint(TintColor.Red), new SidebarTint(TintColor.Blue)], SidebarTint.SessionParts);
        Assert.False(Entry(allIcon, "Icon").Choice.Shown);
        Assert.True(Entry(allIcon, "Flag").Choice.Shown);
    }

    [Fact]
    public void Show_on_is_disabled_unless_every_target_is_tinted()
    {
        Assert.All(SidebarTintMenu.Placements([new SidebarTint(TintColor.Red), null], SidebarTint.SessionParts), e => Assert.False(e.IsEnabled));
        Assert.All(SidebarTintMenu.Placements([new SidebarTint(TintColor.Red), null], SidebarTint.SessionParts), e => Assert.False(e.IsChecked));
        Assert.All(SidebarTintMenu.Placements([new SidebarTint(TintColor.Red)], SidebarTint.SessionParts), e => Assert.True(e.IsEnabled));
    }

    [Fact]
    public void A_choice_applied_to_one_tint_recolors_it_or_toggles_one_part()
    {
        var current = new SidebarTint(TintColor.Red, TintParts.Name);

        Assert.Equal(new SidebarTint(TintColor.Purple, TintParts.Name), SidebarTintChoice.Of(TintColor.Purple).AppliedTo(current));
        Assert.Null(SidebarTintChoice.Of(null).AppliedTo(current));
        Assert.Equal(new SidebarTint(TintColor.Purple), SidebarTintChoice.Of(TintColor.Purple).AppliedTo(null));
        Assert.Equal(new SidebarTint(TintColor.Red, TintParts.Icon | TintParts.Name), SidebarTintChoice.Show(TintParts.Icon, true).AppliedTo(current));
        Assert.Null(SidebarTintChoice.Show(TintParts.Name, false).AppliedTo(current));
        Assert.Null(SidebarTintChoice.Show(TintParts.Icon, true).AppliedTo(null));
    }
}
