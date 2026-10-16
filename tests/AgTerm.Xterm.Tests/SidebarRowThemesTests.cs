using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

public sealed class SidebarRowThemesTests
{
    private static readonly Guid Ws1 = Guid.NewGuid();
    private static readonly Guid Ws2 = Guid.NewGuid();

    private static readonly SidebarRowColors SolarizedColors = new("#002b36", "#839496");
    private static readonly SidebarRowColors GruvboxColors = new("#fbf1c7", "#3c3836");
    private static readonly SidebarRowColors NordColors = new("#2e3440", "#d8dee9");

    private static TerminalTheme Theme(string name, SidebarRowColors colors) =>
        new(name, colors.Foreground, colors.Background, "#ffffff", "#000000", "#cccccc", "#000000",
            Enumerable.Repeat("#000000", 16).ToArray());

    private static readonly Dictionary<string, TerminalTheme> Catalog = new()
    {
        ["Solarized"] = Theme("Solarized", SolarizedColors),
        ["Gruvbox Light"] = Theme("Gruvbox Light", GruvboxColors),
        ["Nord"] = Theme("Nord", NordColors),
    };

    private static SidebarRowThemes Themes() =>
        new(name => Catalog.TryGetValue(name, out var theme) ? theme : null);

    private static SidebarRow Row(Guid workspaceId, string? themeName) =>
        new(SidebarRowKind.Session, Guid.NewGuid(), "s") { WorkspaceId = workspaceId, ThemeName = themeName };

    [Fact]
    public void A_row_of_a_themed_workspace_gets_the_exact_theme_colors()
    {
        Assert.Equal(SolarizedColors, Themes().ColorsFor(Row(Ws1, "Solarized"), enabled: true));
    }

    [Fact]
    public void A_row_without_a_theme_or_with_an_unknown_theme_gets_no_colors()
    {
        var themes = Themes();

        Assert.Null(themes.ColorsFor(Row(Ws1, null), enabled: true));
        Assert.Null(themes.ColorsFor(Row(Ws1, "Gone"), enabled: true));
    }

    [Fact]
    public void Turned_off_gives_no_colors_even_for_a_themed_row()
    {
        Assert.Null(Themes().ColorsFor(Row(Ws1, "Solarized"), enabled: false));
    }

    [Fact]
    public void A_preview_recolors_only_the_rows_of_its_workspace()
    {
        var themes = Themes();

        themes.Preview(Ws1, "Gruvbox Light");
        Assert.Equal(GruvboxColors, themes.ColorsFor(Row(Ws1, "Solarized"), enabled: true));
        Assert.Equal(SolarizedColors, themes.ColorsFor(Row(Ws2, "Solarized"), enabled: true));

        themes.Preview(Ws1, null);
        Assert.Null(themes.ColorsFor(Row(Ws1, "Solarized"), enabled: true));

        themes.Preview(Ws1, "Solarized");
        Assert.Equal(SolarizedColors, themes.ColorsFor(Row(Ws1, "Gone"), enabled: true));
    }

    [Fact]
    public void Ending_the_preview_shows_the_rows_own_themes_again()
    {
        var themes = Themes();
        themes.Preview(Ws1, "Gruvbox Light");

        themes.EndPreview();

        Assert.Equal(SolarizedColors, themes.ColorsFor(Row(Ws1, "Solarized"), enabled: true));
    }

    [Fact]
    public void A_saved_pick_stays_until_a_snapshot_requested_after_the_save()
    {
        var themes = Themes();
        themes.Observe(generation: 3);
        themes.Preview(Ws1, "Gruvbox Light");
        themes.Saved(Ws1, "Gruvbox Light", untilGeneration: 5);

        var stale = Row(Ws1, "Solarized");
        themes.Observe(generation: 4);
        Assert.Equal(GruvboxColors, themes.ColorsFor(stale, enabled: true));

        themes.Observe(generation: 5);
        Assert.Equal(SolarizedColors, themes.ColorsFor(stale, enabled: true));
    }

    [Fact]
    public void A_snapshot_after_the_save_wins_even_when_it_shows_the_old_theme_again()
    {
        var themes = Themes();
        themes.Observe(generation: 3);
        themes.Saved(Ws1, "Gruvbox Light", untilGeneration: 5);

        var setBack = Row(Ws1, "Solarized");
        themes.Observe(generation: 7);

        Assert.Equal(SolarizedColors, themes.ColorsFor(setBack, enabled: true));
    }

    [Fact]
    public void A_saved_pick_an_observed_snapshot_already_covers_is_not_held()
    {
        var themes = Themes();
        themes.Observe(generation: 5);

        themes.Saved(Ws1, "Gruvbox Light", untilGeneration: 5);

        Assert.Equal(SolarizedColors, themes.ColorsFor(Row(Ws1, "Solarized"), enabled: true));
    }
}
