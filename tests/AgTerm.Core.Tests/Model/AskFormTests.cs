using System.Text;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// Ported from agtermCore AskTests.swift input-form cases (upstream 96b0a6c6); the choice-list cases come with B9b.
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
        var form = Input(ControlAskInputMode.Line);
        var anchored = Ask(form, "cancel") with { Anchor = new AskAnchor(Guid.NewGuid()) };
        Assert.Equal(form, anchored.Form);
        Assert.Equal("cancel", anchored.CancelId);
    }
}
