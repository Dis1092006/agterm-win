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
        Assert.DoesNotContain(SidebarTintMenu.Placements(mixed), e => e.IsChecked);

        IReadOnlyList<SidebarTint?> shared = [new SidebarTint(TintColor.Blue, TintParts.Name), new SidebarTint(TintColor.Blue, TintParts.Name)];
        Assert.Equal(["Blue"], SidebarTintMenu.Colors(shared).Where(e => e.IsChecked).Select(e => e.Title));
        Assert.Equal(["Name"], SidebarTintMenu.Placements(shared).Where(e => e.IsChecked).Select(e => e.Title));
    }

    [Fact]
    public void Placements_list_icon_name_and_both()
    {
        var placements = SidebarTintMenu.Placements([new SidebarTint(TintColor.Red, TintParts.Icon | TintParts.Name)]);

        Assert.Equal(["Icon", "Name", "Icon and Name"], placements.Select(e => e.Title));
        Assert.Equal([TintParts.Icon, TintParts.Name, TintParts.Icon | TintParts.Name], placements.Select(e => e.Choice.Parts!.Value));
        Assert.True(Entry(placements, "Icon and Name").IsChecked);
    }

    [Fact]
    public void Show_on_is_disabled_unless_every_target_is_tinted()
    {
        Assert.All(SidebarTintMenu.Placements([new SidebarTint(TintColor.Red), null]), e => Assert.False(e.IsEnabled));
        Assert.All(SidebarTintMenu.Placements([new SidebarTint(TintColor.Red)]), e => Assert.True(e.IsEnabled));
    }

    [Fact]
    public void A_choice_applied_to_one_tint_recolors_or_moves_it()
    {
        var current = new SidebarTint(TintColor.Red, TintParts.Name);

        Assert.Equal(new SidebarTint(TintColor.Purple, TintParts.Name), SidebarTintChoice.Of(TintColor.Purple).AppliedTo(current));
        Assert.Null(SidebarTintChoice.Of(null).AppliedTo(current));
        Assert.Equal(new SidebarTint(TintColor.Purple), SidebarTintChoice.Of(TintColor.Purple).AppliedTo(null));
        Assert.Equal(new SidebarTint(TintColor.Red, TintParts.Icon), SidebarTintChoice.On(TintParts.Icon).AppliedTo(current));
        Assert.Null(SidebarTintChoice.On(TintParts.Icon).AppliedTo(null));
    }
}
