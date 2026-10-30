using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

public sealed class ChromePaletteTests
{
    [Fact]
    public void A_light_theme_paints_the_chrome_with_its_own_background_and_foreground()
    {
        var settings = new AppSettings { Theme = "Moonwalk" };
        var palette = ChromePalette.From(LiveThemeSettings.Resolve(settings, ThemeCatalog.Default, systemDark: true), settings);

        Assert.Equal(new ChromeColor(0xFF, 0xE4, 0xE2, 0xE0), palette.Background);
        Assert.Equal(new ChromeColor(0xFF, 0x06, 0x1F, 0x4A), palette.Foreground);
        Assert.False(palette.UseDarkChrome);
        Assert.False(palette.SidebarIsDark);
    }

    [Fact]
    public void Lines_and_secondary_text_are_the_foreground_at_upstream_opacities()
    {
        var settings = new AppSettings { Theme = "Moonwalk" };
        var palette = ChromePalette.From(LiveThemeSettings.Resolve(settings, ThemeCatalog.Default, systemDark: false), settings);

        // upstream chromeText.opacity(0.6) / (0.1) / (0.25) / (0.15)
        Assert.Equal(new ChromeColor(153, 0x06, 0x1F, 0x4A), palette.Secondary);
        Assert.Equal(new ChromeColor(26, 0x06, 0x1F, 0x4A), palette.Hairline);
        Assert.Equal(new ChromeColor(64, 0x06, 0x1F, 0x4A), palette.GroupDivider);
        Assert.Equal(new ChromeColor(38, 0x06, 0x1F, 0x4A), palette.PillFill);
    }

    [Fact]
    public void Without_a_theme_the_chrome_uses_the_default_terminal_colors()
    {
        var settings = new AppSettings();
        var palette = ChromePalette.From(LiveThemeSettings.Resolve(settings, ThemeCatalog.Default, systemDark: false), settings);

        Assert.Equal(new ChromeColor(0xFF, 0x0C, 0x0C, 0x0C), palette.Background);
        Assert.Equal(new ChromeColor(0xFF, 0xCC, 0xCC, 0xCC), palette.Foreground);
        Assert.True(palette.UseDarkChrome);
        Assert.True(palette.SidebarIsDark);
    }

    [Fact]
    public void Background_opacity_is_the_background_alpha_like_the_terminal_page()
    {
        var settings = new AppSettings { Theme = "Moonwalk", BackgroundOpacity = 0.8, BackgroundBlur = 20 };
        var palette = ChromePalette.From(LiveThemeSettings.Resolve(settings, ThemeCatalog.Default, systemDark: false), settings);

        Assert.Equal(new ChromeColor(204, 0xE4, 0xE2, 0xE0), palette.Background);
        Assert.Equal(0xFF, palette.Foreground.A);
    }

    [Theory]
    [InlineData(5, 0, 0x00)]
    [InlineData(0, 76, 0xFF)]
    [InlineData(10, 76, 0x00)]
    [InlineData(7, 31, 0x00)]
    public void The_sidebar_wash_is_black_or_white_at_the_shift_amount(int shift, byte alpha, byte channel)
    {
        var settings = new AppSettings { Theme = "Moonwalk", SidebarBackgroundShift = shift };
        var palette = ChromePalette.From(LiveThemeSettings.Resolve(settings, ThemeCatalog.Default, systemDark: false), settings);

        Assert.Equal(new ChromeColor(alpha, channel, channel, channel), palette.SidebarWash);
    }

    [Theory]
    [InlineData("#8c8c8c", 5, false)]
    [InlineData("#8c8c8c", 10, true)]
    [InlineData("#ea3323", 5, true)]
    [InlineData("#ea3323", 0, false)]
    public void The_sidebar_brightness_is_taken_on_the_washed_color(string background, int shift, bool dark)
    {
        var settings = new AppSettings { SidebarBackgroundShift = shift };
        var palette = ChromePalette.From(Presentation(settings, background), settings);

        Assert.Equal(dark, palette.SidebarIsDark);
        Assert.Equal(TerminalTheme.IsDarkColor(background), palette.UseDarkChrome);
    }

    [Fact]
    public void An_unreadable_background_falls_back_to_the_default_dark_terminal_color()
    {
        var settings = new AppSettings();
        var palette = ChromePalette.From(Presentation(settings, "nonsense"), settings);

        Assert.Equal(new ChromeColor(0xFF, 0x0C, 0x0C, 0x0C), palette.Background);
        Assert.True(palette.SidebarIsDark);
    }

    [Fact]
    public void The_palette_preview_and_the_saved_theme_give_the_same_chrome()
    {
        var settings = new AppSettings { Theme = "Moonwalk", SidebarBackgroundShift = 3, BackgroundOpacity = 0.9 };

        Assert.Equal(
            ChromePalette.From(LiveThemeSettings.Resolve(settings, ThemeCatalog.Default, systemDark: true), settings),
            ChromePalette.From(LiveThemeSettings.Preview(settings, ThemeCatalog.Default, "Moonwalk"), settings));
    }

    [Fact]
    public void High_contrast_uses_the_system_colors_opaque_and_without_a_wash()
    {
        var window = new ChromeColor(0xFF, 0x00, 0x00, 0x00);
        var text = new ChromeColor(0xFF, 0xFF, 0xFF, 0x00);

        var palette = ChromePalette.HighContrast(window, text);

        Assert.Equal(window, palette.Background);
        Assert.Equal(text, palette.Foreground);
        Assert.Equal(text, palette.Secondary);
        Assert.Equal(text, palette.Hairline);
        Assert.Equal(text, palette.GroupDivider);
        Assert.Equal(0, palette.PillFill.A);
        Assert.Equal(0, palette.SidebarWash.A);
        Assert.True(palette.UseDarkChrome);
        Assert.True(palette.SidebarIsDark);
    }

    private static LiveThemePresentation Presentation(AppSettings settings, string background)
    {
        var theme = new TerminalTheme("Test", "#ffffff", background, "#ffffff", "#000000", "#444444", "#ffffff",
            Enumerable.Repeat("#808080", 16).ToArray());
        return new LiveThemePresentation(theme, theme.IsDark, RendererSettingsSnapshot.From(settings, theme));
    }
}
