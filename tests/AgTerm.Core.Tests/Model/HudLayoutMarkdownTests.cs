using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// The grid and markdown cases of agtermCore HudTests.swift (upstream ed831ab). Upstream compares the
// SGR-encoded body lines; the port compares the rows of runs the page draws.
public class HudLayoutMarkdownTests
{
    private static string Text(IReadOnlyList<HudMarkdownRun> row) => string.Concat(row.Select(run => run.Text));

    private static HudMarkdownRun Run(string text, HudMarkdownStyle style = HudMarkdownStyle.None, string? link = null) => new(text, style, link);

    [Fact]
    public void TheGridIsThePanelsOwn()
    {
        var pane = new PaneMetrics(7.8, 15, 1000, 700, 8, 6);
        var spec = new HudSpec("gathering options…");
        var box = HudLayout.Box(spec);
        var width = HudLayout.WidthPercent(box, pane);
        var height = HudLayout.HeightPercent(box, pane);

        var grid = HudLayout.PaintGrid(spec, new HudPanelSize(width, height), pane);

        Assert.Equal(19, width);
        Assert.Equal(9, height);
        Assert.Equal((22, 3), grid);
        Assert.Equal((22, 3), box);
        Assert.Equal(1, (grid.Rows - HudLayout.BodyLines(spec).Count) / 2);
    }

    [Fact]
    public void ACallerOverrideWidensThePanelWithoutHeighteningIt()
    {
        var pane = new PaneMetrics(7.8, 15, 1000, 700, 8, 6);
        var spec = new HudSpec("ok", SizePercent: HudLayout.MaxSizePercent);
        var size = HudLayout.PanelSize(spec, pane);

        var grid = HudLayout.PaintGrid(spec, size, pane);

        Assert.Equal(HudLayout.MaxSizePercent, size.WidthPercent);
        Assert.Equal(9, size.HeightPercent);
        Assert.Equal((100, 3), grid);
        Assert.Equal((6, 3), HudLayout.Box(spec));
    }

    [Fact]
    public void PaintGridFallsBackToTheBoxWithoutAMeasuredPane()
    {
        var spec = new HudSpec("working");
        var unmeasured = new PaneMetrics(8, 18, 0, 0);
        var noCell = new PaneMetrics(0, 0, 1000, 700);
        var tiny = new PaneMetrics(8, 18, 30, 30, 8, 6);

        Assert.Null(HudLayout.PanelGrid(new HudPanelSize(40, 10), unmeasured));
        Assert.Null(HudLayout.PanelGrid(new HudPanelSize(40, 10), noCell));
        Assert.Equal(HudLayout.Box(spec), HudLayout.PaintGrid(spec, new HudPanelSize(80, 10), unmeasured));
        // a pane smaller than its own padding leaves no cells either, rather than a zero or negative grid
        Assert.Null(HudLayout.PanelGrid(new HudPanelSize(40, 40), tiny));
    }

    [Theory]
    [InlineData(400.0)]
    [InlineData(800.0)]
    [InlineData(1600.0)]
    public void TheGridRowsDoNotFollowThePane(double paneHeight)
    {
        var pane = new PaneMetrics(8, 18, 1200, paneHeight, 8, 6);
        var spec = new HudSpec("gathering options");

        Assert.Equal(3, HudLayout.PaintGrid(spec, HudLayout.PanelSize(spec, pane), pane).Rows);
    }

    [Fact]
    public void AMarkdownBodyCarriesItsBlockWidthAndStyledRows()
    {
        var body = HudLayout.MarkdownBody(new HudSpec("**hi** there", Markdown: true), (20, 5));

        Assert.Equal(8, body.BlockWidth);
        Assert.Equal(new[] { Run("hi", HudMarkdownStyle.Bold), Run(" there") }, Assert.Single(body.Rows));
    }

    [Fact]
    public void AMarkdownLinkAddsNoWidthBeyondItsVisibleText()
    {
        var body = HudLayout.MarkdownBody(new HudSpec("[ab](http://x)", Markdown: true), (40, 5));

        Assert.Equal(2, body.BlockWidth);
        Assert.Equal(new[] { Run("ab", HudMarkdownStyle.Underline, "http://x") }, Assert.Single(body.Rows));
    }

    // upstream regression: the height was measured at the caller's raw percent while the panel took the clamped one
    [Theory]
    [InlineData(100)]
    [InlineData(5)]
    public void AMarkdownMessageIsMeasuredAtTheClampedWidthItIsPaintedAt(int percent)
    {
        var pane = new PaneMetrics(8, 18, 576, 2000);
        var spec = new HudSpec(string.Join(' ', Enumerable.Repeat("word", 24)), SizePercent: percent, Markdown: true);

        var measured = HudLayout.PanelSize(spec, pane);
        var taken = new HudPanelSize(HudLayout.ClampSizePercent(percent, spec), measured.HeightPercent);
        var grid = HudLayout.PaintGrid(spec, taken, pane);
        var body = HudLayout.MarkdownBody(spec, grid);

        Assert.DoesNotContain(body.Rows, row => Text(row).Contains("more"));
        Assert.True(grid.Rows - body.Rows.Count - HudLayout.VerticalPadding * 2 <= 1);
    }

    [Fact]
    public void ASpinningMarkdownBodyIndentsEveryRowAfterTheFirstByTheGutter()
    {
        var body = HudLayout.MarkdownBody(new HudSpec("- a\n- bb", Spinner: HudSpinner.Bar, Markdown: true), (20, 6));

        Assert.Equal(new[] { "• a", "  • bb" }, body.Rows.Select(Text));
        Assert.Equal(Run("  "), body.Rows[1][0]);
        Assert.Equal(4 + HudLayout.SpinnerWidth, body.BlockWidth);
    }

    [Fact]
    public void AMarkdownBodyWrapsAtTheGridLessItsPaddingAndCountsWhatDoesNotFit()
    {
        var message = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"- item {i}"));

        var body = HudLayout.MarkdownBody(new HudSpec(message, Markdown: true), (9, 5));

        Assert.Equal(new[] { "• ite", "  m 1", "… 19…" }, body.Rows.Select(Text));
        Assert.Equal(new[] { Run("… 19", HudMarkdownStyle.Dim), Run("…") }, body.Rows[2]);
        Assert.Equal(5, body.BlockWidth);
    }

    [Fact]
    public void AMarkdownTableWiderThanThePanelIsStillClipped() =>
        Assert.Equal("┌────…", Text(HudLayout.MarkdownBody(new HudSpec("| abcdef |\n|---|\n| x |", Markdown: true), (10, 9)).Rows[0]));

    [Fact]
    public void AMarkdownMessageIsMeasuredAndWrappedAtTheWidthThePaneAllows()
    {
        var pane = new PaneMetrics(8, 18, 400, 800);
        var spec = new HudSpec("aaaa bbbb cccc dddd eeee ffff gggg [the link](http://x) hhhh iiii jjjj", Markdown: true);

        var size = HudLayout.PanelSize(spec, pane);
        var grid = HudLayout.PaintGrid(spec, size, pane);
        var body = HudLayout.MarkdownBody(spec, grid);

        Assert.Equal(38, grid.Columns);
        Assert.Equal(2, body.Rows.Count);
        Assert.Equal(new[] { Run("aaaa bbbb cccc dddd eeee ffff gggg") }, body.Rows[0]);
        Assert.Equal(new[] { Run("the link", HudMarkdownStyle.Underline, "http://x"), Run(" hhhh iiii jjjj") }, body.Rows[1]);
        Assert.Equal(body.Rows.Count + HudLayout.VerticalPadding * 2, grid.Rows);
    }

    [Fact]
    public void AOneRowSpinningMarkdownBodyShowsOnlyTheMarker()
    {
        var body = HudLayout.MarkdownBody(new HudSpec("a\n\nb", Spinner: HudSpinner.Bar, Markdown: true), (30, 3));

        Assert.Equal(new[] { Run("… 3 more", HudMarkdownStyle.Dim) }, Assert.Single(body.Rows));
        Assert.Equal(8 + HudLayout.SpinnerWidth, body.BlockWidth);
    }

    [Fact]
    public void AnEmptyMarkdownBudgetStillHasABlockWidth()
    {
        var body = HudLayout.MarkdownBody(new HudSpec("a", Markdown: true), (4, 2));

        Assert.Empty(body.Rows);
        Assert.Equal(1, body.BlockWidth);
    }

    [Fact]
    public void AMarkdownDetailFollowsABlankRowDimmed()
    {
        var body = HudLayout.MarkdownBody(new HudSpec("x", Detail: "d", Markdown: true), (20, 10));

        Assert.Equal(new[] { "x", "", "d" }, body.Rows.Select(Text));
        Assert.Equal(new[] { Run("d", HudMarkdownStyle.Dim) }, body.Rows[2]);
    }

    [Fact]
    public void TheMarkdownBoxMeasuresTheRenderedRows() =>
        Assert.Equal((3 + HudLayout.HorizontalPadding * 2, 4 + HudLayout.VerticalPadding * 2),
            HudLayout.Box(new HudSpec("# T\n\n- a\n- b", Markdown: true)));

    // upstream regression: markdown wrapped at 60 columns however wide the caller made the panel
    [Fact]
    public void AMarkdownMessageWithACallerWidthWrapsAtThatWidth()
    {
        var pane = new PaneMetrics(8, 18, 1600, 800);
        var line = string.Join(' ', Enumerable.Repeat("word", 20));
        var sized = new HudSpec(line, SizePercent: 80, Markdown: true);
        var unsized = new HudSpec(line, Markdown: true);

        var body = HudLayout.MarkdownBody(sized, HudLayout.PaintGrid(sized, HudLayout.PanelSize(sized, pane), pane));

        Assert.Equal(new[] { line }, body.Rows.Select(Text));
        Assert.Equal(2 + HudLayout.VerticalPadding * 2, HudLayout.Box(unsized, HudLayout.WrapColumns(unsized, pane)).Rows);
    }

    [Fact]
    public void APlainMessageAlwaysWrapsAtSixty()
    {
        var pane = new PaneMetrics(8, 18, 1600, 800);

        Assert.Equal(HudLayout.MaxColumns, HudLayout.WrapColumns(new HudSpec("x", SizePercent: 80), pane));
        Assert.Equal(HudLayout.MaxColumns, HudLayout.WrapColumns(new HudSpec("x", Markdown: true), PaneMetrics.Unmeasured));
    }

    [Fact]
    public void AFramelessPanelIsAsTallAsItsText()
    {
        var pane = new PaneMetrics(8, 18, 1200, 800, 8, 6);
        var spec = new HudSpec("- a\n- b", Markdown: true, Frame: false);

        var size = HudLayout.PanelSize(spec, pane);
        var grid = HudLayout.PaintGrid(spec, size, pane);

        Assert.Equal(2, HudLayout.Box(spec).Rows);
        Assert.Equal(1, HudLayout.Box(new HudSpec("one line", Frame: false)).Rows);
        Assert.Equal(48.0, size.HeightPoints);
        Assert.Equal(2, grid.Rows);
        Assert.Equal(new[] { "• a", "• b" }, HudLayout.MarkdownBody(spec, grid).Rows.Select(Text));
    }

    [Theory]
    [InlineData(15.4)]
    [InlineData(17.3)]
    [InlineData(19.7)]
    public void AFractionalCellStillYieldsTheWholeRowsThePointsWereBuiltFrom(double cellHeight)
    {
        var pane = new PaneMetrics(7.8, cellHeight, 1200, 900, 8, 6);
        var spec = new HudSpec("- a\n- b\n- c", Markdown: true, Frame: false);

        Assert.Equal(3, HudLayout.PaintGrid(spec, HudLayout.PanelSize(spec, pane), pane).Rows);
    }

    [Fact]
    public void ContentPastTheHeightCapAgreesOnPointsGridBodyAndPercent()
    {
        var pane = new PaneMetrics(8, 18, 1200, 800, 8, 6);
        var spec = new HudSpec(string.Join('\n', Enumerable.Range(1, 80).Select(i => $"- item {i}")), Markdown: true);

        var size = HudLayout.PanelSize(spec, pane);
        var grid = HudLayout.PaintGrid(spec, size, pane);
        var body = HudLayout.MarkdownBody(spec, grid);

        Assert.Equal(640, size.HeightPoints);
        Assert.Equal(HudLayout.MaxSizePercent, size.HeightPercent);
        Assert.Equal((int)((640.0 - 12) / 18), grid.Rows);
        Assert.Equal(grid.Rows - HudLayout.VerticalPadding * 2, body.Rows.Count);
        Assert.Contains("more", Text(body.Rows[^1]));
    }
}
