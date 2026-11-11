using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// The terminal theme as a page sees it (upstream HtmlOverlayTheme and its tests, c63e5158).
public class HtmlOverlayThemeTests
{
    [Fact]
    public void TheThemeStylesheetYieldsToAnyPageRule()
    {
        var theme = new HtmlOverlayTheme("#102030", "#e0e0e0", dark: true);
        Assert.Equal(":where(html) { color-scheme: dark; color: #e0e0e0; --agterm-background: #102030; --agterm-foreground: #e0e0e0; }",
                     theme.Stylesheet(themed: true));
        Assert.Equal("#102030", theme.Background);
        Assert.Contains($"'{theme.Stylesheet(themed: true)}'", theme.Script(themed: true));
        Assert.Contains("adoptedStyleSheets", theme.Script(themed: true));
    }

    [Fact]
    public void ALightThemeSaysSo() =>
        Assert.StartsWith(":where(html) { color-scheme: light; color: #1e1e1e;",
                          new HtmlOverlayTheme("#ffffff", "#1e1e1e", dark: false).Stylesheet(themed: true));

    [Fact]
    public void AnUnthemedPageGetsTheVariablesWithoutTheLook()
    {
        var theme = new HtmlOverlayTheme("#102030", "#e0e0e0", dark: true, Palette());
        var css = theme.Stylesheet(themed: false);
        Assert.DoesNotContain("color-scheme", css);
        Assert.DoesNotContain(" color:", css);
        Assert.StartsWith(":where(html) { --agterm-background: #102030; --agterm-foreground: #e0e0e0; --agterm-color-0: #000000;", css);
        Assert.Contains(css, theme.Script(themed: false));
    }

    [Fact]
    public void ThePaletteKeepsItsAnsiSlots()
    {
        var css = new HtmlOverlayTheme("#102030", "#e0e0e0", dark: true, Palette()).Stylesheet(themed: true);
        Assert.Contains("--agterm-color-1: #010000; ", css);
        Assert.Contains("--agterm-color-15: #0f0000; }", css);
    }

    [Fact]
    public void AnInvalidPaletteEntryOmitsOnlyItsOwnSlot()
    {
        var entries = Palette();
        entries[3] = "red; } body { display: none";
        var css = new HtmlOverlayTheme("#102030", "#e0e0e0", dark: true, entries).Stylesheet(themed: true);
        Assert.DoesNotContain("--agterm-color-3:", css);
        Assert.DoesNotContain("display", css);
        Assert.Contains("--agterm-color-4: #040000;", css);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(17)]
    public void APaletteOfTheWrongSizeOmitsEverySlot(int count)
    {
        var css = new HtmlOverlayTheme("#102030", "#e0e0e0", dark: true, Palette(count)).Stylesheet(themed: true);
        Assert.DoesNotContain("--agterm-color-", css);
    }

    [Fact]
    public void AMalformedThemeColorFallsBackToAPlainPair()
    {
        var theme = new HtmlOverlayTheme("red; } body { display: none", "#ffffff", dark: false);
        Assert.Equal("#ffffff", theme.Background);
        Assert.Equal("#1e1e1e", theme.Foreground);
        Assert.DoesNotContain("display", theme.Stylesheet(themed: true));
    }

    [Fact]
    public void AMalformedDarkThemeFallsBackToADarkPair()
    {
        var theme = new HtmlOverlayTheme("#102030", "white", dark: true);
        Assert.Equal("#1e1e1e", theme.Background);
        Assert.Equal("#d4d4d4", theme.Foreground);
    }

    [Fact]
    public void ThemesWithTheSameColorsAreEqual() =>
        Assert.Equal(new HtmlOverlayTheme("#102030", "#e0e0e0", true, Palette()),
                     new HtmlOverlayTheme("#102030", "#e0e0e0", true, Palette()));

    private static string[] Palette(int count = 16) =>
        Enumerable.Range(0, count).Select(i => $"#{i:x2}0000").ToArray();
}
