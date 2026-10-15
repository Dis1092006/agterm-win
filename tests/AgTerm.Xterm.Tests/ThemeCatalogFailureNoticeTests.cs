namespace AgTerm.Xterm.Tests;

public sealed class ThemeCatalogFailureNoticeTests
{
    [Fact]
    public void No_errors_give_no_notice()
    {
        Assert.Null(ThemeCatalogFailureNotice.From([]));
    }

    [Fact]
    public void One_theme_is_listed_with_its_first_error_and_line()
    {
        var notice = ThemeCatalogFailureNotice.From(
        [
            new ThemeCatalogError("Broken", 17, "background must be #rrggbb: red"),
            new ThemeCatalogError("Broken", 0, "missing background"),
        ]);

        Assert.Equal(
            new LiveFailureNotice("Theme files failed to load", "Broken (line 17): background must be #rrggbb: red"),
            notice);
    }

    [Fact]
    public void An_error_without_a_line_has_no_line_part()
    {
        var notice = ThemeCatalogFailureNotice.From([new ThemeCatalogError("Gap", 0, "missing cursor-text")]);

        Assert.Equal("Gap: missing cursor-text", notice?.Message);
    }

    [Fact]
    public void Three_themes_are_all_listed()
    {
        var notice = ThemeCatalogFailureNotice.From(
        [
            new ThemeCatalogError("A", 1, "expected key = value"),
            new ThemeCatalogError("B", 0, "missing palette 3"),
            new ThemeCatalogError("C", 2, "empty key"),
        ]);

        Assert.Equal("A (line 1): expected key = value; B: missing palette 3; C (line 2): empty key", notice?.Message);
    }

    [Fact]
    public void More_than_three_themes_are_counted_after_the_first_three()
    {
        var notice = ThemeCatalogFailureNotice.From(
        [
            new ThemeCatalogError("A", 1, "e"),
            new ThemeCatalogError("B", 1, "e"),
            new ThemeCatalogError("C", 1, "e"),
            new ThemeCatalogError("D", 1, "e"),
            new ThemeCatalogError("D", 2, "e"),
            new ThemeCatalogError("E", 1, "e"),
        ]);

        Assert.Equal("A (line 1): e; B (line 1): e; C (line 1): e; and 2 more", notice?.Message);
    }
}
