using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

public sealed class SidebarTintInksTests
{
    private static readonly string[] Ansi =
    [
        "#000000", "#cc0000", "#4e9a06", "#c4a000", "#3465a4", "#75507b", "#06989a", "#d3d7cf",
        "#555753", "#ef2929", "#8ae234", "#fce94f", "#729fcf", "#ad7fa8", "#34e2e2", "#eeeeec",
    ];

    private static readonly SidebarTintInks ThemeInks = SidebarTintInks.FromAnsi(Ansi)!;
    private static readonly SidebarTintInks SidebarInks = SidebarTintInks.Dark;

    [Theory]
    [InlineData(TintColor.Red, "#cc0000")]
    [InlineData(TintColor.Green, "#4e9a06")]
    [InlineData(TintColor.Yellow, "#c4a000")]
    [InlineData(TintColor.Blue, "#3465a4")]
    [InlineData(TintColor.Purple, "#75507b")]
    [InlineData(TintColor.Gray, "#d3d7cf")]
    public void A_theme_palette_gives_each_color_its_ansi_entry(TintColor color, string hex) =>
        Assert.Equal(hex, ThemeInks.For(color));

    [Fact]
    public void Orange_is_the_even_mix_of_ansi_red_and_yellow() =>
        // (cc+c4)/2 = c8, (00+a0)/2 = 50, (00+00)/2 = 00
        Assert.Equal("#c85000", ThemeInks.For(TintColor.Orange));

    [Fact]
    public void The_orange_mix_rounds_half_up() =>
        Assert.Equal("#020100", SidebarTintInks.FromAnsi(
            ["#000000", "#010100", "#000000", "#020000", "#000000", "#000000", "#000000", "#000000"])!
            .For(TintColor.Orange));

    [Fact]
    public void Without_a_usable_palette_there_are_no_theme_inks()
    {
        Assert.Null(SidebarTintInks.FromAnsi(null));
        Assert.Null(SidebarTintInks.FromAnsi(Ansi.Take(7).ToArray()));
        var broken = Ansi.ToArray();
        broken[4] = "blue";
        Assert.Null(SidebarTintInks.FromAnsi(broken));
    }

    [Fact]
    public void The_fallback_is_the_macos_system_colors_of_the_sidebar_appearance()
    {
        Assert.Equal(new SidebarTintInks("#ff3b30", "#ff9500", "#ffcc00", "#34c759", "#007aff", "#af52de", "#8e8e93"),
            SidebarTintInks.Light);
        Assert.Equal(new SidebarTintInks("#ff453a", "#ff9f0a", "#ffd60a", "#30d158", "#0a84ff", "#bf5af2", "#8e8e93"),
            SidebarTintInks.Dark);
        Assert.Equal(SidebarTintInks.Light, SidebarTintInks.Of(null, dark: false));
        Assert.Equal(SidebarTintInks.Dark, SidebarTintInks.Of(null, dark: true));
        Assert.Equal(ThemeInks, SidebarTintInks.Of(Ansi, dark: false));
    }

    [Fact]
    public void An_untinted_row_has_no_tint_colors() =>
        Assert.Equal(SidebarRowTint.None, SidebarRowTint.For(null, SidebarRowLook.Default, SidebarInks));

    [Fact]
    public void A_default_row_takes_the_sidebar_inks_on_the_parts_the_tint_shows()
    {
        var red = SidebarInks.For(TintColor.Red);
        Assert.Equal(new SidebarRowTint(red, null),
            SidebarRowTint.For(new SidebarTint(TintColor.Red), SidebarRowLook.Default, SidebarInks));
        Assert.Equal(new SidebarRowTint(null, red),
            SidebarRowTint.For(new SidebarTint(TintColor.Red, TintParts.Name), SidebarRowLook.Default, SidebarInks));
        Assert.Equal(new SidebarRowTint(red, red),
            SidebarRowTint.For(new SidebarTint(TintColor.Red, TintParts.Icon | TintParts.Name), SidebarRowLook.Default,
                SidebarInks));
    }

    [Fact]
    public void A_row_style_row_takes_its_theme_inks_on_the_icon_and_the_name()
    {
        var look = SidebarRowLook.From(new SidebarRowColors("#002b36", "#839496", ThemeInks), SidebarThemeStyle.Row);

        Assert.Equal(new SidebarRowTint("#3465a4", "#3465a4"),
            SidebarRowTint.For(new SidebarTint(TintColor.Blue, TintParts.Icon | TintParts.Name), look, SidebarInks));
    }

    [Fact]
    public void A_label_style_row_takes_its_theme_inks_on_the_name_and_the_sidebar_inks_on_the_icon()
    {
        var look = SidebarRowLook.From(new SidebarRowColors("#002b36", "#839496", ThemeInks), SidebarThemeStyle.Label);

        Assert.Equal(new SidebarRowTint(SidebarInks.For(TintColor.Blue), "#3465a4"),
            SidebarRowTint.For(new SidebarTint(TintColor.Blue, TintParts.Icon | TintParts.Name), look, SidebarInks));
    }

    [Fact]
    public void Theme_colors_without_inks_fall_back_to_the_sidebar_inks()
    {
        var look = SidebarRowLook.From(new SidebarRowColors("#002b36", "#839496"), SidebarThemeStyle.Row);

        Assert.Equal(new SidebarRowTint(SidebarInks.For(TintColor.Gray), null),
            SidebarRowTint.For(new SidebarTint(TintColor.Gray), look, SidebarInks));
    }

    [Fact]
    public void A_look_carries_no_tint_by_default_and_a_tint_makes_it_a_different_look()
    {
        Assert.Equal(SidebarRowTint.None, SidebarRowLook.Default.Tint);
        Assert.NotEqual(SidebarRowLook.Default, SidebarRowLook.Default with { Tint = new SidebarRowTint("#ff0000", null) });
    }
}
