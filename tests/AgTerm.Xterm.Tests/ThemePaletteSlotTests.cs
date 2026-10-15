using AgTerm.App;
using AgTerm.ControlServer;

namespace AgTerm.Xterm.Tests;

// upstream AppActions+Palette.swift: previews and commits target the slot on screen, which is the
// dark slot while following the system appearance in dark mode (ed831ab)
public sealed class ThemePaletteSlotTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Not_following_writes_the_single_theme(bool systemDark)
    {
        var args = ThemePaletteSlot.ArgsFor("Nord", new HeadlessThemeSnapshot("agterm", null, Sync: false), systemDark);

        Assert.Equal("Nord", args.Name);
        Assert.Null(args.Dark);
        Assert.Null(args.Light);
    }

    [Fact]
    public void Not_following_the_default_row_clears_the_theme()
    {
        var args = ThemePaletteSlot.ArgsFor(null, new HeadlessThemeSnapshot("agterm", null, Sync: false), systemDark: false);

        Assert.Null(args.Name);
        Assert.Null(args.Dark);
    }

    [Fact]
    public void Following_in_light_mode_writes_the_light_slot()
    {
        var args = ThemePaletteSlot.ArgsFor("Nord", new HeadlessThemeSnapshot("agterm", "Dracula", Sync: true), systemDark: false);

        Assert.Equal("Nord", args.Name);
        Assert.Null(args.Dark);
    }

    [Fact]
    public void Following_in_dark_mode_writes_the_dark_slot()
    {
        var args = ThemePaletteSlot.ArgsFor("Nord", new HeadlessThemeSnapshot("agterm", "Dracula", Sync: true), systemDark: true);

        Assert.Equal("Nord", args.Dark);
        Assert.Null(args.Name);
    }

    [Fact]
    public void Following_in_dark_mode_refuses_the_default_row_the_palette_does_not_offer()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ThemePaletteSlot.ArgsFor(null, new HeadlessThemeSnapshot("agterm", "Dracula", Sync: true), systemDark: true));
    }
}
