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
        { new ControlArgs { Title = "Choose", Buttons = new[] { Button("café", "First"), Button("café", "Second") } }, "ask button ids must be unique" },
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
    [InlineData("é")]
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
        var (response, _) = await Open(new ControlArgs { Title = "Choose", Buttons = new[] { Button("café", "Cafe") }, DefaultButton = "café" });
        Assert.True(response!.Ok);
    }

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
}
