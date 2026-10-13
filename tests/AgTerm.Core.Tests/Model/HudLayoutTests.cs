using System.Globalization;
using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// Ported from agtermCore HudTests.swift, the plain layout cases (upstream ed831ab). Upstream's
// renderedBody and paintGrid cases are macOS helper plumbing: their line content is pinned through
// BodyLines and their sizes through Box and PanelSize. The markdown cases belong to Task 13.
public class HudLayoutTests
{
    private static readonly PaneMetrics Pane1200x800 = new(8, 18, 1200, 800);

    public static IEnumerable<object[]> AllPositions => Enum.GetValues<HudPosition>().Select(p => new object[] { p });

    [Fact]
    public void ShortMessageBoxIsContentPlusPadding() =>
        Assert.Equal((17 + HudLayout.HorizontalPadding * 2, 1 + HudLayout.VerticalPadding * 2), HudLayout.Box(new HudSpec("gathering options")));

    [Fact]
    public void EmptyMessageStillProducesAOneRowBox()
    {
        Assert.Empty(HudLayout.BodyLines(new HudSpec("")));
        Assert.Equal((5, 3), HudLayout.Box(new HudSpec("")));
    }

    [Fact]
    public void LongSingleWordIsBrokenAtMaxColumns()
    {
        var word = new string('x', 130);

        Assert.Equal(new[] { new string('x', 60), new string('x', 60), new string('x', 10) }, HudLayout.Wrap(word, HudLayout.MaxColumns));
        Assert.Equal((HudLayout.MaxColumns + HudLayout.HorizontalPadding * 2, 3 + HudLayout.VerticalPadding * 2), HudLayout.Box(new HudSpec(word)));
    }

    [Fact]
    public void WordWrapKeepsWordsWholeUpToMaxColumns()
    {
        var lines = HudLayout.Wrap(string.Join(" ", Enumerable.Repeat("word", 20)), HudLayout.MaxColumns);

        Assert.Equal(2, lines.Count);
        Assert.Equal(string.Join(" ", Enumerable.Repeat("word", 12)), lines[0]);
        Assert.Equal(string.Join(" ", Enumerable.Repeat("word", 8)), lines[1]);
        Assert.All(lines, line => Assert.True(HudLayout.CellCount(line) <= HudLayout.MaxColumns));
    }

    [Fact]
    public void ALongWordFlushesTheLineBeforeIt() =>
        Assert.Equal(new[] { "ab", "xxxxx", "xx" }, HudLayout.Wrap("ab xxxxxxx", 5));

    [Fact]
    public void ALongWordOfSurrogatePairsSplitsBetweenScalars() =>
        Assert.Equal(new[] { "\U0001F98A\U0001F98A", "\U0001F98A" }, HudLayout.Wrap("\U0001F98A\U0001F98A\U0001F98A", 2));

    [Fact]
    public void RepeatedAndOuterSpacesAreDropped()
    {
        Assert.Equal(new[] { "a b" }, HudLayout.Wrap("a   b", 60));
        Assert.Equal(new[] { "a" }, HudLayout.Wrap("  a  ", 60));
    }

    [Fact]
    public void OnlyASpaceIsABreakPoint()
    {
        Assert.Equal(new[] { "a\u00A0b" }, HudLayout.Wrap("a\u00A0b", 60));
        Assert.Equal(new[] { "a\tb" }, HudLayout.Wrap("a\tb", 60));
    }

    [Fact]
    public void ColumnsBelowOneWrapAtOne() => Assert.Equal(new[] { "a", "b" }, HudLayout.Wrap("ab", 0));

    [Fact]
    public void DetailFollowsMessageAfterASingleEmptyLine()
    {
        var spec = new HudSpec("gathering options", "scanning 4 repositories");

        Assert.Equal(new[] { "gathering options", "", "scanning 4 repositories" }, HudLayout.BodyLines(spec));
        Assert.Equal((23 + HudLayout.HorizontalPadding * 2, 3 + HudLayout.VerticalPadding * 2), HudLayout.Box(spec));
    }

    [Fact]
    public void EmptyDetailAddsNoSeparator()
    {
        var spec = new HudSpec("working", "   ");

        Assert.Equal(new[] { "working" }, HudLayout.BodyLines(spec));
        Assert.Equal((11, 3), HudLayout.Box(spec));
    }

    [Fact]
    public void EmbeddedNewlinesBecomeHardBreaksWithNoBlankLines()
    {
        var spec = new HudSpec("one\n\ntwo three", "four\nfive");

        Assert.Equal(new[] { "one", "two three", "", "four", "five" }, HudLayout.BodyLines(spec));
        Assert.Equal(5 + HudLayout.VerticalPadding * 2, HudLayout.Box(spec).Rows);
    }

    [Fact]
    public void SpinnerWidensTheBoxWithoutRewrapping()
    {
        var plain = HudLayout.Box(new HudSpec("hi"));
        var spinning = HudLayout.Box(new HudSpec("hi", Spinner: HudSpinner.Braille));

        Assert.Equal(plain.Columns + HudLayout.SpinnerWidth, spinning.Columns);
        Assert.Equal(plain.Rows, spinning.Rows);
    }

    [Fact]
    public void AFramelessBoxHasNoPaddingRows()
    {
        Assert.Equal(1, HudLayout.Box(new HudSpec("one line", Frame: false)).Rows);
        Assert.Equal(0, HudLayout.VerticalPaddingFor(new HudSpec("x", Frame: false)));
        Assert.Equal(HudLayout.VerticalPadding, HudLayout.VerticalPaddingFor(new HudSpec("x")));
    }

    [Fact]
    public void EachAxisMeasuresItsOwnNeed()
    {
        // 22 cells = 176 of 1200 = 15%, 3 rows = 54 of 800 = 7%
        Assert.Equal(15, HudLayout.WidthPercent((22, 3), Pane1200x800));
        Assert.Equal(7, HudLayout.HeightPercent((22, 3), Pane1200x800));
        // 20 rows = 360 of 800 = 45%, and the width is unmoved by it
        Assert.Equal(15, HudLayout.WidthPercent((22, 20), Pane1200x800));
        Assert.Equal(45, HudLayout.HeightPercent((22, 20), Pane1200x800));
    }

    [Fact]
    public void AWideMessageDoesNotMakeATallPanel()
    {
        var pane = new PaneMetrics(8, 18, 700, 800);
        var box = HudLayout.Box(new HudSpec(
            "waiting on the package index \u2014 this can take a minute on a cold cache", "registry.example.org"));

        Assert.Equal(71, HudLayout.WidthPercent(box, pane));
        Assert.Equal(14, HudLayout.HeightPercent(box, pane));
    }

    [Fact]
    public void ClampSizePercentBoundsALoosePanelIntoTenToEighty()
    {
        var spec = new HudSpec("x");
        Assert.Equal(HudLayout.MaxSizePercent, HudLayout.ClampSizePercent(100, spec));
        Assert.Equal(HudLayout.MinSizePercent, HudLayout.ClampSizePercent(1, spec));
        Assert.Equal(40, HudLayout.ClampSizePercent(40, spec));
        Assert.Equal(HudLayout.MaxSizePercent, HudLayout.ClampSizePercent(HudLayout.MaxSizePercent, spec));
    }

    [Fact]
    public void OnlyTheWidthTakesTheMinimumFloor()
    {
        var pane = new PaneMetrics(8, 18, 2000, 1000);

        Assert.Equal(HudLayout.MinSizePercent, HudLayout.WidthPercent((6, 3), pane));
        Assert.Equal(6, HudLayout.HeightPercent((6, 3), pane));
    }

    [Fact]
    public void AHugeMessageGrowsTallAndIsCappedThere()
    {
        var box = HudLayout.Box(new HudSpec(string.Join(" ", Enumerable.Repeat("word", 600))));

        Assert.Equal(42, HudLayout.WidthPercent(box, Pane1200x800));
        Assert.Equal(HudLayout.MaxSizePercent, HudLayout.HeightPercent(box, Pane1200x800));
    }

    [Fact]
    public void UnmeasuredPaneWidensButDoesNotHeighten()
    {
        var pane = new PaneMetrics(8, 18, 0, 0);

        Assert.Equal(HudLayout.MaxSizePercent, HudLayout.WidthPercent((10, 3), pane));
        Assert.Equal(HudLayout.MinSizePercent, HudLayout.HeightPercent((10, 3), pane));
    }

    [Fact]
    public void APaddedPaneMeasuresTheContentPlusThePadding()
    {
        var pane = new PaneMetrics(7.8, 15, 1000, 700, 8, 6);
        var box = HudLayout.Box(new HudSpec("gathering options…"));

        Assert.Equal((22, 3), box);
        Assert.Equal(19, HudLayout.WidthPercent(box, pane));
        Assert.Equal(9, HudLayout.HeightPercent(box, pane));
    }

    [Fact]
    public void ACallerOverrideWidensThePanelWithoutHeighteningIt()
    {
        var pane = new PaneMetrics(7.8, 15, 1000, 700, 8, 6);
        var spec = new HudSpec("ok", SizePercent: HudLayout.MaxSizePercent);

        var size = HudLayout.PanelSize(spec, pane);

        Assert.Equal(HudLayout.MaxSizePercent, size.WidthPercent);
        Assert.Equal(9, size.HeightPercent);
        Assert.Equal((6, 3), HudLayout.Box(spec));
    }

    [Theory]
    [InlineData(5, 10)]
    [InlineData(100, 80)]
    public void PanelSizeClampsACallerOverride(int requested, int expected) =>
        Assert.Equal(expected, HudLayout.PanelSize(new HudSpec("ok", SizePercent: requested), Pane1200x800).WidthPercent);

    [Theory]
    [MemberData(nameof(AllPositions))]
    public void AStickyPanelMaySpanThePaneUnlessItIsCentered(HudPosition position)
    {
        var sticky = new HudSpec("caption", Position: position, Sticky: true);
        var loose = new HudSpec("caption", Position: position);
        var widest = position == HudPosition.Center ? HudLayout.MaxSizePercent : 100;

        Assert.Equal(widest, HudLayout.MaxWidthPercent(sticky));
        Assert.Equal(widest, HudLayout.ClampSizePercent(100, sticky));
        Assert.Equal(HudLayout.MaxSizePercent, HudLayout.ClampSizePercent(100, loose));
        Assert.Equal(HudLayout.MinSizePercent, HudLayout.ClampSizePercent(1, sticky));
        Assert.Equal(widest, HudLayout.PanelSize(sticky with { SizePercent = 100 }, Pane1200x800).WidthPercent);
    }

    [Fact]
    public void AMeasuredStickyPanelTakesTheWidthItsTextNeedsUpToThePane()
    {
        var pane = new PaneMetrics(8, 18, 400, 800);
        var message = new string('x', 58);

        Assert.Equal(100, HudLayout.PanelSize(new HudSpec(message, Position: HudPosition.TopCenter, Sticky: true), pane).WidthPercent);
        Assert.Equal(HudLayout.MaxSizePercent, HudLayout.PanelSize(new HudSpec(message, Position: HudPosition.TopCenter), pane).WidthPercent);
    }

    [Theory]
    [InlineData(400.0)]
    [InlineData(800.0)]
    [InlineData(1600.0)]
    public void TheHeightInPointsDoesNotFollowThePane(double paneHeight)
    {
        var pane = new PaneMetrics(8, 18, 1200, paneHeight, 8, 6);

        Assert.Equal(66.0, HudLayout.PanelSize(new HudSpec("gathering options"), pane).HeightPoints);
    }

    [Fact]
    public void HeightPointsAreCappedAtFourFifthsOfThePane()
    {
        var pane = new PaneMetrics(8, 18, 1200, 800, 8, 6);
        var box = HudLayout.Box(new HudSpec(string.Join(" ", Enumerable.Repeat("word", 600))));

        Assert.Equal(640.0, HudLayout.HeightPoints(box, pane));
    }

    [Fact]
    public void AnUnmeasuredPaneHasNoHeightInPoints()
    {
        Assert.Null(HudLayout.PanelSize(new HudSpec("working"), new PaneMetrics(8, 18, 0, 0)).HeightPoints);
        Assert.Null(HudLayout.HeightPoints((10, 3), new PaneMetrics(8, 0, 1200, 800)));
    }

    [Fact]
    public void TheUnmeasuredPaneGetsTheMaxWidthAndATenthOfTheHeight()
    {
        Assert.Equal(new HudPanelSize(80, 10), HudLayout.PanelSize(new HudSpec("working"), PaneMetrics.Unmeasured));
        Assert.Equal(new HudPanelSize(35, 10), HudLayout.PanelSize(new HudSpec("working", SizePercent: 35), PaneMetrics.Unmeasured));
        Assert.Equal(new HudPanelSize(100, 10),
            HudLayout.PanelSize(new HudSpec("working", Position: HudPosition.TopLeft, Sticky: true), PaneMetrics.Unmeasured));
    }

    [Fact]
    public void WidthIsCountedInTheUnitTheHelperCounts()
    {
        var lines = HudLayout.BodyLines(new HudSpec("cafe\u0301 au lait"));

        Assert.Equal(new[] { "caf\u00E9 au lait" }, lines);
        Assert.Equal(12, HudLayout.CellCount(lines[0]));
        Assert.Equal(12 + HudLayout.HorizontalPadding * 2, HudLayout.Box(new HudSpec("cafe\u0301 au lait")).Columns);
    }

    [Fact]
    public void AZwjSequenceIsMeasuredInCodePointsNotClusters()
    {
        var family = "\U0001F468\u200D\U0001F469\u200D\U0001F466";

        Assert.Equal(1, new StringInfo(family).LengthInTextElements);
        Assert.Equal(5, HudLayout.CellCount(family));
        Assert.Equal(5 + HudLayout.HorizontalPadding * 2, HudLayout.Box(new HudSpec(family)).Columns);
    }

    [Fact]
    public void CellCountCountsScalarsNotUtf16Units()
    {
        Assert.Equal(2, "\U0001F98A".Length);
        Assert.Equal(1, HudLayout.CellCount("\U0001F98A"));
    }

    [Fact]
    public void TextLengthCountsTheNfcForm()
    {
        Assert.Equal(2, HudLayout.CellCount("e\u0301"));
        Assert.Equal(1, HudLayout.TextLength("e\u0301"));
        Assert.Equal(5, HudLayout.TextLength("\U0001F468\u200D\U0001F469\u200D\U0001F466"));
    }
}
