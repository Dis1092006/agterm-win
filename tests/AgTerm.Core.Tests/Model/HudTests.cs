using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// Ported from agtermCore HudTests.swift, the spec, spinner and position cases (upstream ed831ab).
// The JSON round trips of HudSpec are not ported: the port never serializes a spec.
public class HudTests
{
    [Fact]
    public void HideAfterDefaultsToAPanelThatStays()
    {
        var timed = new HudSpec("deploying", HideAfter: 10);
        var persistent = new HudSpec("waiting");

        Assert.Equal(10.0, timed.HideAfter);
        Assert.Equal(10.0, timed.EffectiveHideAfter);
        Assert.Null(persistent.HideAfter);
        Assert.Equal(0.0, persistent.EffectiveHideAfter);
    }

    [Fact]
    public void HoldingTheCreationFieldsKeepsTheAutoHide()
    {
        var held = new HudSpec("deploying", HideAfter: 10)
            .HoldingCreationFields(new HudSpec("live", BackgroundColor: "#101010", FontSize: 18));

        Assert.Equal(10.0, held.HideAfter);
        Assert.Equal("#101010", held.BackgroundColor);
        Assert.Equal(18.0, held.FontSize);
        Assert.Equal("deploying", held.Message);
    }

    [Fact]
    public void HoldingTheCreationFieldsTakesEveryOtherFieldFromTheUpdate()
    {
        var update = new HudSpec("next", TextColor: "#7ec07e", Position: HudPosition.BottomRight, Markdown: true, Sticky: true, Frame: false);
        var live = new HudSpec("live", "old detail", HudSpinner.Dot, "#101010", "#202020", 40, HudPosition.TopLeft, 5, false, 18, false, true);

        Assert.Equal(update with { BackgroundColor = "#101010", FontSize = 18 }, update.HoldingCreationFields(live));
    }

    [Fact]
    public void HoldingTheCreationFieldsKeepsMarkdownStickyAndFrame()
    {
        var held = new HudSpec("# status", Markdown: true, FontSize: 20, Sticky: true, Frame: false)
            .HoldingCreationFields(new HudSpec("live", FontSize: 20));

        Assert.True(held.Markdown);
        Assert.Equal(20.0, held.FontSize);
        Assert.True(held.Sticky);
        Assert.False(held.Frame);
    }

    [Fact]
    public void OmittedFieldsTakeTheirDefaults()
    {
        var spec = new HudSpec("working");

        Assert.Equal("working", spec.Message);
        Assert.Equal(HudPosition.Center, spec.Position);
        Assert.Null(spec.Spinner);
        Assert.Null(spec.Detail);
        Assert.Null(spec.BackgroundColor);
        Assert.Null(spec.TextColor);
        Assert.Null(spec.SizePercent);
        Assert.False(spec.Markdown);
        Assert.Null(spec.FontSize);
        Assert.False(spec.Sticky);
        Assert.True(spec.Frame);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1e12)]
    [InlineData(86400.5)]
    public void UnschedulableDurationsAreRejected(double seconds) => Assert.False(HudSpec.IsValidHideAfter(seconds));

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(10.0)]
    [InlineData(86400.0)]
    public void SchedulableDurationsAreAccepted(double seconds) => Assert.True(HudSpec.IsValidHideAfter(seconds));

    [Theory]
    [InlineData(6.0)]
    [InlineData(12.0)]
    [InlineData(72.0)]
    public void FontSizesInsideTheRangeAreAccepted(double points) => Assert.True(HudSpec.IsValidFontSize(points));

    [Theory]
    [InlineData(5.9)]
    [InlineData(72.5)]
    [InlineData(0.0)]
    [InlineData(-12.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void FontSizesOutsideTheRangeAreRejected(double points) => Assert.False(HudSpec.IsValidFontSize(points));

    [Fact]
    public void TheCapsAreUpstreams()
    {
        Assert.Equal(256, HudSpec.MaxTextLength);
        Assert.Equal(4096, HudSpec.MaxMarkdownLength);
        Assert.Equal(86400.0, HudSpec.MaxHideAfter);
    }

    [Fact]
    public void SpinnerFramesAndIntervalsMatchUpstream()
    {
        Assert.Equal(new[] { "|", "/", "-", "\\" }, HudSpinners.Frames(HudSpinner.Bar));
        Assert.Equal(new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" }, HudSpinners.Frames(HudSpinner.Braille));
        Assert.Equal(new[] { "◐", "◓", "◑", "◒" }, HudSpinners.Frames(HudSpinner.Circle));
        Assert.Equal(new[] { "▁", "▃", "▄", "▅", "▆", "▇", "▆", "▅", "▄", "▃" }, HudSpinners.Frames(HudSpinner.Blocks));
        Assert.Equal(new[] { "●", "\u00A0" }, HudSpinners.Frames(HudSpinner.Dot));
        Assert.Equal(0.1, HudSpinners.IntervalSeconds(HudSpinner.Bar));
        Assert.Equal(0.08, HudSpinners.IntervalSeconds(HudSpinner.Braille));
        Assert.Equal(0.12, HudSpinners.IntervalSeconds(HudSpinner.Circle));
        Assert.Equal(0.08, HudSpinners.IntervalSeconds(HudSpinner.Blocks));
        Assert.Equal(0.45, HudSpinners.IntervalSeconds(HudSpinner.Dot));
    }

    [Fact]
    public void EveryFrameIsOneSpacelessScalar()
    {
        foreach (var style in Enum.GetValues<HudSpinner>())
        {
            Assert.NotEmpty(HudSpinners.Frames(style));
            foreach (var frame in HudSpinners.Frames(style))
            {
                Assert.Equal(1, HudLayout.CellCount(frame));
                Assert.DoesNotContain(" ", frame);
            }
            Assert.True(HudSpinners.IntervalSeconds(style) > 0);
        }
    }

    [Fact]
    public void TheDefaultStyleIsBar() => Assert.Equal(HudSpinner.Bar, HudSpinners.Default);

    [Theory]
    [InlineData(HudSpinner.Bar, "bar")]
    [InlineData(HudSpinner.Braille, "braille")]
    [InlineData(HudSpinner.Circle, "circle")]
    [InlineData(HudSpinner.Blocks, "blocks")]
    [InlineData(HudSpinner.Dot, "dot")]
    public void SpinnerNamesRoundTrip(HudSpinner style, string wire)
    {
        Assert.Equal(wire, HudSpinners.Wire(style));
        Assert.Equal(style, HudSpinners.FromWire(wire));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("")]
    [InlineData("Bar")]
    [InlineData("swirl")]
    public void NoneAndUnknownNamesAreNotStyles(string raw) => Assert.Null(HudSpinners.FromWire(raw));

    [Fact]
    public void AcceptedSpinnerNamesAddNone()
    {
        Assert.Equal("none", HudSpinners.NoneName);
        Assert.Equal("bar|braille|circle|blocks|dot|none", HudSpinners.AcceptedNamesList);
        Assert.Equal("bar, braille, circle, blocks, dot, none", HudSpinners.AcceptedNamesPhrase);
    }

    [Fact]
    public void PositionNamesDeriveFromTheCases() =>
        Assert.Equal(
            "top-left, top-center, top-right, center-left, center, center-right, bottom-left, bottom-center, bottom-right",
            HudPositions.ValidNamesPhrase);

    [Fact]
    public void AcceptedNamesAddTheAliasesToTheCanonicalSet()
    {
        Assert.Equal(
            "top-left|top-center|top-right|center-left|center|center-right|bottom-left|bottom-center|bottom-right|top|bottom",
            HudPositions.AcceptedNamesList);
        Assert.Equal(HudPositions.ValidNamesPhrase + ", top, bottom", HudPositions.AcceptedNamesPhrase);
    }

    [Fact]
    public void HudPositionSpellingMatchesTheWatermarkAnchors() =>
        Assert.Equal(
            WatermarkConfig.ValidPositions.OrderBy(name => name, StringComparer.Ordinal),
            Enum.GetValues<HudPosition>().Select(HudPositions.Wire).OrderBy(name => name, StringComparer.Ordinal));

    [Theory]
    [InlineData("top", HudPosition.TopCenter)]
    [InlineData("bottom", HudPosition.BottomCenter)]
    public void BareAliasesNormalizeToTheMiddleColumn(string raw, HudPosition expected)
    {
        Assert.Equal(expected, HudPositions.Parse(raw));
        Assert.NotEqual(raw, HudPositions.Wire(expected));
    }

    [Theory]
    [InlineData("middle")]
    [InlineData("top-middle")]
    [InlineData("TOP")]
    [InlineData("Bottom")]
    [InlineData("")]
    [InlineData("left")]
    public void ParseRejectsEverythingOutsideTheAcceptedSet(string raw) => Assert.Null(HudPositions.Parse(raw));

    [Fact]
    public void EveryAnchorRoundTripsThroughItsWireName()
    {
        foreach (var position in Enum.GetValues<HudPosition>())
            Assert.Equal(position, HudPositions.Parse(HudPositions.Wire(position)));
    }

    [Fact]
    public void TheDefaultPositionIsCenter() => Assert.Equal(HudPosition.Center, HudPositions.Default);

    [Fact]
    public void BandsSplitTheAnchorsIntoThreeRowsAndThreeColumns()
    {
        var all = Enum.GetValues<HudPosition>();
        Assert.Equal(new[] { HudPosition.TopLeft, HudPosition.TopCenter, HudPosition.TopRight },
            all.Where(p => HudPositions.VerticalBand(p) == HudBand.Leading));
        Assert.Equal(new[] { HudPosition.BottomLeft, HudPosition.BottomCenter, HudPosition.BottomRight },
            all.Where(p => HudPositions.VerticalBand(p) == HudBand.Trailing));
        Assert.Equal(new[] { HudPosition.TopRight, HudPosition.CenterRight, HudPosition.BottomRight },
            all.Where(p => HudPositions.HorizontalBand(p) == HudBand.Trailing));
        Assert.Equal(new[] { HudPosition.TopLeft, HudPosition.CenterLeft, HudPosition.BottomLeft },
            all.Where(p => HudPositions.HorizontalBand(p) == HudBand.Leading));
        Assert.Equal(HudBand.Middle, HudPositions.VerticalBand(HudPosition.Center));
        Assert.Equal(HudBand.Middle, HudPositions.HorizontalBand(HudPosition.Center));
    }

    [Fact]
    public void EdgeMarginLeavesRoomForTheLargestPanelOnBothAxes()
    {
        Assert.True(HudPositions.EdgeMarginPercent * 2 + HudLayout.MaxSizePercent <= 100);
        Assert.True(HudPositions.EdgeMarginPercent * 2 + HudLayout.ClampSizePercent(100, new HudSpec("x")) <= 100);
    }
}
