using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class TerminalHudViewTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    private static TerminalHudPlacement Placement(HudSpec spec, HudPanelSize? size = null, OverlayPane? pane = null,
                                                  string? paneId = null, int revision = 1, double? fontSize = null) =>
        new(SessionId, spec, size ?? new HudPanelSize(80, 10), pane, paneId, revision, fontSize);

    private static readonly string Repositories = string.Join(' ', Enumerable.Repeat("repository", 7));

    [Fact]
    public void APlainMessageIsOneRowOfASessionWidePanelWithTheDefaults()
    {
        var view = TerminalHudViews.From(Placement(new HudSpec("building")));

        Assert.Null(view.PaneId);
        Assert.Equal(new[] { new TerminalHudRow("building", false) }, view.Rows);
        Assert.Equal(80, view.WidthPercent);
        Assert.Null(view.HeightPoints);
        Assert.Equal(10, view.HeightPercent);
        Assert.Equal("center", view.Position);
        Assert.False(view.Sticky);
        Assert.True(view.Frame);
        Assert.Null(view.Background);
        Assert.Null(view.Foreground);
        Assert.Null(view.FontSize);
        Assert.Empty(view.SpinnerFrames);
        Assert.Equal(0, view.SpinnerInterval);
        Assert.Equal(1, view.Revision);
    }

    [Fact]
    public void TheDetailFollowsOneEmptyRowAndEveryDetailRowIsDim()
    {
        var view = TerminalHudViews.From(Placement(new HudSpec("building index", Detail: Repositories)));

        Assert.Equal(new[]
        {
            new TerminalHudRow("building index", false),
            new TerminalHudRow("", false),
            new TerminalHudRow("repository repository repository repository repository", true),
            new TerminalHudRow("repository repository", true),
        }, view.Rows);
    }

    [Fact]
    public void AnEmptyDetailAddsNoRows() =>
        Assert.Equal(new[] { new TerminalHudRow("building", false) },
            TerminalHudViews.From(Placement(new HudSpec("building", Detail: ""))).Rows);

    // plain text always wraps at 60 columns, whatever the width, so the rows are the store's own lines
    [Fact]
    public void ALongMessageWrapsAtSixtyColumnsWhateverTheWidth()
    {
        var spec = new HudSpec(Repositories, SizePercent: 100, Sticky: true, Position: HudPosition.TopCenter);

        var view = TerminalHudViews.From(Placement(spec, new HudPanelSize(100, 10)));

        Assert.Equal(new[] { "repository repository repository repository repository", "repository repository" },
            view.Rows.Select(row => row.Text));
        Assert.Equal(HudLayout.BodyLines(spec), view.Rows.Select(row => row.Text));
        Assert.All(view.Rows, row => Assert.False(row.Dim));
    }

    [Fact]
    public void EveryPanelFieldReachesTheView()
    {
        var spec = new HudSpec("done", Spinner: HudSpinner.Braille, BackgroundColor: "#2a1a3a", TextColor: "#e0e0e0",
            Position: HudPosition.TopCenter, FontSize: 18, Sticky: true, Frame: false);

        var view = TerminalHudViews.From(Placement(spec, new HudPanelSize(35, 12, 132.5), OverlayPane.Right, "right-token", 7, 18));

        Assert.Equal("right-token", view.PaneId);
        Assert.Equal(35, view.WidthPercent);
        Assert.Equal(132.5, view.HeightPoints);
        Assert.Equal(12, view.HeightPercent);
        Assert.Equal("top-center", view.Position);
        Assert.True(view.Sticky);
        Assert.False(view.Frame);
        Assert.Equal("#2a1a3a", view.Background);
        Assert.Equal("#e0e0e0", view.Foreground);
        Assert.Equal(18, view.FontSize);
        Assert.Equal(new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" }, view.SpinnerFrames);
        Assert.Equal(0.08, view.SpinnerInterval);
        Assert.Equal(7, view.Revision);
    }

    // the store fixes the font at open, from the spec or the session; the spec alone does not say it
    [Fact]
    public void TheFontComesFromThePlacementNotTheSpec()
    {
        Assert.Equal(15, TerminalHudViews.From(Placement(new HudSpec("done"), fontSize: 15)).FontSize);
        Assert.Null(TerminalHudViews.From(Placement(new HudSpec("done", FontSize: 18))).FontSize);
    }

    // the wire and tree keep a color without '#' as sent; CSS cannot use it that way
    [Fact]
    public void AColorWithoutAHashGainsOneForThePage()
    {
        var view = TerminalHudViews.From(Placement(new HudSpec("done", BackgroundColor: "2a1a3a", TextColor: "E0E0E0")));
        Assert.Equal("#2a1a3a", view.Background);
        Assert.Equal("#E0E0E0", view.Foreground);
    }

    [Theory]
    [InlineData(HudSpinner.Bar, "|/-\\", 0.1)]
    [InlineData(HudSpinner.Braille, "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏", 0.08)]
    [InlineData(HudSpinner.Circle, "◐◓◑◒", 0.12)]
    [InlineData(HudSpinner.Blocks, "▁▃▄▅▆▇▆▅▄▃", 0.08)]
    [InlineData(HudSpinner.Dot, "● ", 0.45)]
    public void EverySpinnerCarriesItsFramesAndInterval(HudSpinner spinner, string frames, double interval)
    {
        var view = TerminalHudViews.From(Placement(new HudSpec("working", Spinner: spinner)));
        Assert.Equal(frames.Select(frame => frame.ToString()), view.SpinnerFrames);
        Assert.Equal(interval, view.SpinnerInterval);
    }
}
