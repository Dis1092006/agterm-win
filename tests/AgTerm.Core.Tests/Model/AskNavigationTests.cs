using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

// Ported from agtermCore AskTests.swift navigation cases and ControlDispatcherAskTests.swift pane aliases (upstream ed831ab).
public class AskNavigationTests
{
    private static ControlAskButton[] Buttons(int count) =>
        Enumerable.Range(0, count).Select(i => new ControlAskButton { Id = $"button-{i}", Label = $"Button {i}" }).ToArray();

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void WithoutDefaultTheFirstNonDestructiveButtonIsHighlighted(int count)
    {
        var navigation = new AskNavigation(Buttons(count), destructiveId: "button-0");
        Assert.Equal(count == 1 ? 0 : 1, navigation.Highlighted);
        Assert.Equal(count == 1 ? 0 : 1, navigation.Activate());
    }

    [Theory]
    [InlineData(1, "button-0", 0)]
    [InlineData(6, "button-4", 4)]
    public void DefaultSeedsTheHighlight(int count, string defaultId, int expected)
    {
        var navigation = new AskNavigation(Buttons(count), defaultId);
        Assert.Equal(expected, navigation.Highlighted);
        Assert.Equal(expected, navigation.Activate());
    }

    [Fact]
    public void DefaultMatchesACanonicallyEquivalentId()
    {
        var buttons = new[] { new ControlAskButton { Id = "a", Label = "A" }, new ControlAskButton { Id = "café", Label = "B" } };
        Assert.Equal(1, new AskNavigation(buttons, "café").Highlighted);
    }

    [Theory]
    [InlineData(1, new[] { 0, 0 })]
    [InlineData(6, new[] { 1, 2, 3, 4, 5, 0, 1 })]
    public void ForwardAdvancesAndWraps(int count, int[] expected)
    {
        var navigation = new AskNavigation(Buttons(count));
        foreach (var index in expected)
        {
            navigation.MoveForward();
            Assert.Equal(index, navigation.Highlighted);
        }
    }

    [Theory]
    [InlineData(1, new[] { 0, 0 })]
    [InlineData(6, new[] { 5, 4, 3, 2, 1, 0, 5 })]
    public void BackwardWrapsToTheLast(int count, int[] expected)
    {
        var navigation = new AskNavigation(Buttons(count));
        foreach (var index in expected)
        {
            navigation.MoveBackward();
            Assert.Equal(index, navigation.Highlighted);
        }
    }

    [Fact]
    public void NavigationMovesAwayFromTheDefault()
    {
        var navigation = new AskNavigation(Buttons(6), "button-2");
        navigation.MoveForward();
        Assert.Equal(3, navigation.Activate());
        navigation.MoveBackward();
        Assert.Equal(2, navigation.Activate());
    }

    [Theory]
    [InlineData("y", 0)]
    [InlineData("Y", 0)]
    [InlineData("n", 1)]
    [InlineData("N", 1)]
    public void HotkeysIgnoreCaseAndTheHighlight(string letter, int expected)
    {
        var navigation = new AskNavigation(new[]
        {
            new ControlAskButton { Id = "yes", Label = "Yes", Hotkey = "Y" },
            new ControlAskButton { Id = "no", Label = "No", Hotkey = "n" },
        });
        Assert.Equal(expected, navigation.Hotkey(letter));
        Assert.Equal(0, navigation.Highlighted);
    }

    [Fact]
    public void AnUndeclaredHotkeyMatchesNoLabel()
    {
        var navigation = new AskNavigation(new[]
        {
            new ControlAskButton { Id = "yes", Label = "Yes" },
            new ControlAskButton { Id = "no", Label = "No", Hotkey = "n" },
        }, "no");
        Assert.Null(navigation.Hotkey("y"));
        Assert.Null(navigation.Hotkey(""));
        Assert.Equal(1, navigation.Activate());
    }

    [Fact]
    public void TheDestructiveChoiceStaysReachable()
    {
        var navigation = new AskNavigation(new[]
        {
            new ControlAskButton { Id = "cancel", Label = "Cancel" },
            new ControlAskButton { Id = "delete", Label = "Delete", Hotkey = "d" },
        }, destructiveId: "delete");
        Assert.Equal(0, navigation.Activate());
        navigation.MoveBackward();
        Assert.Equal(1, navigation.Activate());
        Assert.Equal(1, navigation.Hotkey("d"));
    }

    [Fact]
    public void AnEmptyListStaysInert()
    {
        var navigation = new AskNavigation(Array.Empty<ControlAskButton>());
        navigation.MoveForward();
        navigation.MoveBackward();
        Assert.Null(navigation.Activate());
        Assert.Null(navigation.Hotkey("a"));
    }

    [Fact]
    public void ApplyTurnsIntentsIntoOutcomes()
    {
        var navigation = new AskNavigation(new[]
        {
            new ControlAskButton { Id = "yes", Label = "Yes", Hotkey = "y" },
            new ControlAskButton { Id = "no", Label = "No" },
        });
        Assert.Equal(new AskIntentOutcome.Moved(1), navigation.Apply(new AskIntent.Forward()));
        Assert.Equal(new AskIntentOutcome.Moved(0), navigation.Apply(new AskIntent.Backward()));
        Assert.Equal(new AskIntentOutcome.Answer(0), navigation.Apply(new AskIntent.Activate()));
        Assert.Equal(new AskIntentOutcome.Answer(0), navigation.Apply(new AskIntent.Hotkey("Y")));
        Assert.Equal(new AskIntentOutcome.None(), navigation.Apply(new AskIntent.Hotkey("q")));
        Assert.Equal(new AskIntentOutcome.Answer(1), navigation.Apply(new AskIntent.Press(1)));
        Assert.Equal(new AskIntentOutcome.None(), navigation.Apply(new AskIntent.Press(2)));
        Assert.Equal(new AskIntentOutcome.Escape(), navigation.Apply(new AskIntent.Cancel()));
    }

    [Theory]
    [InlineData("left", OverlayPane.Left)]
    [InlineData("primary", OverlayPane.Left)]
    [InlineData("top", OverlayPane.Left)]
    [InlineData("right", OverlayPane.Right)]
    [InlineData("split", OverlayPane.Right)]
    [InlineData("bottom", OverlayPane.Right)]
    public void PaneAliasesParseToCanonicalRoles(string raw, OverlayPane expected) =>
        Assert.Equal(expected, OverlayPanes.FromControlName(raw));

    [Theory]
    [InlineData("")]
    [InlineData("scratch")]
    [InlineData("overlay")]
    [InlineData("RIGHT")]
    public void OtherPaneNamesAreRefused(string raw) => Assert.Null(OverlayPanes.FromControlName(raw));

    [Fact]
    public void PaneWireNamesAreCanonical()
    {
        Assert.Equal("left", OverlayPanes.Wire(OverlayPane.Left));
        Assert.Equal("right", OverlayPanes.Wire(OverlayPane.Right));
    }
}
