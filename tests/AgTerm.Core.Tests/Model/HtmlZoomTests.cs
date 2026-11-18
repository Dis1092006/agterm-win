using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

// One page zoom for every HTML overlay, stepped by the font actions (upstream #670, HtmlOverlay.swift
// and HtmlOverlayTests.swift at c63e5158).
public class HtmlZoomTests
{
    [Theory]
    [InlineData("increase_font_size:1", 1.0, 1.15)]
    [InlineData("increase_font_size:1", 2.5, 3.0)]
    [InlineData("increase_font_size:1", 3.0, 3.0)]
    [InlineData("increase_font_size:1", 1.1, 1.15)]
    [InlineData("decrease_font_size:1", 1.0, 0.85)]
    [InlineData("decrease_font_size:1", 0.5, 0.5)]
    [InlineData("decrease_font_size:1", 1.1, 1.0)]
    [InlineData("reset_font_size", 2.0, 1.0)]
    public void AFontActionStepsTheZoomAlongTheLadder(string action, double from, double to)
    {
        Assert.Equal(to, HtmlZoom.Applying(action, from));
    }

    [Fact]
    public void AnyOtherActionLeavesTheZoomAlone()
    {
        Assert.Null(HtmlZoom.Applying("paste_from_clipboard", 1));
    }

    [Theory]
    [InlineData(null, 1.0)]
    [InlineData(1.5, 1.5)]
    [InlineData(0.1, 0.5)]
    [InlineData(9.0, 3.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    public void AStoredZoomResolvesIntoTheLaddersRange(double? stored, double resolved)
    {
        Assert.Equal(resolved, HtmlZoom.Resolve(stored));
    }

    [Fact]
    public void TheSettingDefaultsToActualSizeAndRoundTripsUnderTheUpstreamName()
    {
        Assert.Equal(1, new AppSettings().EffectiveHtmlOverlayZoom);
        var json = ControlJson.Serialize(new AppSettings { HtmlOverlayZoom = 1.25 });
        Assert.Contains("\"htmlOverlayZoom\":1.25", json);
        Assert.Equal(1.25, ControlJson.Deserialize<AppSettings>(json)!.EffectiveHtmlOverlayZoom);
        Assert.Equal(3, new AppSettings { HtmlOverlayZoom = 40 }.EffectiveHtmlOverlayZoom);
        Assert.Equal(1, ControlJson.Deserialize<AppSettings>("{\"fontSize\":13}")!.EffectiveHtmlOverlayZoom);
    }

    [Fact]
    public void TheSettingTakesPartInEquality()
    {
        Assert.NotEqual(new AppSettings(), new AppSettings { HtmlOverlayZoom = 1.25 });
        Assert.Equal(new AppSettings { HtmlOverlayZoom = 1.25 }, new AppSettings { HtmlOverlayZoom = 1.25 });
    }

    [Fact]
    public void SteppingTheSettingStoresTheNextFactor()
    {
        var once = new AppSettings().SteppingHtmlOverlayZoom("increase_font_size:1")!;
        var twice = once.SteppingHtmlOverlayZoom("increase_font_size:1")!;

        Assert.Equal(1.25, twice.HtmlOverlayZoom);
    }

    [Fact]
    public void SteppingBackToActualSizeClearsTheStoredField()
    {
        var down = new AppSettings().SteppingHtmlOverlayZoom("decrease_font_size:1")!;

        Assert.Equal(0.85, down.HtmlOverlayZoom);
        Assert.Null(down.SteppingHtmlOverlayZoom("reset_font_size")!.HtmlOverlayZoom);
    }

    [Fact]
    public void SteppingByAnotherActionChangesNothing()
    {
        Assert.Null(new AppSettings().SteppingHtmlOverlayZoom("paste_from_clipboard"));
    }

    [Fact]
    public void TheScriptZoomsTheTopDocumentThroughOneReusedSheet()
    {
        var script = HtmlZoom.Script(1.25);

        Assert.Contains("window !== window.top", script);
        Assert.Contains("Symbol.for('agterm-zoom')", script);
        Assert.Contains("sheet.replaceSync(':root { zoom: 1.25; }');", script);
    }

    [Fact]
    public void AtActualSizeTheScriptEmptiesTheSheet()
    {
        Assert.Contains("sheet.replaceSync('');", HtmlZoom.Script(1));
    }

    [Fact]
    public void TheScriptWritesTheFactorWithADotUnderAnyCulture()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
            Assert.Contains("zoom: 0.85;", HtmlZoom.Script(0.85));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }
}
