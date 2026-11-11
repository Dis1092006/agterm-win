using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

public sealed class HtmlOverlayStripTests
{
    private const string Page = @"C:\pages\index.html";

    private static HtmlOverlay Overlay(bool navigation = false, bool chromeless = false) =>
        new(new HtmlSource.File(Page, @"C:\pages")) { Navigation = navigation, Chromeless = chromeless };

    [Fact]
    public void BeforeTheFirstReportTheStripNamesTheFileAndOffersNoHistory()
    {
        var strip = HtmlOverlayStrip.From(Overlay(navigation: true), HtmlLoadState.Loading, null, null);

        Assert.True(strip.Shown);
        Assert.True(strip.Navigation);
        Assert.False(strip.CanGoBack);
        Assert.False(strip.CanGoForward);
        Assert.Equal("index.html", strip.Identity);
        Assert.Equal(Page, strip.IdentityTip);
        Assert.Null(strip.Title);
        Assert.False(strip.Failed);
    }

    [Fact]
    public void TheStripFollowsThePageShownNotTheOverlayCopy()
    {
        var shown = new HtmlPageInfo(@"C:\pages\b.html", "Bee", CanGoBack: true, CanGoForward: false);

        var strip = HtmlOverlayStrip.From(Overlay(navigation: true), HtmlLoadState.Loaded, null, shown);

        Assert.Equal("b.html", strip.Identity);
        Assert.Equal(@"C:\pages\b.html", strip.IdentityTip);
        Assert.Equal("Bee", strip.Title);
        Assert.True(strip.CanGoBack);
        Assert.False(strip.CanGoForward);
    }

    [Fact]
    public void WithoutNavigationTheStripHasNoButtonsButClose()
    {
        var strip = HtmlOverlayStrip.From(Overlay(), HtmlLoadState.Loaded, null, null);

        Assert.False(strip.Navigation);
    }

    [Fact]
    public void AChromelessPageHasNoStrip()
    {
        Assert.False(HtmlOverlayStrip.From(Overlay(chromeless: true), HtmlLoadState.Loaded, null, null).Shown);
    }

    [Fact]
    public void AFailedLoadShowsItsError()
    {
        var strip = HtmlOverlayStrip.From(Overlay(), HtmlLoadState.Failed, "file not found", null);

        Assert.True(strip.Failed);
        Assert.Equal("file not found", strip.Error);
    }

    [Fact]
    public void AFullPageTakesTheWholeArea()
    {
        Assert.Equal((0, 0, 800, 600), HtmlOverlayStrip.PanelFrame((800, 600), null, default, HtmlOverlay.ChromeHeight));
    }

    [Fact]
    public void AFloatingPageIsCenteredAtItsPercent()
    {
        Assert.Equal((200, 150, 400, 300), HtmlOverlayStrip.PanelFrame((800, 600), 50, default, HtmlOverlay.ChromeHeight));
    }

    [Fact]
    public void PointSizesAddTheStripToTheHeightAndStayUnderTheCap()
    {
        var frame = HtmlOverlayStrip.PanelFrame((800, 600), 50, new OverlayPointSize(Width: 300, Height: 200),
                                                HtmlOverlay.ChromeHeight);

        Assert.Equal((250, 300 - 229 / 2.0, 300, 229), frame);
        var capped = HtmlOverlayStrip.PanelFrame((800, 600), 50, new OverlayPointSize(Width: 5000, Height: 5000),
                                                 HtmlOverlay.ChromeHeight);
        Assert.Equal((80, 60, 640, 480), capped);
    }

    private static readonly string[] Palette = Enumerable.Range(0, 16).Select(slot => $"#0000{slot:x2}").ToArray();

    private static RendererSettingsSnapshot Renderer() =>
        RendererSettingsSnapshot.From(new AppSettings()) with
        {
            BackgroundColor = "#102030",
            ForegroundColor = "#e0e0e0",
            AnsiColors = Palette,
        };

    [Fact]
    public void APageTakesTheTerminalColors()
    {
        var theme = HtmlPageTheme.From(Renderer(), backgroundColor: null);

        Assert.Equal(new HtmlOverlayTheme("#102030", "#e0e0e0", dark: true, Palette), theme);
    }

    [Fact]
    public void TheOverlayBackgroundReplacesTheTerminalOneAndDecidesDarkness()
    {
        var theme = HtmlPageTheme.From(Renderer(), backgroundColor: "f0f0f0");

        Assert.Equal(new HtmlOverlayTheme("#f0f0f0", "#e0e0e0", dark: false, Palette), theme);
    }
}
