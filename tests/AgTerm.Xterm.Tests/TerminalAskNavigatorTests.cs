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

    private static PendingAsk InputAsk(string id = "ASK", ControlAskInputMode mode = ControlAskInputMode.Line) =>
        new(id, "Name", null,
            new[] { new ControlAskButton { Id = "ok", Label = "OK" }, new ControlAskButton { Id = "cancel", Label = "Cancel" } },
            "ok", null, ControlAskStyle.Terminal, ControlAskAlignment.Right, null,
            Form: new AskForm.Input(new AskInputForm(mode, "prefill", "hint")), CancelId: "cancel");

    private static TerminalAskPlacement InputPlacement(PendingAsk ask, string? draft) =>
        Placement(ask) with { Draft = draft is null ? null : new AskDraft(ask.Form) { Text = draft } };

    private static PendingAsk ChoiceAsk(string id = "ASK", bool multiple = false, IReadOnlyList<string>? check = null) =>
        new(id, "Pick", null,
            new[] { new ControlAskButton { Id = "ok", Label = "OK" }, new ControlAskButton { Id = "cancel", Label = "Cancel" } },
            "ok", null, ControlAskStyle.Terminal, ControlAskAlignment.Right, null,
            Form: new AskForm.Choices(new AskChoiceForm(
                new[]
                {
                    new ControlAskChoice { Id = "a", Label = "Alpha", Subtitle = "first" },
                    new ControlAskChoice { Id = "b", Label = "Beta" },
                    new ControlAskChoice { Id = "c", Label = "Gamma" },
                },
                multiple, check)),
            CancelId: "cancel");

    [Fact]
    public void AChoiceAskShowsItsListWithTheCheckedItemsAndTheCursor()
    {
        var view = new TerminalAskNavigator().Present(Placement(ChoiceAsk(multiple: true, check: ["c", "b"])))!;

        Assert.Null(view.Input);
        var list = view.Choices!;
        Assert.True(list.Multiple);
        Assert.Equal(1, list.Cursor);
        Assert.Equal(
            new[]
            {
                new TerminalAskChoiceView("Alpha", "first", false),
                new TerminalAskChoiceView("Beta", null, true),
                new TerminalAskChoiceView("Gamma", null, true),
            },
            list.Items);
        Assert.Null(new TerminalAskNavigator().Present(Placement(Ask()))!.Choices);
    }

    [Fact]
    public void AChoiceAskStartsFromTheSessionDraft()
    {
        var ask = ChoiceAsk();
        var draft = new AskDraft(ask.Form);
        draft.Toggle(2);

        var list = new TerminalAskNavigator().Present(Placement(ask) with { Draft = draft })!.Choices!;

        Assert.Equal(2, list.Cursor);
        Assert.Equal(new[] { false, false, true }, list.Items.Select(item => item.Checked));
    }

    [Fact]
    public void ListIntentsMoveTheCursorAndToggleWithASnapshot()
    {
        var navigator = new TerminalAskNavigator();
        navigator.Present(Placement(ChoiceAsk(multiple: true)));

        var down = Assert.IsType<TerminalAskResolution.Drafted>(navigator.Apply("ASK", new AskIntent.Down()));
        Assert.Equal(1, down.View.Choices!.Cursor);
        Assert.Equal(0, down.View.Highlighted);

        var toggled = Assert.IsType<TerminalAskResolution.Drafted>(navigator.Apply("ASK", new AskIntent.Toggle()));
        Assert.True(toggled.View.Choices!.Items[1].Checked);
        Assert.Equal(["b"], toggled.Draft.Selected);

        var clicked = Assert.IsType<TerminalAskResolution.Drafted>(navigator.Apply("ASK", new AskIntent.ToggleAt(2)));
        Assert.Equal(2, clicked.View.Choices!.Cursor);
        Assert.Equal(["b", "c"], clicked.Draft.Selected);
        Assert.Equal(["b"], toggled.Draft.Selected);

        var up = Assert.IsType<TerminalAskResolution.Drafted>(navigator.Apply("ASK", new AskIntent.Up()));
        Assert.Equal(1, up.Draft.Cursor);
    }

    [Fact]
    public void AMissedRowDoesNothing() =>
        Assert.Equal(new TerminalAskResolution.None(), ChoiceNavigated().Apply("ASK", new AskIntent.ToggleAt(3)));

    [Fact]
    public void WithoutAListUpAndDownMoveTheButtons()
    {
        var navigator = Navigated();

        Assert.Equal(1, Assert.IsType<TerminalAskResolution.Redraw>(navigator.Apply("ASK", new AskIntent.Down())).View.Highlighted);
        Assert.Equal(0, Assert.IsType<TerminalAskResolution.Redraw>(navigator.Apply("ASK", new AskIntent.Up())).View.Highlighted);
        Assert.Equal(new TerminalAskResolution.None(), navigator.Apply("ASK", new AskIntent.Toggle()));
    }

    [Fact]
    public void TheAnswerCarriesTheSelection()
    {
        var navigator = ChoiceNavigated(multiple: true);
        navigator.Apply("ASK", new AskIntent.ToggleAt(0));
        navigator.Apply("ASK", new AskIntent.ToggleAt(2));

        Assert.Equal(new TerminalAskResolution.Resolve(SessionId, "ASK", ControlAskResult.Answered("ok", "OK", 0) with { Selected = ["a", "c"] }),
            navigator.Apply("ASK", new AskIntent.Activate()));
    }

    [Fact]
    public void TheSelectionOutlivesHidingTheSameAsk()
    {
        var navigator = ChoiceNavigated();
        navigator.Apply("ASK", new AskIntent.ToggleAt(1));
        navigator.Present(null);

        Assert.True(navigator.Present(Placement(ChoiceAsk()))!.Choices!.Items[1].Checked);
        Assert.True(navigator.Present(Placement(ChoiceAsk("NEXT")))!.Choices!.Items[0].Checked);
    }

    [Fact]
    public void TheDraftSnapshotIsACopyOfTheDrawnDraft()
    {
        var navigator = new TerminalAskNavigator();
        Assert.Null(navigator.DraftSnapshot());
        navigator.Present(InputPlacement(InputAsk(), "prefill"));
        navigator.UpdateDraft("ASK", "typed");

        var snapshot = navigator.DraftSnapshot()!;
        navigator.UpdateDraft("ASK", "later");

        Assert.Equal("typed", snapshot.Text);
    }

    private static PendingAsk OtherAsk(string id = "ASK", bool multiple = false) =>
        ChoiceAsk(id, multiple) is var ask && ask.Form is AskForm.Choices { List: var list }
            ? ask with { Form = new AskForm.Choices(list with { Other = "another" }) }
            : throw new InvalidOperationException();

    [Fact]
    public void AnOtherRowIsDrawnAfterTheItems()
    {
        var navigator = new TerminalAskNavigator();

        Assert.Equal(new TerminalAskOtherView("another", "", false, false), navigator.Present(Placement(OtherAsk()))!.Choices!.Other);
        Assert.Null(new TerminalAskNavigator().Present(Placement(ChoiceAsk()))!.Choices!.Other);
    }

    [Fact]
    public void TypingInTheOtherRowSelectsItAndClearsTheRadioPick()
    {
        var navigator = new TerminalAskNavigator();
        navigator.Present(Placement(OtherAsk()));
        navigator.Apply("ASK", new AskIntent.ToggleAt(1));

        Assert.True(navigator.UpdateDraft("ASK", "qa"));

        var list = navigator.ListRedraw()!.Choices!;
        Assert.Equal(new TerminalAskOtherView("another", "qa", true, true), list.Other);
        Assert.False(list.Items[1].Checked);
        Assert.Equal(new TerminalAskResolution.Resolve(SessionId, "ASK", ControlAskResult.Answered("ok", "OK", 0) with { Text = "qa", Selected = [] }),
            navigator.Apply("ASK", new AskIntent.Activate()));
    }

    [Fact]
    public void TheOtherRowTakesLineTextOnly()
    {
        var navigator = new TerminalAskNavigator();
        navigator.Present(Placement(OtherAsk()));

        Assert.False(navigator.UpdateDraft("ASK", "a\nb"));
        Assert.False(navigator.UpdateDraft("ASK", new string('x', AskInputForm.MaxTextBytes + 1)));
        Assert.False(new TerminalAskNavigator().UpdateDraft("ASK", "x"));
        Assert.False(ChoiceNavigated().UpdateDraft("ASK", "x"));
        Assert.Equal("", navigator.ListRedraw()!.Choices!.Other!.Text);
    }

    [Fact]
    public void OnlyAListRedrawsAfterAnEdit()
    {
        var navigator = new TerminalAskNavigator();
        Assert.Null(navigator.ListRedraw());
        navigator.Present(InputPlacement(InputAsk(), "prefill"));
        Assert.Null(navigator.ListRedraw());
    }

    [Fact]
    public void EditOtherMovesTheCursorToTheRow()
    {
        var navigator = new TerminalAskNavigator();
        navigator.Present(Placement(OtherAsk()));

        var moved = Assert.IsType<TerminalAskResolution.Drafted>(navigator.Apply("ASK", new AskIntent.EditOther()));
        Assert.Equal(3, moved.View.Choices!.Cursor);
        Assert.Equal(new TerminalAskResolution.None(), ChoiceNavigated().Apply("ASK", new AskIntent.EditOther()));
    }
    private static TerminalAskNavigator ChoiceNavigated(bool multiple = false)
    {
        var navigator = new TerminalAskNavigator();
        navigator.Present(Placement(ChoiceAsk(multiple: multiple)));
        return navigator;
    }

    [Fact]
    public void AnInputAskShowsItsFieldWithTheSessionDraft()
    {
        var view = new TerminalAskNavigator().Present(InputPlacement(InputAsk(mode: ControlAskInputMode.Multiline), "typed"))!;

        Assert.Equal(new TerminalAskInputView("multiline", "typed", "hint"), view.Input);
        Assert.Null(new TerminalAskNavigator().Present(Placement(Ask()))!.Input);
    }

    [Fact]
    public void TheAnswerCarriesTheLatestDraft()
    {
        var navigator = new TerminalAskNavigator();
        navigator.Present(InputPlacement(InputAsk(), "prefill"));

        Assert.True(navigator.UpdateDraft("ASK", "hello"));

        Assert.Equal(new TerminalAskResolution.Resolve(SessionId, "ASK", ControlAskResult.Answered("ok", "OK", 0) with { Text = "hello" }),
            navigator.Apply("ASK", new AskIntent.Activate()));
    }

    [Fact]
    public void TheCancelButtonEscapesWithoutText()
    {
        var navigator = new TerminalAskNavigator();
        navigator.Present(InputPlacement(InputAsk(), "x"));
        var moved = Assert.IsType<TerminalAskResolution.Redraw>(navigator.Apply("ASK", new AskIntent.Forward()));
        Assert.Equal(1, moved.View.Highlighted);

        Assert.Equal(new TerminalAskResolution.Resolve(SessionId, "ASK", ControlAskResult.Escaped()),
            navigator.Apply("ASK", new AskIntent.Activate()));
    }

    [Fact]
    public void ARedrawShowsTheLatestDraft()
    {
        var navigator = new TerminalAskNavigator();
        navigator.Present(InputPlacement(InputAsk(), "prefill"));
        navigator.UpdateDraft("ASK", "typed");

        var moved = Assert.IsType<TerminalAskResolution.Redraw>(navigator.Apply("ASK", new AskIntent.Forward()));
        Assert.Equal("typed", moved.View.Input!.Value);
    }

    // a draft write to the store runs off the UI thread and can land after the dialog comes back
    [Fact]
    public void TheDraftOutlivesHidingTheSameAsk()
    {
        var navigator = new TerminalAskNavigator();
        navigator.Present(InputPlacement(InputAsk(), "prefill"));
        navigator.UpdateDraft("ASK", "newer");
        navigator.Present(null);

        Assert.Equal("newer", navigator.Present(InputPlacement(InputAsk(), "older"))!.Input!.Value);
        Assert.Equal("next", navigator.Present(InputPlacement(InputAsk("NEXT"), "next"))!.Input!.Value);
    }

    [Theory]
    [InlineData(ControlAskInputMode.Line, "a\tb", false)]
    [InlineData(ControlAskInputMode.Line, "a\nb", false)]
    [InlineData(ControlAskInputMode.Multiline, "a\u0001b", false)]
    [InlineData(ControlAskInputMode.Multiline, "a\r\nb", false)]
    [InlineData(ControlAskInputMode.Multiline, "a\u007f", false)]
    [InlineData(ControlAskInputMode.Multiline, "a\tb\nc", true)]
    [InlineData(ControlAskInputMode.Line, "", true)]
    public void ADraftWithAForbiddenControlIsRefused(ControlAskInputMode mode, string text, bool accepted)
    {
        var navigator = new TerminalAskNavigator();
        navigator.Present(InputPlacement(InputAsk(mode: mode), "kept"));

        Assert.Equal(accepted, navigator.UpdateDraft("ASK", text));
        Assert.Equal(accepted ? text : "kept", navigator.Present(InputPlacement(InputAsk(mode: mode), "kept"))!.Input!.Value);
    }

    [Fact]
    public void ADraftPastTheCapIsRefused()
    {
        var navigator = new TerminalAskNavigator();
        navigator.Present(InputPlacement(InputAsk(), "kept"));

        Assert.True(navigator.UpdateDraft("ASK", new string('x', AskInputForm.MaxTextBytes)));
        Assert.False(navigator.UpdateDraft("ASK", new string('x', AskInputForm.MaxTextBytes + 1)));
    }

    [Fact]
    public void ADraftForAStaleOrPlainAskIsRefused()
    {
        var navigator = new TerminalAskNavigator();
        navigator.Present(InputPlacement(InputAsk(), "kept"));
        Assert.False(navigator.UpdateDraft("OTHER", "x"));

        Assert.False(Navigated().UpdateDraft("ASK", "x"));
    }

    [Fact]
    public void TheDrawnQuestionNamesItsSession()
    {
        var navigator = new TerminalAskNavigator();
        Assert.Null(navigator.CurrentSessionId);
        navigator.Present(InputPlacement(InputAsk(), "kept"));
        Assert.Equal(SessionId, navigator.CurrentSessionId);
    }

    private static TerminalAskNavigator Navigated()
    {
        var navigator = new TerminalAskNavigator();
        navigator.Present(Placement(Ask()));
        return navigator;
    }
}
