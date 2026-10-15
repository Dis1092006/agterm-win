namespace AgTerm.Xterm.Tests;

public sealed class ThemeCatalogTests
{
    [Fact]
    public void Names_are_sorted_ignoring_case()
    {
        var catalog = ThemeCatalog.FromSources(
        [
            ("zenburn", ThemeTexts.Valid()),
            ("Atom", ThemeTexts.Valid()),
            ("agterm", ThemeTexts.Valid()),
            ("Builtin Light", ThemeTexts.Valid("#ffffff")),
        ]);

        Assert.Equal(["agterm", "Atom", "Builtin Light", "zenburn"], catalog.Names);
        Assert.Empty(catalog.Errors);
    }

    [Fact]
    public void Resolve_matches_the_exact_name()
    {
        var catalog = ThemeCatalog.FromSources([("Dracula", ThemeTexts.Valid())]);

        Assert.Equal("Dracula", catalog.Resolve("Dracula")?.Name);
        Assert.Null(catalog.Resolve("dracula"));
        Assert.Null(catalog.Resolve("Nord"));
    }

    [Fact]
    public void A_theme_that_fails_to_parse_is_reported_and_left_out_of_the_names()
    {
        var broken = ThemeTexts.Valid().Replace("background = #282a36", "background = red", StringComparison.Ordinal);

        var catalog = ThemeCatalog.FromSources([("Good", ThemeTexts.Valid()), ("Broken", broken)]);

        Assert.Equal(["Good"], catalog.Names);
        Assert.Null(catalog.Resolve("Broken"));
        Assert.Equal(
            [
                new ThemeCatalogError("Broken", 17, "background must be #rrggbb: red"),
                new ThemeCatalogError("Broken", 0, "missing background"),
            ],
            catalog.Errors);
    }
}
