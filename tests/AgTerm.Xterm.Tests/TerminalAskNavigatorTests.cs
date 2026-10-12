using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class TerminalAskNavigatorTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    private static PendingAsk Ask(string id = "ASK", string? destructive = "delete") =>
        new(id, "Delete branch?", "It has unmerged commits.",
            new[]
            {
                new ControlAskButton { Id = "keep", Label = "Keep", Hotkey = "k" },
                new ControlAskButton { Id = "delete", Label = "Delete" },
            },
            null, destructive, ControlAskStyle.Terminal, ControlAskAlignment.Center, 60);

    private static TerminalAskPlacement Placement(PendingAsk ask, string? paneId = "right-token") =>
        new(SessionId, ask, paneId is null ? null : OverlayPane.Right, paneId,
            paneId is null ? new[] { "left-token", "right-token" } : new[] { paneId });

    [Fact]
    public void PresentBuildsTheViewWithTerminalLabelsAndTheStartingHighlight()
    {
        var navigator = new TerminalAskNavigator();

        var view = navigator.Present(Placement(Ask()));

        Assert.Equal("ASK", view!.AskId);
        Assert.Equal("right-token", view.PaneId);
        Assert.Equal(new[] { "right-token" }, view.CoveredPaneIds);
        Assert.Equal("Delete branch?", view.Title);
        Assert.Equal("It has unmerged commits.", view.Message);
        Assert.Equal(0, view.Highlighted);
        Assert.Equal("center", view.Align);
        Assert.Equal(60, view.Width);
        Assert.Equal(new[] { new AskLabelRun("K", true), new AskLabelRun("eep", false) }, view.Buttons[0].Runs);
        Assert.False(view.Buttons[0].Destructive);
        Assert.Equal(new[] { new AskLabelRun("! ", false), new AskLabelRun("Delete", false) }, view.Buttons[1].Runs);
        Assert.True(view.Buttons[1].Destructive);
    }

    [Fact]
    public void MovingRedrawsAndKeepsTheHighlightForTheSameAsk()
    {
        var navigator = new TerminalAskNavigator();
        navigator.Present(Placement(Ask()));

        var moved = Assert.IsType<TerminalAskResolution.Redraw>(navigator.Apply("ASK", new AskIntent.Forward()));
        Assert.Equal(1, moved.View.Highlighted);
        Assert.Equal(1, navigator.Present(Placement(Ask()))!.Highlighted);
    }

    [Fact]
    public void AnAnswerResolvesWithTheButtonIdLabelAndIndex()
    {
        var navigator = new TerminalAskNavigator();
        navigator.Present(Placement(Ask()));

        Assert.Equal(new TerminalAskResolution.Resolve(SessionId, "ASK", ControlAskResult.Answered("keep", "Keep", 0)),
            navigator.Apply("ASK", new AskIntent.Hotkey("k")));
        Assert.Equal(new TerminalAskResolution.Resolve(SessionId, "ASK", ControlAskResult.Answered("delete", "Delete", 1)),
            navigator.Apply("ASK", new AskIntent.Press(1)));
    }

    [Fact]
    public void CancelEscapes() =>
        Assert.Equal(new TerminalAskResolution.Resolve(SessionId, "ASK", ControlAskResult.Escaped()),
            Navigated().Apply("ASK", new AskIntent.Cancel()));

    [Fact]
    public void AStaleIdOrAHiddenAskIsIgnored()
    {
        var navigator = Navigated();
        Assert.Equal(new TerminalAskResolution.None(), navigator.Apply("OTHER", new AskIntent.Activate()));

        Assert.Null(navigator.Present(null));
        Assert.Null(navigator.CurrentAskId);
        Assert.Equal(new TerminalAskResolution.None(), navigator.Apply("ASK", new AskIntent.Activate()));
    }

    [Fact]
    public void ANewAskStartsWithItsOwnHighlight()
    {
        var navigator = Navigated();
        navigator.Apply("ASK", new AskIntent.Forward());

        Assert.Equal(0, navigator.Present(Placement(Ask("NEXT")))!.Highlighted);
    }

    [Fact]
    public void ANoMatchHotkeyDoesNothing() =>
        Assert.Equal(new TerminalAskResolution.None(), Navigated().Apply("ASK", new AskIntent.Hotkey("z")));

    private static TerminalAskNavigator Navigated()
    {
        var navigator = new TerminalAskNavigator();
        navigator.Present(Placement(Ask()));
        return navigator;
    }
}
