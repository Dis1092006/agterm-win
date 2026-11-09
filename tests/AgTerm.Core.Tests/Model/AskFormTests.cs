using System.Text;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// Ported from agtermCore AskTests.swift input-form and choice-list cases (upstream 96b0a6c6).
public class AskFormTests
{
    private static readonly Guid WindowId = Guid.NewGuid();

    private static readonly ControlAskButton[] FormButtons =
    {
        new() { Id = "ok", Label = "OK" },
        new() { Id = "cancel", Label = "Cancel" },
    };

    private static PendingAsk Ask(AskForm? form = null, string? cancelId = null, string id = "a",
        ControlAskStyle style = ControlAskStyle.Terminal) =>
        new(id, "Name", null, FormButtons, null, null, style, ControlAskAlignment.Right, null,
            Form: form, CancelId: cancelId);

    private static AskForm.Input Input(ControlAskInputMode mode, string value = "", string? placeholder = null) =>
        new(new AskInputForm(mode, value, placeholder));

    private static ControlAskChoice[] Items(params string[] ids) =>
        ids.Select(id => new ControlAskChoice { Id = id, Label = id.ToUpperInvariant() }).ToArray();

    private static AskForm.Choices Choices(ControlAskChoice[] items, bool multiple = false, params string[] checkedIds) =>
        new(new AskChoiceForm(items, multiple, checkedIds));

    [Fact]
    public void InputAnswerCarriesTheDraftTextAndCancelEscapesWithoutIt()
    {
        var ask = Ask(Input(ControlAskInputMode.Multiline, "draft"), "cancel");
        var draft = new AskDraft(ask.Form);
        Assert.Equal("draft", draft.Text);

        draft.Text = "  line one\nline two  ";

        Assert.Equal(ControlAskResult.Answered("ok", "OK", 0) with { Text = "  line one\nline two  " }, ask.Answer(0, draft));
        Assert.Equal(ControlAskResult.Escaped(), ask.Answer(1, draft));
        Assert.Null(ask.Answer(2, draft));
        Assert.Null(ask.Answer(-1, draft));
    }

    [Fact]
    public void AnswerWithoutADraftUsesTheFormsInitialValue() =>
        Assert.Equal("x", Ask(Input(ControlAskInputMode.Line, "x")).Answer(0, null)!.Text);

    [Fact]
    public void ChoiceAnswerListsCheckedIdsInCallerOrder()
    {
        var form = Choices(Items("a", "b", "c"), multiple: true, "c");
        var ask = Ask(form, "cancel");
        var draft = new AskDraft(form);
        draft.Toggle(0);
        Assert.Equal(["a", "c"], draft.Selected);
        Assert.Equal(ControlAskResult.Answered("ok", "OK", 0) with { Selected = ["a", "c"] }, ask.Answer(0, draft));
        Assert.Null(ask.Answer(0, draft)!.Text);
        Assert.Equal(ControlAskResult.Escaped(), ask.Answer(1, draft));
    }

    [Fact]
    public void AnswerWithoutADraftUsesTheFormsInitialChoices() =>
        Assert.Equal(["a"], Ask(Choices(Items("a", "b"))).Answer(0, null)!.Selected);

    [Fact]
    public void RadioDraftStartsOnTheFirstItemAndPicksExactlyOne()
    {
        var draft = new AskDraft(Choices(Items("a", "b", "c")));
        Assert.Equal(["a"], draft.Selected);
        Assert.Equal(0, draft.Cursor);
        draft.MoveCursor(2);
        draft.ToggleCursor();
        Assert.Equal(["c"], draft.Selected);
        draft.ToggleCursor();
        Assert.Equal(["c"], draft.Selected);
    }

    [Fact]
    public void RadioDraftStartsOnItsCheckedItem()
    {
        var draft = new AskDraft(Choices(Items("a", "b", "c"), multiple: false, "b"));
        Assert.Equal(["b"], draft.Selected);
        Assert.Equal(1, draft.Cursor);
    }

    [Fact]
    public void DraftCursorStartsOnTheFirstCheckedItemAndStopsAtTheEnds()
    {
        var draft = new AskDraft(Choices(Items("a", "b", "c"), multiple: true, "c", "b"));
        Assert.Equal(1, draft.Cursor);
        draft.MoveCursor(-5);
        Assert.Equal(0, draft.Cursor);
        draft.MoveCursor(9);
        Assert.Equal(2, draft.Cursor);
    }

    [Fact]
    public void CheckboxDraftTogglesAndMayEndEmpty()
    {
        var draft = new AskDraft(Choices(Items("a", "b"), multiple: true, "b"));
        draft.Toggle(1);
        Assert.Empty(draft.Selected);
        draft.Toggle(7);
        draft.Toggle(-1);
        Assert.Empty(draft.Selected);
        Assert.Equal(1, draft.Cursor);
    }

    [Fact]
    public void ADraftWithoutAListIgnoresCursorMoves()
    {
        var draft = new AskDraft(Input(ControlAskInputMode.Line));
        draft.MoveCursor(1);
        draft.ToggleCursor();
        Assert.Equal(0, draft.Cursor);
        Assert.Empty(draft.Selected);
    }

    [Fact]
    public void CheckedIdsMatchCanonicallyEquivalentChoiceIds()
    {
        var draft = new AskDraft(Choices(Items("caf\u00E9", "b"), multiple: true, "cafe\u0301"));
        Assert.Equal(["caf\u00E9"], draft.Selected);
    }

    [Fact]
    public void ChoiceFormsCompareByTheirItems()
    {
        Assert.Equal(Choices(Items("a", "b"), multiple: true, "a"), Choices(Items("a", "b"), multiple: true, "a"));
        Assert.NotEqual(Choices(Items("a", "b")), Choices(Items("a", "c")));
        Assert.NotEqual(Choices(Items("a", "b"), multiple: true, "a"), Choices(Items("a", "b"), multiple: true, "b"));
    }

    [Fact]
    public void CancelRoleAppliesWithoutAForm()
    {
        var ask = Ask(cancelId: "cancel");
        Assert.Equal(ControlAskResult.Escaped(), ask.Answer(1, null));
        Assert.Equal(ControlAskResult.Answered("ok", "OK", 0), ask.Answer(0, null));
    }

    [Fact]
    public void CancelRoleMatchesCanonicallyEquivalentIds()
    {
        var buttons = new ControlAskButton[] { new() { Id = "caf\u00E9", Label = "Cafe" } };
        var ask = new PendingAsk("a", "T", null, buttons, null, null, ControlAskStyle.Terminal, ControlAskAlignment.Right, null,
            CancelId: "cafe\u0301");
        Assert.Equal(ControlAskResult.Escaped(), ask.Answer(0, null));
    }

    [Theory]
    [InlineData(AskInputForm.MaxTextBytes, AskInputForm.MaxTextBytes)]
    [InlineData(AskInputForm.MaxTextBytes + 1, AskInputForm.MaxTextBytes)]
    public void DraftTextIsClampedToTheByteCap(int length, int expected)
    {
        var draft = new AskDraft(Input(ControlAskInputMode.Line));
        draft.Text = new string('x', length);
        Assert.Equal(expected, Encoding.UTF8.GetByteCount(draft.Text));
    }

    [Fact]
    public void ADraftStartsFromAClampedPrefill()
    {
        var draft = new AskDraft(Input(ControlAskInputMode.Line, new string('x', AskInputForm.MaxTextBytes + 5)));
        Assert.Equal(AskInputForm.MaxTextBytes, draft.Text.Length);
    }

    [Fact]
    public void ClampingNeverSplitsAMultibyteCharacter()
    {
        var text = string.Concat(Enumerable.Repeat("\u00E9", AskInputForm.MaxTextBytes / 2 + 1));
        var clamped = AskInputForm.Clamped(text);
        Assert.Equal(AskInputForm.MaxTextBytes, Encoding.UTF8.GetByteCount(clamped));
        Assert.All(clamped, c => Assert.Equal('\u00E9', c));
    }

    [Fact]
    public void ClampingNeverSplitsASurrogatePairOrACombiningSequence()
    {
        var pairs = new string('x', AskInputForm.MaxTextBytes - 2) + "\U0001F600";
        Assert.Equal(new string('x', AskInputForm.MaxTextBytes - 2), AskInputForm.Clamped(pairs));

        var combined = new string('x', AskInputForm.MaxTextBytes - 1) + "e\u0301";
        Assert.Equal(new string('x', AskInputForm.MaxTextBytes - 1), AskInputForm.Clamped(combined));
    }

    [Theory]
    [InlineData("plain \u00E9 \"q\"", ControlAskInputMode.Line, false)]
    [InlineData("a\nb\tc", ControlAskInputMode.Multiline, false)]
    [InlineData("a\nb", ControlAskInputMode.Line, true)]
    [InlineData("a\tb", ControlAskInputMode.Line, true)]
    [InlineData("a\u0001b", ControlAskInputMode.Multiline, true)]
    [InlineData("a\rb", ControlAskInputMode.Multiline, true)]
    [InlineData("a\u007F", ControlAskInputMode.Multiline, true)]
    [InlineData("a\u001B[31m", ControlAskInputMode.Line, true)]
    public void ControlCharactersAreForbiddenExceptNewlineAndTabInMultiline(string text, ControlAskInputMode mode, bool forbidden) =>
        Assert.Equal(forbidden, AskInputForm.HasForbiddenControls(text, mode));

    [Fact]
    public void ASessionKeepsTheDraftUntilResolutionAndIgnoresStaleIds()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        var ask = Ask(Input(ControlAskInputMode.Line, "x"), "cancel");
        Assert.True(store.OpenAsk(session.Id, ask, null, WindowId));
        Assert.Equal("x", session.AskDraft!.Text);

        Assert.True(store.UpdateAskDraft(session.Id, "a", draft => draft.Text = "typed"));
        Assert.False(store.UpdateAskDraft(session.Id, "stale", draft => draft.Text = "lost"));
        Assert.Equal("typed", session.AskDraft!.Text);

        Assert.False(store.AnswerAsk(session.Id, "stale", 0));
        Assert.False(store.AnswerAsk(session.Id, "a", 2));
        Assert.True(store.AnswerAsk(session.Id, "a", 0));

        Assert.Null(session.AskDraft);
        Assert.Null(session.AskPending);
        Assert.Equal(ControlAskResult.Answered("ok", "OK", 0) with { Text = "typed" }, store.Asks.Find("a")!.Result);
    }

    [Fact]
    public void ASessionAnswerCarriesTheDraftsSelection()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        store.OpenAsk(session.Id, Ask(Choices(Items("a", "b"), multiple: true), "cancel"), null, WindowId);

        Assert.True(store.UpdateAskDraft(session.Id, "a", draft => draft.Toggle(1)));
        Assert.True(store.AnswerAsk(session.Id, "a", 0));

        Assert.Equal(ControlAskResult.Answered("ok", "OK", 0) with { Selected = ["b"] }, store.Asks.Find("a")!.Result);
    }

    [Fact]
    public void ASessionCancelButtonAnswerEscapes()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        store.OpenAsk(session.Id, Ask(Input(ControlAskInputMode.Line), "cancel"), null, WindowId);

        Assert.True(store.AnswerAsk(session.Id, "a", 1));

        Assert.Equal(ControlAskResult.Escaped(), store.Asks.Find("a")!.Result);
    }

    [Fact]
    public void ReleasingASessionAskDropsItsDraft()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        store.OpenAsk(session.Id, Ask(Input(ControlAskInputMode.Line)), null, WindowId);

        store.CancelAllAsks();

        Assert.Null(session.AskDraft);
    }

    [Fact]
    public void AWindowSlotKeepsTheDraftUntilResolutionAndIgnoresStaleIds()
    {
        var controller = new PickController();
        Assert.True(controller.OpenAsk(Ask(Input(ControlAskInputMode.Line, "x"), "cancel", style: ControlAskStyle.Gui), WindowId));
        Assert.Equal("x", controller.AskDraft!.Text);

        Assert.True(controller.UpdateAskDraft("a", draft => draft.Text = "typed"));
        Assert.False(controller.UpdateAskDraft("stale", draft => draft.Text = "lost"));
        Assert.Equal("typed", controller.AskDraft!.Text);

        Assert.False(controller.AnswerAsk("stale", 0));
        Assert.True(controller.AnswerAsk("a", 0));

        Assert.Null(controller.AskDraft);
        Assert.Null(controller.PendingAsk);
        Assert.Equal(ControlAskResult.Answered("ok", "OK", 0) with { Text = "typed" }, controller.Asks.Find("a")!.Result);
    }

    [Fact]
    public void AWindowSlotDropsTheDraftOnEscapeAndCancel()
    {
        var controller = new PickController();
        controller.OpenAsk(Ask(Input(ControlAskInputMode.Line), id: "a", style: ControlAskStyle.Gui), WindowId);
        controller.EscapeAsk("a");
        Assert.Null(controller.AskDraft);

        controller.OpenAsk(Ask(Input(ControlAskInputMode.Line), id: "b", style: ControlAskStyle.Gui), WindowId);
        controller.CancelAsk("b");
        Assert.Null(controller.AskDraft);
    }

    [Fact]
    public void AnchoringKeepsTheFormAndCancelRole()
    {
        var form = Choices(Items("a"));
        var anchored = Ask(form, "cancel") with { Anchor = new AskAnchor(Guid.NewGuid()) };
        Assert.Equal(form, anchored.Form);
        Assert.Equal("cancel", anchored.CancelId);
    }

    // upstream AskDialogView.handle: with a list Up and Down move its cursor and Space toggles it
    [Fact]
    public void AListTakesTheCursorAndToggleIntents()
    {
        var draft = new AskDraft(Choices(Items("a", "b", "c"), multiple: true));

        Assert.True(draft.Apply(new AskIntent.Down()));
        Assert.Equal(1, draft.Cursor);
        Assert.True(draft.Apply(new AskIntent.Toggle()));
        Assert.Equal(["b"], draft.Selected);
        Assert.True(draft.Apply(new AskIntent.Up()));
        Assert.Equal(0, draft.Cursor);
        Assert.True(draft.Apply(new AskIntent.ToggleAt(2)));
        Assert.Equal(2, draft.Cursor);
        Assert.Equal(["b", "c"], draft.Selected);
    }

    [Fact]
    public void AListLeavesButtonIntentsAndAMissedRowAlone()
    {
        var draft = new AskDraft(Choices(Items("a", "b")));

        foreach (AskIntent intent in new AskIntent[] { new AskIntent.Forward(), new AskIntent.Backward(), new AskIntent.Activate(),
                     new AskIntent.Cancel(), new AskIntent.Hotkey("a"), new AskIntent.Press(0), new AskIntent.ToggleAt(2), new AskIntent.ToggleAt(-1) })
            Assert.False(draft.Apply(intent));
        Assert.Equal(["a"], draft.Selected);
    }

    [Fact]
    public void ADraftWithoutAListLeavesTheListIntentsToTheButtons()
    {
        var draft = new AskDraft(Input(ControlAskInputMode.Line));

        foreach (AskIntent intent in new AskIntent[] { new AskIntent.Up(), new AskIntent.Down(), new AskIntent.Toggle(), new AskIntent.ToggleAt(0) })
            Assert.False(draft.Apply(intent));
    }

    [Fact]
    public void IsCheckedReadsAnItemByIndex()
    {
        var draft = new AskDraft(Choices(Items("a", "b", "c"), true, "c", "a"));

        Assert.Equal(new[] { true, false, true }, Enumerable.Range(0, 3).Select(draft.IsChecked));
        Assert.False(draft.IsChecked(3));
        Assert.False(draft.IsChecked(-1));
    }

    [Fact]
    public void ACopyIsIndependentAndCopyFromRestoresIt()
    {
        var draft = new AskDraft(Choices(Items("a", "b", "c"), multiple: true));
        draft.Toggle(2);
        draft.Text = "t";

        var copy = draft.Copy();
        draft.Toggle(0);

        Assert.Equal(["c"], copy.Selected);
        Assert.Equal(2, copy.Cursor);
        Assert.Equal("t", copy.Text);

        draft.CopyFrom(copy);
        Assert.Equal(["c"], draft.Selected);
        Assert.Equal(2, draft.Cursor);
        Assert.Equal("t", draft.Text);
    }

    private static AskForm.Choices OtherForm(bool multiple, params string[] checkedIds) =>
        new(new AskChoiceForm(Items("a", "b", "c"), multiple, checkedIds, "Something else"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypingSelectsTheOtherRowAndBlankTextDisablesIt(bool multiple)
    {
        var draft = new AskDraft(OtherForm(multiple));
        Assert.False(draft.OtherEnabled);
        draft.Text = " ";
        Assert.False(draft.OtherEnabled || draft.OtherSelected);
        Assert.Null(draft.OtherText);
        draft.Text = " x";
        Assert.True(draft.OtherEnabled && draft.OtherSelected);
        Assert.Equal(" x", draft.OtherText);
        draft.Text = "  ";
        Assert.False(draft.OtherEnabled || draft.OtherSelected);
        Assert.Null(draft.OtherText);
        Assert.Equal("  ", draft.Text);
    }

    [Fact]
    public void RadioOtherRestoresThePickItTookOverFrom()
    {
        var draft = new AskDraft(OtherForm(false, "b"));
        draft.Text = "x";
        Assert.Empty(draft.Selected);
        Assert.True(draft.OtherSelected);
        draft.Text = "xyz";
        draft.Text = "";
        Assert.Equal(["b"], draft.Selected);
        Assert.False(draft.OtherSelected);
    }

    [Fact]
    public void RadioPickElsewhereKeepsOtherTextAndClearingItKeepsThatPick()
    {
        var draft = new AskDraft(OtherForm(false, "b"));
        draft.Text = "x";
        draft.Toggle(2);
        Assert.Equal(["c"], draft.Selected);
        Assert.False(draft.OtherSelected);
        Assert.Equal("x", draft.Text);
        Assert.True(draft.OtherEnabled);
        draft.Text = "";
        Assert.Equal(["c"], draft.Selected);
        draft.Text = "y";
        Assert.Empty(draft.Selected);
        Assert.True(draft.OtherSelected);
        draft.Text = "";
        Assert.Equal(["c"], draft.Selected);
    }

    [Fact]
    public void RadioOtherWithoutAnInitialPickFallsBackToTheFirstItem()
    {
        var draft = new AskDraft(OtherForm(false));
        draft.Toggle(3);
        Assert.False(draft.OtherSelected);
        draft.Text = "x";
        draft.Text = "";
        Assert.Equal(["a"], draft.Selected);
    }

    [Fact]
    public void RadioOtherMarkerPicksAnEnabledRowAndIsANoOpOncePicked()
    {
        var draft = new AskDraft(OtherForm(false, "b"));
        draft.Text = "x";
        draft.Toggle(0);
        draft.Toggle(3);
        Assert.True(draft.OtherSelected);
        Assert.Empty(draft.Selected);
        draft.Toggle(3);
        Assert.True(draft.OtherSelected);
        Assert.Empty(draft.Selected);
        draft.Text = "";
        Assert.Equal(["a"], draft.Selected);
    }

    [Fact]
    public void CheckboxOtherMarkerTogglesWhileEnabledAndKeepsTheText()
    {
        var draft = new AskDraft(OtherForm(true, "a"));
        draft.Toggle(3);
        Assert.False(draft.OtherSelected);
        draft.Text = "x";
        draft.Toggle(3);
        Assert.False(draft.OtherSelected);
        Assert.True(draft.OtherEnabled);
        Assert.Equal("x", draft.Text);
        Assert.Null(draft.OtherText);
        Assert.Equal(["a"], draft.Selected);
        draft.Text = "xy";
        Assert.True(draft.OtherSelected);
        Assert.Equal("xy", draft.OtherText);
    }

    [Fact]
    public void CursorReachesTheOtherRowAfterFiftyItemsAndClampsThere()
    {
        var items = Enumerable.Range(1, ControlAskChoice.MaxChoices).Select(i => new ControlAskChoice { Id = $"i{i}", Label = $"I{i}" }).ToArray();
        var draft = new AskDraft(new AskForm.Choices(new AskChoiceForm(items, true, null, "Other")));
        draft.MoveCursor(100);
        Assert.Equal(ControlAskChoice.MaxChoices, draft.Cursor);
        Assert.True(draft.CursorOnOther);
        draft.MoveCursor(1);
        Assert.Equal(ControlAskChoice.MaxChoices, draft.Cursor);
        draft.MoveCursor(-1);
        Assert.False(draft.CursorOnOther);

        var plain = new AskDraft(Choices(Items("a", "b")));
        plain.MoveCursor(5);
        Assert.Equal(1, plain.Cursor);
        Assert.False(plain.CursorOnOther);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OtherAnswerReturnsRawTextOnlyWhileSelected(bool multiple)
    {
        var form = new AskForm.Choices(new AskChoiceForm(Items("a", "b", "other"), multiple, ["other"], "Something else"));
        var ask = Ask(form, "cancel");
        var draft = new AskDraft(form);
        draft.Text = "  mine  ";
        var answered = ask.Answer(0, draft);
        Assert.Equal("  mine  ", answered?.Text);
        Assert.Equal(multiple ? new[] { "other" } : Array.Empty<string>(), answered?.Selected);
        Assert.Equal(ControlAskResult.Escaped(), ask.Answer(1, draft));
        draft.Toggle(0);
        Assert.Equal(multiple ? "  mine  " : null, ask.Answer(0, draft)?.Text);
        if (multiple) draft.Toggle(3);
        Assert.Null(ask.Answer(0, draft)?.Text);
    }

    [Fact]
    public void ARadioAnswerThroughTheOtherRowKeepsAnEmptySelectedList() =>
        Assert.Equal("{\"result\":\"answered\",\"id\":\"ok\",\"label\":\"OK\",\"index\":0,\"text\":\"mine\",\"selected\":[]}",
            ControlJson.Serialize(Ask(OtherForm(false)).Answer(0, new AskDraft(OtherForm(false)) { Text = "mine" })));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetypingTheSameTextReselectsTheRow(bool multiple)
    {
        var draft = new AskDraft(OtherForm(multiple));
        draft.Text = "x";
        draft.Toggle(multiple ? 3 : 0);
        Assert.False(draft.OtherSelected);
        draft.Text = "x";
        Assert.True(draft.OtherSelected);
        Assert.Equal("x", draft.OtherText);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData(" \t\n\u00A0\u2003", true)]
    [InlineData(" x ", false)]
    [InlineData("\u200B", false)]
    public void IsBlankMeansOnlyWhitespace(string text, bool blank) => Assert.Equal(blank, AskChoiceForm.IsBlank(text));

    [Fact]
    public void FormsWithDifferentOtherRowsAreNotEqual()
    {
        Assert.Equal(new AskChoiceForm(Items("a"), Other: "x"), new AskChoiceForm(Items("a"), Other: "x"));
        Assert.NotEqual(new AskChoiceForm(Items("a"), Other: "x"), new AskChoiceForm(Items("a"), Other: "y"));
        Assert.NotEqual(new AskChoiceForm(Items("a"), Other: "x"), new AskChoiceForm(Items("a")));
    }

    // Port: Down and a marker click reach the row after the items; EditOther is a focus or click into its field.
    [Fact]
    public void ListIntentsReachTheOtherRow()
    {
        var draft = new AskDraft(OtherForm(true));
        Assert.True(draft.Apply(new AskIntent.ToggleAt(3)));
        Assert.Equal(3, draft.Cursor);
        Assert.False(draft.Apply(new AskIntent.ToggleAt(4)));
        Assert.True(draft.Apply(new AskIntent.Up()));
        Assert.True(draft.Apply(new AskIntent.EditOther()));
        Assert.True(draft.CursorOnOther);
        Assert.False(draft.OtherSelected);
        draft.Text = "x";
        Assert.True(draft.Apply(new AskIntent.Toggle()));
        Assert.False(draft.OtherSelected);
    }

    [Fact]
    public void EditOtherIsRefusedWithoutAnOtherRow()
    {
        Assert.False(new AskDraft(Choices(Items("a"))).Apply(new AskIntent.EditOther()));
        Assert.False(new AskDraft(Input(ControlAskInputMode.Line)).Apply(new AskIntent.EditOther()));
    }

    [Fact]
    public void ACopyCarriesTheOtherRowAndThePickItTookOverFrom()
    {
        var draft = new AskDraft(OtherForm(false, "b"));
        draft.Text = "x";

        var copy = draft.Copy();
        Assert.True(copy.OtherSelected);
        Assert.Equal("x", copy.OtherText);
        copy.Text = "";
        Assert.Equal(["b"], copy.Selected);
        Assert.True(draft.OtherSelected);

        var restored = new AskDraft(OtherForm(false));
        restored.CopyFrom(draft);
        Assert.True(restored.OtherSelected);
        restored.Text = "";
        Assert.Equal(["b"], restored.Selected);
    }
}
