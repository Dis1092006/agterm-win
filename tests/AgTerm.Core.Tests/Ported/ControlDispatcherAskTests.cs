using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Ported;

// Ported from agtermCore ControlDispatcherAskTests.swift (upstream ed831ab).
public class ControlDispatcherAskTests
{
    private static ControlResponse Err(string error) => new() { Ok = false, Error = error };

    private static ControlAskButton Button(string id, string label, string? hotkey = null) =>
        new() { Id = id, Label = label, Hotkey = hotkey };

    private static readonly ControlAskButton[] Ok = { Button("ok", "OK") };

    private static async Task<(ControlResponse? Response, MockControlActions Actions)> Run(ControlRequest request)
    {
        var actions = new MockControlActions();
        var response = await new ControlDispatcher(actions).Dispatch(request);
        return (response, actions);
    }

    private static Task<(ControlResponse? Response, MockControlActions Actions)> Open(ControlArgs? args, string? target = null) =>
        Run(new ControlRequest { Cmd = Command.AskOpen, Target = target, Args = args });

    private static void AssertRejected(string error, (ControlResponse? Response, MockControlActions Actions) run)
    {
        Assert.Equal(CanonicalJson.Of(Err(error)), CanonicalJson.Of(run.Response!));
        Assert.Empty(run.Actions.Calls);
    }

    private static MockControlActions.Call.AskOpen OpenCall(MockControlActions actions) =>
        Assert.IsType<MockControlActions.Call.AskOpen>(Assert.Single(actions.Calls));

    public static TheoryData<ControlArgs?, string> InvalidOpens => new()
    {
        { null, "ask.open requires a title" },
        { new ControlArgs(), "ask.open requires a title" },
        { new ControlArgs { Title = "" }, "ask.open requires a title" },
        { new ControlArgs { Title = "  " }, "ask.open requires a title" },
        { new ControlArgs { Title = "\t\n" }, "ask.open requires a title" },
        { new ControlArgs { Title = "Choose" }, "ask.open requires buttons" },
        { new ControlArgs { Title = "Choose", Buttons = Array.Empty<ControlAskButton>() }, "ask.open requires at least one button" },
        { new ControlArgs { Title = "Choose", Buttons = Enumerable.Range(0, 7).Select(i => Button($"{i}", $"Button {i}")).ToArray() }, "too many buttons (max 6)" },
        { new ControlArgs { Title = "Choose", Buttons = new[] { Button("empty", "") } }, "ask button label must not be empty" },
        { new ControlArgs { Title = "Choose", Buttons = new[] { Button("same", "First"), Button("same", "Second") } }, "ask button ids must be unique" },
        { new ControlArgs { Title = "Choose", Buttons = new[] { Button("caf\u00E9", "First"), Button("cafe\u0301", "Second") } }, "ask button ids must be unique" },
        { new ControlArgs { Title = "Bad\ntitle", Buttons = new[] { Button("yes", "Yes") } }, "ask text must not contain control characters" },
        { new ControlArgs { Title = "Choose", Message = "Bad\tmessage", Buttons = new[] { Button("yes", "Yes") } }, "ask text must not contain control characters" },
        { new ControlArgs { Title = "Choose", Buttons = new[] { Button("yes", "Bad\u007Flabel") } }, "ask text must not contain control characters" },
        { new ControlArgs { Title = "Choose", Buttons = new[] { Button("yes", "Yes") }, DefaultButton = "missing" }, "unknown default button: missing" },
        { new ControlArgs { Title = "Choose", Buttons = new[] { Button("yes", "Yes") }, DestructiveButton = "missing" }, "unknown destructive button: missing" },
        { new ControlArgs { Title = "Choose", Buttons = new[] { Button("delete", "Delete") }, DefaultButton = "delete", DestructiveButton = "delete" }, "default button must not be destructive" },
        { new ControlArgs { Title = "Choose", Buttons = new[] { Button("one", "One", "y"), Button("two", "Two", "Y") } }, "ask button hotkeys must be unique" },
    };

    [Theory]
    [MemberData(nameof(InvalidOpens))]
    public async Task InvalidOpenDoesNotReachTheHost(ControlArgs? args, string error) => AssertRejected(error, await Open(args));

    [Fact]
    public async Task AButtonWithoutIdOrLabelIsAnInvalidRequest() =>
        AssertRejected("invalid request: ask button requires id and label",
            await Open(new ControlArgs { Title = "Choose", Buttons = new[] { new ControlAskButton { Id = null!, Label = "L" } } }));

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public async Task InvalidWidthNeverReachesTheHost(int width) =>
        AssertRejected("width must be 10 to 100", await Open(new ControlArgs { Title = "Choose", Buttons = Ok, Width = width }));

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task FixedWidthReachesTheHost(int width)
    {
        var (_, actions) = await Open(new ControlArgs { Title = "Choose", Buttons = Ok, Width = width });
        Assert.Equal(width, OpenCall(actions).Ask.Width);
    }

    [Theory]
    [InlineData("")]
    [InlineData("GUI")]
    [InlineData("other")]
    public async Task InvalidStyleOrAlignNeverReachesTheHost(string value)
    {
        AssertRejected("unknown style", await Open(new ControlArgs { Title = "Choose", Buttons = Ok, Style = value }));
        AssertRejected("unknown align", await Open(new ControlArgs { Title = "Choose", Buttons = Ok, Align = value }));
    }

    [Theory]
    [InlineData("terminal", "left", ControlAskStyle.Terminal, ControlAskAlignment.Left)]
    [InlineData("gui", "center", ControlAskStyle.Gui, ControlAskAlignment.Center)]
    [InlineData("terminal", "right", ControlAskStyle.Terminal, ControlAskAlignment.Right)]
    public async Task DecorationReachesTheHost(string style, string align, ControlAskStyle expectedStyle, ControlAskAlignment expectedAlign)
    {
        var (_, actions) = await Open(new ControlArgs { Title = "Choose", Buttons = Ok, Style = style, Align = align });
        Assert.Equal(expectedStyle, OpenCall(actions).Ask.Style);
        Assert.Equal(expectedAlign, OpenCall(actions).Ask.Align);
    }

    [Theory]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("!")]
    [InlineData(" ")]
    [InlineData("\r")]
    [InlineData("\t")]
    [InlineData("\u001B")]
    [InlineData("→")]
    [InlineData("\u00E9")]
    [InlineData("á")]
    [InlineData("\U0001F98A")]
    public async Task HotkeysMustBeSingleAsciiLetters(string hotkey) =>
        AssertRejected("ask button hotkey must be one ASCII letter",
            await Open(new ControlArgs { Title = "Choose", Buttons = new[] { Button("yes", "Yes", hotkey) } }));

    [Theory]
    [InlineData("right", null)]
    [InlineData(null, "pane-id")]
    [InlineData(null, "")]
    public async Task GuiPaneSelectorsRequireASession(string? pane, string? paneId) =>
        AssertRejected("--pane requires a session",
            await Open(new ControlArgs { Title = "Choose", Buttons = new[] { Button("yes", "Yes") }, Style = "gui", Pane = pane, PaneId = paneId }));

    [Theory]
    [InlineData("")]
    [InlineData("scratch")]
    [InlineData("overlay")]
    [InlineData("unknown")]
    [InlineData("RIGHT")]
    public async Task AnInvalidPaneDoesNotReachTheHost(string pane) =>
        AssertRejected("--pane must be left or right",
            await Open(new ControlArgs { Title = "Choose", Buttons = new[] { Button("yes", "Yes") }, Pane = pane }, target: "active"));

    [Theory]
    [InlineData("right", null)]
    [InlineData(null, "pane-id")]
    public async Task TerminalPaneSelectorsReachTheHostWithoutASession(string? pane, string? paneId)
    {
        var (_, actions) = await Open(new ControlArgs { Title = "Choose", Buttons = new[] { Button("yes", "Yes") }, Pane = pane, PaneId = paneId });
        var call = OpenCall(actions);
        Assert.Equal(ControlAskStyle.Terminal, call.Ask.Style);
        Assert.Null(call.Target);
        Assert.Equal(new ControlAskPlacement(pane is null ? null : OverlayPane.Right, paneId), call.Placement);
    }

    [Theory]
    [InlineData(Command.AskResult, "ask.result requires an ask id")]
    [InlineData(Command.AskCancel, "ask.cancel requires an ask id")]
    public async Task LookupsRequireAnAskId(Command command, string error) =>
        AssertRejected(error, await Run(new ControlRequest { Cmd = command }));

    [Fact]
    public async Task AValidOpenPreservesFieldsAndLowersHotkeys()
    {
        var actions = new MockControlActions();
        var expected = new ControlResponse { Ok = true, Result = new ControlResult { Id = "ask-id", Pane = "right" } };
        actions.NextAskOpenResponse = expected;
        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest
        {
            Cmd = Command.AskOpen,
            Target = "session-id",
            Args = new ControlArgs
            {
                Follow = true, Message = "Changes are unsaved.",
                Buttons = new[] { Button("Save", "Save", "S"), Button("cancel", "Not now", "n"), Button("delete", "Delete") },
                DefaultButton = "Save", DestructiveButton = "delete", Style = "gui", Align = "center",
                Window = "window-id", Pane = "right", PaneId = "pane-id", Title = "  Choose  ",
            },
        });

        Assert.Same(expected, response);
        var call = OpenCall(actions);
        Assert.True(Guid.TryParseExact(call.Ask.Id, "D", out _));
        Assert.Equal(call.Ask.Id.ToUpperInvariant(), call.Ask.Id);
        Assert.Equal("  Choose  ", call.Ask.Title);
        Assert.Equal("Changes are unsaved.", call.Ask.Message);
        Assert.Equal(new[] { Button("Save", "Save", "s"), Button("cancel", "Not now", "n"), Button("delete", "Delete") }, call.Ask.Buttons);
        Assert.Equal("Save", call.Ask.DefaultId);
        Assert.Equal("delete", call.Ask.DestructiveId);
        Assert.Equal(ControlAskStyle.Gui, call.Ask.Style);
        Assert.Equal(ControlAskAlignment.Center, call.Ask.Align);
        Assert.Null(call.Ask.Anchor);
        Assert.Equal("session-id", call.Target);
        Assert.Equal("window-id", call.Window);
        Assert.Equal(new ControlAskPlacement(OverlayPane.Right, "pane-id"), call.Placement);
        Assert.True(call.Follow);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public async Task ValidButtonCountsLeaveOmittedOptionsUnset(int count)
    {
        var buttons = Enumerable.Range(0, count).Select(i => Button($"{i}", $"Button {i}")).ToArray();
        var (response, actions) = await Open(new ControlArgs { Title = "Choose", Buttons = buttons });

        Assert.True(response!.Ok);
        var call = OpenCall(actions);
        Assert.Equal(buttons, call.Ask.Buttons);
        Assert.Null(call.Ask.Message);
        Assert.Null(call.Ask.DefaultId);
        Assert.Equal(ControlAskStyle.Terminal, call.Ask.Style);
        Assert.Equal(ControlAskAlignment.Right, call.Ask.Align);
        Assert.Null(call.Ask.Width);
        Assert.Null(call.Ask.DestructiveId);
        Assert.Null(call.Target);
        Assert.Null(call.Window);
        Assert.Equal(new ControlAskPlacement(), call.Placement);
        Assert.False(call.Follow);
    }

    [Theory]
    [InlineData("left", OverlayPane.Left)]
    [InlineData("primary", OverlayPane.Left)]
    [InlineData("top", OverlayPane.Left)]
    [InlineData("right", OverlayPane.Right)]
    [InlineData("split", OverlayPane.Right)]
    [InlineData("bottom", OverlayPane.Right)]
    public async Task PaneAliasesReachTheHostAsCanonicalRoles(string raw, OverlayPane expected)
    {
        var (_, actions) = await Open(new ControlArgs { Title = "Choose", Buttons = Ok, Pane = raw }, target: "active");
        Assert.Equal("active", OpenCall(actions).Target);
        Assert.Equal(expected, OpenCall(actions).Placement.Pane);
    }

    [Fact]
    public async Task APaneIdPassesThroughWithoutARoleFallback()
    {
        var (_, actions) = await Open(new ControlArgs { Title = "Choose", Buttons = Ok, PaneId = "identity" }, target: "session-id");
        Assert.Equal(new ControlAskPlacement(null, "identity"), OpenCall(actions).Placement);
    }

    [Fact]
    public async Task ACancelLabelIsAnOrdinaryDefaultButton()
    {
        var (response, actions) = await Open(new ControlArgs { Title = "Choose", Buttons = new[] { Button("cancel", "Cancel") }, DefaultButton = "cancel" });
        Assert.True(response!.Ok);
        Assert.Single(actions.Calls);
    }

    [Fact]
    public async Task ADefaultMatchesACanonicallyEquivalentId()
    {
        var (response, _) = await Open(new ControlArgs { Title = "Choose", Buttons = new[] { Button("caf\u00E9", "Cafe") }, DefaultButton = "cafe\u0301" });
        Assert.True(response!.Ok);
    }

    [Fact]
    public async Task ADestructiveIdMatchesACanonicallyEquivalentButtonId()
    {
        var (response, _) = await Open(new ControlArgs { Title = "Choose", Buttons = new[] { Button("caf\u00E9", "Cafe") }, DestructiveButton = "cafe\u0301" });
        Assert.True(response!.Ok);
    }

    [Fact]
    public async Task CanonicallyEquivalentDefaultAndDestructiveIdsAreRefused() =>
        AssertRejected("default button must not be destructive",
            await Open(new ControlArgs { Title = "Choose", Buttons = new[] { Button("caf\u00E9", "Cafe") }, DefaultButton = "caf\u00E9", DestructiveButton = "cafe\u0301" }));

    [Fact]
    public async Task RepeatedOpensGenerateDistinctIds()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        var request = new ControlRequest { Cmd = Command.AskOpen, Args = new ControlArgs { Title = "Choose", Buttons = Ok } };
        await dispatcher.Dispatch(request);
        await dispatcher.Dispatch(request);
        var ids = actions.Calls.Cast<MockControlActions.Call.AskOpen>().Select(call => call.Ask.Id).ToArray();
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Fact]
    public async Task OpenReturnsAHostFailureUnchanged()
    {
        var actions = new MockControlActions { NextAskOpenResponse = Err("ask already pending") };
        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest { Cmd = Command.AskOpen, Args = new ControlArgs { Title = "Choose", Buttons = Ok } });
        Assert.Same(actions.NextAskOpenResponse, response);
        Assert.Single(actions.Calls);
    }

    [Fact]
    public async Task ResultAndCancelRouteTheIdAndWindow()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        await dispatcher.Dispatch(new ControlRequest { Cmd = Command.AskResult, Target = "ask-id", Args = new ControlArgs { Window = "window-id" } });
        await dispatcher.Dispatch(new ControlRequest { Cmd = Command.AskCancel, Target = "ask-id" });
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.AskResult("ask-id", "window-id"),
            new MockControlActions.Call.AskCancel("ask-id", null),
        }, actions.Calls);
    }

    private static async Task<PendingAsk> Opened(ControlArgs args)
    {
        var (response, actions) = await Open(args);
        Assert.True(response!.Ok, response.Error);
        return OpenCall(actions).Ask;
    }

    private static AskForm.Input InputForm(ControlAskInputMode mode, string value = "", string? placeholder = null) =>
        new(new AskInputForm(mode, value, placeholder));

    [Fact]
    public async Task AFormWithoutButtonsGetsOkAndCancelWithOkDefault()
    {
        var ask = await Opened(new ControlArgs { Title = "Name", Input = "line" });
        Assert.Equal(new[] { Button("ok", "OK"), Button("cancel", "Cancel") }, ask.Buttons);
        Assert.Equal("ok", ask.DefaultId);
        Assert.Equal("cancel", ask.CancelId);
        Assert.Equal(InputForm(ControlAskInputMode.Line), ask.Form);
    }

    [Fact]
    public async Task CallerButtonsReplaceTheDefaultsAndCancelMayBeTheDefault()
    {
        var ask = await Opened(new ControlArgs
        {
            Title = "Name", Input = "multiline", Value = "x", Placeholder = "who",
            Buttons = new[] { Button("save", "Save"), Button("skip", "Skip") }, DefaultButton = "skip", CancelButton = "skip",
        });
        Assert.Equal(new[] { "save", "skip" }, ask.Buttons.Select(button => button.Id));
        Assert.Equal("skip", ask.DefaultId);
        Assert.Equal("skip", ask.CancelId);
        Assert.Equal(InputForm(ControlAskInputMode.Multiline, "x", "who"), ask.Form);
    }

    [Fact]
    public async Task CallerButtonsWithAFormGetNoGeneratedRoles()
    {
        var ask = await Opened(new ControlArgs { Title = "Name", Input = "line", Buttons = new[] { Button("save", "Save") } });
        Assert.Null(ask.DefaultId);
        Assert.Null(ask.CancelId);
    }

    [Fact]
    public async Task APlainAskStillRequiresButtonsAndHasNoCancelRole()
    {
        AssertRejected("ask.open requires buttons", await Open(new ControlArgs { Title = "Go?" }));
        var ask = await Opened(new ControlArgs { Title = "Go?", Buttons = Ok });
        Assert.Null(ask.Form);
        Assert.Null(ask.CancelId);
        Assert.Null(ask.DefaultId);
    }

    [Fact]
    public async Task APlainAskTakesACancelRole() =>
        Assert.Equal("ok", (await Opened(new ControlArgs { Title = "Go?", Buttons = Ok, CancelButton = "ok" })).CancelId);

    public static TheoryData<ControlArgs, string> InvalidForms => new()
    {
        { new ControlArgs { Title = "T", Input = "wide" }, "unknown input" },
        { new ControlArgs { Title = "T", Input = "" }, "unknown input" },
        { new ControlArgs { Title = "T", Buttons = Ok, Value = "x" }, "value requires input" },
        { new ControlArgs { Title = "T", Buttons = Ok, Placeholder = "x" }, "placeholder requires input" },
        { new ControlArgs { Title = "T", Value = "x" }, "value requires input" },
        { new ControlArgs { Title = "T", Input = "line", CancelButton = "nope" }, "unknown cancel button: nope" },
        { new ControlArgs { Title = "T", Buttons = Ok, CancelButton = "nope" }, "unknown cancel button: nope" },
        { new ControlArgs { Title = "T", Input = "line", Buttons = new[] { Button("ok", "OK", "o") } }, "ask input does not take hotkeys" },
        { new ControlArgs { Title = "T", Input = "line", Buttons = new[] { Button("ok", "OK", "1") } }, "ask input does not take hotkeys" },
        { new ControlArgs { Title = "T", Input = "line", Buttons = Array.Empty<ControlAskButton>() }, "ask.open requires at least one button" },
        { new ControlArgs { Title = "T", Value = "a\tb", Input = "line" }, "ask text must not contain control characters" },
        { new ControlArgs { Title = "T", Value = "a\nb", Input = "line" }, "ask text must not contain control characters" },
        { new ControlArgs { Title = "T", Value = "a\u0007b", Input = "multiline" }, "ask text must not contain control characters" },
        { new ControlArgs { Title = "T", Input = "multiline", Placeholder = "a\nb" }, "ask text must not contain control characters" },
        { new ControlArgs { Title = "T", Input = "line", Choices = Items("a") }, "input and choices are exclusive" },
        { new ControlArgs { Title = "T", Value = "x", Choices = Items("a") }, "value requires input" },
        { new ControlArgs { Title = "T", Placeholder = "x", Choices = Items("a") }, "placeholder requires input" },
        { new ControlArgs { Title = "T", Input = "line", Checked = ["a"] }, "checked requires choices" },
        { new ControlArgs { Title = "T", Input = "line", Multiple = true }, "multiple requires choices" },
        { new ControlArgs { Title = "T", Buttons = Ok, Checked = [] }, "checked requires choices" },
        { new ControlArgs { Title = "T", Buttons = Ok, Multiple = false }, "multiple requires choices" },
        { new ControlArgs { Title = "T", Buttons = Ok, Checked = ["a"], Multiple = true }, "checked requires choices" },
        { new ControlArgs { Title = "T", Choices = [] }, "ask choices must be 1 to 50" },
        { new ControlArgs { Title = "T", Choices = [Choice("a", "")] }, "ask choice label must not be empty" },
        { new ControlArgs { Title = "T", Choices = [Choice("a", "A"), Choice("a", "B")] }, "ask choice ids must be unique" },
        { new ControlArgs { Title = "T", Choices = [Choice("caf\u00E9", "A"), Choice("cafe\u0301", "B")] }, "ask choice ids must be unique" },
        { new ControlArgs { Title = "T", Choices = Items("a"), Checked = ["b"] }, "unknown checked choice: b" },
        { new ControlArgs { Title = "T", Choices = Items("a"), Multiple = true, Checked = ["a", "a"] }, "ask checked ids must be unique" },
        { new ControlArgs { Title = "T", Choices = Items("a", "b"), Checked = ["a", "b"] }, "radio choices accept one checked id" },
        { new ControlArgs { Title = "T", Choices = Items("a", "b"), Multiple = false, Checked = ["a", "b"] }, "radio choices accept one checked id" },
        { new ControlArgs { Title = "T", Choices = [Choice("a", "A\nB")] }, "ask text must not contain control characters" },
        { new ControlArgs { Title = "T", Choices = [Choice("a", "A", "x\ty")] }, "ask text must not contain control characters" },
        { new ControlArgs { Title = "T", Choices = [Choice("a", "A", "x\r\ny")] }, "ask text must not contain control characters" },
        { new ControlArgs { Title = "T", Choices = [Choice("a", "A\u001B[1m")] }, "ask text must not contain control characters" },
        { new ControlArgs { Title = "T", Choices = Items("a"), Checked = ["b"], Multiple = false }, "unknown checked choice: b" },
        { new ControlArgs { Title = "T", Choices = [Choice("a", "")], Checked = ["a", "a"] }, "ask choice label must not be empty" },
        { new ControlArgs { Title = "T", Choices = [Choice("a", "A\n")], Checked = ["b"] }, "ask text must not contain control characters" },
        { new ControlArgs { Title = "T", Choices = Items("a"), Checked = ["b", "b"] }, "ask checked ids must be unique" },
        { new ControlArgs { Title = "T", Choices = Items("a"), Checked = ["b", "a", "c"], Multiple = false }, "unknown checked choice: b" },
        { new ControlArgs { Title = "T", Choices = [null!] }, "invalid request: ask choice requires id and label" },
        { new ControlArgs { Title = "T", Choices = [new ControlAskChoice { Id = null!, Label = "A" }] }, "invalid request: ask choice requires id and label" },
        { new ControlArgs { Title = "T", Choices = [new ControlAskChoice { Id = "a", Label = null! }] }, "invalid request: ask choice requires id and label" },
        { new ControlArgs { Title = "T", Choices = Items("a"), Checked = [null!] }, "invalid request: ask checked id must be a string" },
        { new ControlArgs { Title = "T", Input = "line", Other = "x" }, "other requires choices" },
        { new ControlArgs { Title = "T", Buttons = Ok, Other = "x" }, "other requires choices" },
        { new ControlArgs { Title = "T", Buttons = Ok, Multiple = true, Other = "x" }, "multiple requires choices" },
        { new ControlArgs { Title = "T", Value = "x", Input = "line", Other = "  " }, "other requires choices" },
        { new ControlArgs { Title = "T", Choices = Items("a"), Other = "  " }, "ask other placeholder must not be blank" },
        { new ControlArgs { Title = "T", Choices = Items("a"), Other = "" }, "ask other placeholder must not be blank" },
        { new ControlArgs { Title = "T", Choices = Items("a"), Other = "a\tb" }, "ask text must not contain control characters" },
        { new ControlArgs { Title = "T", Choices = Items("a", "b"), Checked = ["a", "b"], Other = "  " }, "radio choices accept one checked id" },
    };

    private static ControlAskChoice Choice(string id, string label, string? subtitle = null) =>
        new() { Id = id, Label = label, Subtitle = subtitle };

    private static ControlAskChoice[] Items(params string[] ids) => ids.Select(id => Choice(id, id.ToUpperInvariant())).ToArray();

    [Fact]
    public async Task ChoicesReachTheHostWithSubtitlesAndCheckedIds()
    {
        var choices = new[] { Choice("398", "issue #398", "waits on you\nhttps://example.com/398"), Choice("395", "PR #395") };
        var ask = await Opened(new ControlArgs { Title = "Pick", Choices = choices, Multiple = true, Checked = ["395"] });
        Assert.Equal(new AskForm.Choices(new AskChoiceForm(choices, true, ["395"])), ask.Form);
        Assert.Equal(new[] { Button("ok", "OK"), Button("cancel", "Cancel") }, ask.Buttons);
        Assert.Equal("ok", ask.DefaultId);
        Assert.Equal("cancel", ask.CancelId);
    }

    [Fact]
    public async Task OtherPlaceholderReachesHostWithTheChoices() =>
        Assert.Equal(new AskForm.Choices(new AskChoiceForm(Items("a"), false, [], "Something else")),
            (await Opened(new ControlArgs { Title = "Pick", Choices = Items("a"), Other = "Something else" })).Form);

    [Fact]
    public async Task ARadioListWithoutCheckedIdsReachesTheHostAsRadio() =>
        Assert.Equal(new AskForm.Choices(new AskChoiceForm(Items("a", "b"), false, [])),
            (await Opened(new ControlArgs { Title = "T", Choices = Items("a", "b") })).Form);

    [Fact]
    public async Task AChoiceListTakesButtonHotkeys() =>
        Assert.NotNull((await Opened(new ControlArgs { Title = "T", Choices = Items("a"), Buttons = [Button("ok", "OK", "o")] })).Form);

    [Fact]
    public async Task ACheckedIdMatchesACanonicallyEquivalentChoiceId() =>
        Assert.NotNull((await Opened(new ControlArgs { Title = "T", Choices = Items("caf\u00E9"), Checked = ["cafe\u0301"] })).Form);

    [Fact]
    public async Task FiftyChoicesAreAcceptedAndFiftyOneRejected()
    {
        var fifty = Enumerable.Range(0, 50).Select(i => Choice($"{i}", $"item {i}")).ToArray();
        await Opened(new ControlArgs { Title = "T", Choices = fifty });
        AssertRejected("ask choices must be 1 to 50", await Open(new ControlArgs { Title = "T", Choices = [.. fifty, Choice("x", "x")] }));
    }

    [Fact]
    public async Task ASubtitleKeepsItsNewlines() =>
        Assert.Equal(new AskForm.Choices(new AskChoiceForm([Choice("a", "A", "x\ny")], false, [])),
            (await Opened(new ControlArgs { Title = "T", Choices = [Choice("a", "A", "x\ny")] })).Form);

    [Theory]
    [MemberData(nameof(InvalidForms))]
    public async Task AnInvalidFormNeverReachesTheHost(ControlArgs args, string error) => AssertRejected(error, await Open(args));

    [Fact]
    public async Task AFormIsCheckedBeforeTheButtons() =>
        AssertRejected("unknown input",
            await Open(new ControlArgs { Title = "T", Input = "wide", Buttons = Array.Empty<ControlAskButton>() }));

    [Fact]
    public async Task AnInputHotkeyIsRefusedBeforeTheStyle() =>
        AssertRejected("ask input does not take hotkeys",
            await Open(new ControlArgs { Title = "T", Input = "line", Style = "bad", Buttons = new[] { Button("ok", "OK", "o") } }));

    [Fact]
    public async Task ACancelIdMatchesACanonicallyEquivalentButtonId()
    {
        var ask = await Opened(new ControlArgs { Title = "T", Buttons = new[] { Button("caf\u00E9", "Cafe") }, CancelButton = "cafe\u0301" });
        Assert.Equal("cafe\u0301", ask.CancelId);
    }

    [Fact]
    public async Task AMultilineValueKeepsItsAllowedControls() =>
        Assert.Equal(InputForm(ControlAskInputMode.Multiline, "a\nb\tc"),
            (await Opened(new ControlArgs { Title = "T", Value = "a\nb\tc", Input = "multiline" })).Form);

    [Fact]
    public async Task TheValueIsCappedInUtf8Bytes()
    {
        var fits = string.Concat(Enumerable.Repeat("\u00E9", AskInputForm.MaxTextBytes / 2));
        await Opened(new ControlArgs { Title = "T", Value = fits, Input = "line" });
        AssertRejected("ask text too long (max 65536 bytes)", await Open(new ControlArgs { Title = "T", Value = fits + "x", Input = "line" }));
    }
}
