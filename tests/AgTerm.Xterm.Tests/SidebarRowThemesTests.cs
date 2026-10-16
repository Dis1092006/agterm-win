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
    public void A_saved_pick_stays_while_the_rows_still_show_the_old_theme()
    {
        var themes = Themes();
        themes.Observe([Row(Ws1, "Solarized")]);
        themes.Preview(Ws1, "Gruvbox Light");
        themes.Saved(Ws1, "Gruvbox Light");

        var stale = Row(Ws1, "Solarized");
        themes.Observe([stale]);
        Assert.Equal(GruvboxColors, themes.ColorsFor(stale, enabled: true));

        var confirming = Row(Ws1, "Gruvbox Light");
        themes.Observe([confirming]);
        Assert.Equal(GruvboxColors, themes.ColorsFor(confirming, enabled: true));

        var cleared = Row(Ws1, null);
        themes.Observe([cleared]);
        Assert.Null(themes.ColorsFor(cleared, enabled: true));
    }

    [Fact]
    public void A_saved_pick_gives_way_to_a_third_theme()
    {
        var themes = Themes();
        themes.Observe([Row(Ws1, "Solarized")]);
        themes.Saved(Ws1, "Gruvbox Light");

        var third = Row(Ws1, "Nord");
        themes.Observe([third]);

        Assert.Equal(NordColors, themes.ColorsFor(third, enabled: true));
    }

    [Fact]
    public void A_saved_pick_gives_way_when_its_workspace_has_no_rows()
    {
        var themes = Themes();
        themes.Observe([Row(Ws1, "Solarized")]);
        themes.Saved(Ws1, "Gruvbox Light");

        themes.Observe([Row(Ws2, null)]);

        Assert.Equal(SolarizedColors, themes.ColorsFor(Row(Ws1, "Solarized"), enabled: true));
    }

    [Fact]
    public void A_saved_pick_the_rows_already_show_is_not_held()
    {
        var themes = Themes();
        themes.Observe([Row(Ws1, "Gruvbox Light")]);
        themes.Saved(Ws1, "Gruvbox Light");

        var later = Row(Ws1, "Solarized");
        themes.Observe([later]);

        Assert.Equal(SolarizedColors, themes.ColorsFor(later, enabled: true));
    }
}
