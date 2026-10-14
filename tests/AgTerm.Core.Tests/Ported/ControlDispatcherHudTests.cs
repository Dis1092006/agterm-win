using System.Globalization;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Ported;

// Ported from agtermCore ControlDispatcherHudTests.swift (upstream ed831ab).
public class ControlDispatcherHudTests
{
    private const string PositionNames =
        "top-left|top-center|top-right|center-left|center|center-right|bottom-left|bottom-center|bottom-right|top|bottom";

    private static ControlResponse Err(string error) => new() { Ok = false, Error = error };

    private static async Task<(ControlResponse? Response, MockControlActions Actions)> Run(ControlRequest request)
    {
        var actions = new MockControlActions();
        var response = await new ControlDispatcher(actions).Dispatch(request);
        return (response, actions);
    }

    private static Task<(ControlResponse? Response, MockControlActions Actions)> Post(Command cmd, ControlArgs? args, string? target = null) =>
        Run(new ControlRequest { Cmd = cmd, Target = target, Args = args });

    private static Task<(ControlResponse? Response, MockControlActions Actions)> Open(ControlArgs? args) =>
        Post(Command.SessionHudOpen, args);

    private static Task<(ControlResponse? Response, MockControlActions Actions)> Update(ControlArgs? args) =>
        Post(Command.SessionHudUpdate, args);

    private static void AssertRejected(string error, (ControlResponse? Response, MockControlActions Actions) run)
    {
        Assert.Equal(CanonicalJson.Of(Err(error)), CanonicalJson.Of(run.Response!));
        Assert.Empty(run.Actions.Calls);
    }

    private static HudSpec SentSpec((ControlResponse? Response, MockControlActions Actions) run)
    {
        Assert.True(run.Response!.Ok, run.Response.Error);
        var call = Assert.Single(run.Actions.Calls);
        return call is MockControlActions.Call.HudUpdate update
            ? update.Spec
            : Assert.IsType<MockControlActions.Call.HudOpen>(call).Spec;
    }

    [Theory]
    [InlineData(Command.SessionHudOpen, "session.hud.open requires a message")]
    [InlineData(Command.SessionHudUpdate, "session.hud.update requires a message")]
    public async Task AMissingOrEmptyMessageNeverReachesTheHost(Command cmd, string error)
    {
        AssertRejected(error, await Post(cmd, null));
        AssertRejected(error, await Post(cmd, new ControlArgs()));
        AssertRejected(error, await Post(cmd, new ControlArgs { Message = "" }));
        AssertRejected(error, await Post(cmd, new ControlArgs { Message = "", Detail = "still here" }));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData("\r\n")]
    [InlineData("\u00A0")]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u3000")]
    public async Task AWhitespaceOnlyMessageIsNoMessage(string message)
    {
        AssertRejected("session.hud.open requires a message", await Open(new ControlArgs { Message = message }));
        AssertRejected("session.hud.update requires a message", await Update(new ControlArgs { Message = message }));
    }

    [Theory]
    [InlineData("\u200B")]
    [InlineData("\uFEFF")]
    public async Task AnInvisibleScalarOutsideTheWhitespaceSetIsAMessage(string message) =>
        Assert.Equal(message, SentSpec(await Open(new ControlArgs { Message = message })).Message);

    [Fact]
    public async Task AUnitSeparatorIsAControlCharacterNotWhitespace() =>
        AssertRejected("hud text must not contain control characters", await Open(new ControlArgs { Message = "\u001F" }));

    [Fact]
    public async Task TheMessageReachesTheHostAsSent() =>
        Assert.Equal("  padded  ", SentSpec(await Open(new ControlArgs { Message = "  padded  " })).Message);

    [Fact]
    public async Task OpenCarriesTheRequestedHideAfterToTheHost()
    {
        var spec = SentSpec(await Open(new ControlArgs { Message = "deploying", HideAfter = 10 }));
        Assert.Equal(10.0, spec.HideAfter);
        Assert.Equal(10.0, spec.EffectiveHideAfter);
    }

    [Fact]
    public async Task UpdateCarriesTheRequestedHideAfterToTheHost() =>
        Assert.Equal(2.5, SentSpec(await Update(new ControlArgs { Message = "ready", HideAfter = 2.5 })).HideAfter);

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(86400.0)]
    public async Task ZeroAFractionAndTheMaximumReachTheHost(double seconds) =>
        Assert.Equal(seconds, SentSpec(await Open(new ControlArgs { Message = "working", HideAfter = seconds })).HideAfter);

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e12)]
    [InlineData(86401.0)]
    public async Task OpenRejectsAHideAfterNothingCanSchedule(double seconds) =>
        AssertRejected("session.hud.open: --hide-after must be 0...86400 seconds",
            await Open(new ControlArgs { Message = "working", HideAfter = seconds }));

    [Fact]
    public async Task UpdateRejectsANegativeHideAfter() =>
        AssertRejected("session.hud.update: --hide-after must be 0...86400 seconds",
            await Update(new ControlArgs { Message = "working", HideAfter = -0.5 }));

    [Theory]
    [InlineData("paint\u001B[2Jme")]
    [InlineData("two\nlines")]
    [InlineData("tab\there")]
    [InlineData("cr\rhere")]
    [InlineData("del\u007F")]
    public async Task APlainMessageRefusesEveryControlCharacter(string message) =>
        AssertRejected("hud text must not contain control characters", await Open(new ControlArgs { Message = message }));

    [Fact]
    public async Task ADetailRefusesControlCharacters() =>
        AssertRejected("hud text must not contain control characters",
            await Update(new ControlArgs { Message = "fine", Detail = "bad\u007F" }));

    [Theory]
    [InlineData("cr\rhere")]
    [InlineData("esc\u001B[2J")]
    [InlineData("del\u007F")]
    [InlineData("bell\u0007")]
    public async Task MarkdownStillRejectsEveryOtherControlCharacter(string message) =>
        AssertRejected("hud text must not contain control characters",
            await Open(new ControlArgs { Message = message, Markdown = true }));

    [Fact]
    public async Task MarkdownKeepsTheDetailPlainRules()
    {
        AssertRejected("hud text must not contain control characters",
            await Open(new ControlArgs { Message = "ok", Detail = "two\nlines", Markdown = true }));
        AssertRejected("hud detail too long (max 256 characters)",
            await Open(new ControlArgs { Message = "ok", Detail = new string('d', HudSpec.MaxTextLength + 1), Markdown = true }));
    }

    [Fact]
    public async Task AWhitespaceAndNewlineOnlyMarkdownMessageIsNoMessage() =>
        AssertRejected("session.hud.open requires a message", await Open(new ControlArgs { Message = " \n\t\n ", Markdown = true }));

    // Ported markdown cases of agtermCore ControlDispatcherHudTests.swift (upstream ed831ab) that the
    // stage 1 refusal answered, and the ones that need the renderer.
    [Fact]
    public async Task MarkdownAcceptsNewlinesAndTabsAndCarriesTheModeToTheHost()
    {
        var spec = SentSpec(await Open(new ControlArgs { Message = "# Tasks\n\n- build\n\t- test", Markdown = true }));

        Assert.True(spec.Markdown);
        Assert.Equal("# Tasks\n\n- build\n\t- test", spec.Message);
    }

    [Fact]
    public async Task MarkdownCapsTheMessageAtItsOwnLength()
    {
        var atCap = new string('m', HudSpec.MaxMarkdownLength);

        Assert.Equal(atCap, SentSpec(await Open(new ControlArgs { Message = atCap, Markdown = true })).Message);
        AssertRejected("hud message too long (max 4096 characters)",
            await Open(new ControlArgs { Message = atCap + "m", Markdown = true }));
    }

    // upstream: a source that rendered nothing passed the blank check and opened an empty panel tree reported as live
    [Theory]
    [InlineData("[x]: /y")]
    [InlineData("&#32;")]
    [InlineData("- ")]
    public async Task MarkdownThatRendersNothingIsNoMessage(string message)
    {
        AssertRejected("session.hud.open requires a message", await Open(new ControlArgs { Message = message, Markdown = true }));
        AssertRejected("session.hud.update requires a message", await Update(new ControlArgs { Message = message, Markdown = true }));
    }

    [Theory]
    [InlineData("<!-- note -->\n\n[ref]: http://a")]
    [InlineData("---")]
    public async Task MarkdownThatPaintsOnlyLiteralHtmlOrARuleIsAMessage(string message) =>
        Assert.True(SentSpec(await Open(new ControlArgs { Message = message, Markdown = true })).Markdown);

    [Fact]
    public async Task ATooLongMessageIsRefusedBeforeItsRenderingIsChecked() =>
        AssertRejected("hud message too long (max 4096 characters)",
            await Open(new ControlArgs { Message = string.Concat(Enumerable.Repeat("&#32;", 820)), Markdown = true }));

    [Fact]
    public async Task AnInvisibleMessageIsRefusedBeforeATooLongDetail() =>
        AssertRejected("session.hud.open requires a message",
            await Open(new ControlArgs { Message = "[x]: /y", Detail = new string('d', HudSpec.MaxTextLength + 1), Markdown = true }));

    [Fact]
    public async Task APlainMessageIsNeverParsedAsMarkdown() =>
        Assert.Equal("[x]: /y", SentSpec(await Open(new ControlArgs { Message = "[x]: /y" })).Message);

    [Fact]
    public async Task AMarkdownUpdateReachesTheHost() =>
        Assert.True(SentSpec(await Update(new ControlArgs { Message = "**done**", Markdown = true })).Markdown);

    [Fact]
    public async Task MarkdownFalseIsAPlainMessage() =>
        Assert.False(SentSpec(await Open(new ControlArgs { Message = "plain", Markdown = false })).Markdown);

    [Theory]
    [InlineData(6.0)]
    [InlineData(72.0)]
    public async Task OpenCarriesAFontSizeInsideTheRange(double points) =>
        Assert.Equal(points, SentSpec(await Open(new ControlArgs { Message = "a", FontSize = points })).FontSize);

    [Theory]
    [InlineData(5.5)]
    [InlineData(72.5)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public async Task OpenRejectsAFontSizeOutsideTheRange(double points) =>
        AssertRejected("session.hud.open: --font-size must be 6...72 points", await Open(new ControlArgs { Message = "a", FontSize = points }));

    [Theory]
    [InlineData(14.0)]
    [InlineData(1000.0)]
    public async Task UpdateRejectsAnyFontSize(double points) =>
        AssertRejected("session.hud.update: --font-size is fixed at open; reopen the hud to change it",
            await Update(new ControlArgs { Message = "a", FontSize = points }));

    [Fact]
    public async Task OversizedMessageAndDetailNeverReachTheHost()
    {
        var tooLong = new string('a', HudSpec.MaxTextLength + 1);
        var atCap = new string('b', HudSpec.MaxTextLength);

        AssertRejected("hud message too long (max 256 characters)", await Open(new ControlArgs { Message = tooLong }));
        AssertRejected("hud detail too long (max 256 characters)", await Open(new ControlArgs { Message = "fine", Detail = tooLong }));
        Assert.Equal(atCap, SentSpec(await Open(new ControlArgs { Message = atCap, Detail = atCap })).Detail);
    }

    [Fact]
    public async Task CapsTextInTheUnitTheLayoutMeasures()
    {
        var emoji = string.Concat(Enumerable.Repeat("\U0001F468\u200D\U0001F469\u200D\U0001F467", 100));
        var decomposed = string.Concat(Enumerable.Repeat("e\u0301", HudSpec.MaxTextLength));

        Assert.True(new StringInfo(emoji).LengthInTextElements <= HudSpec.MaxTextLength, "a grapheme count would let this through");
        AssertRejected("hud message too long (max 256 characters)", await Open(new ControlArgs { Message = emoji }));
        Assert.Equal(decomposed, SentSpec(await Open(new ControlArgs { Message = decomposed })).Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankDetailPassesAndIsKeptAsSent(string detail) =>
        Assert.Equal(detail, SentSpec(await Open(new ControlArgs { Message = "working", Detail = detail })).Detail);

    [Theory]
    [InlineData(Command.SessionHudOpen)]
    [InlineData(Command.SessionHudUpdate)]
    public async Task AnInvalidColorNeverReachesTheHost(Command cmd) =>
        AssertRejected("invalid color: blue (#rrggbb)", await Post(cmd, new ControlArgs { Message = "working", Color = "blue" }));

    [Theory]
    [InlineData("#112233")]
    [InlineData("2a1a3a")]
    public async Task AValidColorReachesTheHostVerbatim(string color) =>
        Assert.Equal(color, SentSpec(await Open(new ControlArgs { Message = "working", Color = color })).BackgroundColor);

    [Fact]
    public async Task AMalformedTextColorNeverReachesTheHost() =>
        AssertRejected("invalid text color: #gg0000 (#rrggbb)", await Open(new ControlArgs { Message = "working", TextColor = "#gg0000" }));

    [Fact]
    public async Task UpdateCarriesItsOwnTextColorThrough()
    {
        var spec = SentSpec(await Update(new ControlArgs { Message = "done", TextColor = "#7ec07e", Position = "bottom-right" }));
        Assert.Equal("#7ec07e", spec.TextColor);
        Assert.Equal(HudPosition.BottomRight, spec.Position);
    }

    [Fact]
    public async Task ASizePercentOutsideTheWireRangeNeverReachesTheHost()
    {
        AssertRejected("session.hud.open: --size-percent must be 1...100", await Open(new ControlArgs { SizePercent = 0, Message = "working" }));
        AssertRejected("session.hud.update: --size-percent must be 1...100", await Update(new ControlArgs { SizePercent = 101, Message = "working" }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public async Task ASizePercentInsideTheWireRangeReachesTheHostUnclamped(int percent) =>
        Assert.Equal(percent, SentSpec(await Open(new ControlArgs { SizePercent = percent, Message = "working" })).SizePercent);

    [Theory]
    [InlineData("top-middle")]
    [InlineData("nope")]
    [InlineData("TOP")]
    [InlineData("")]
    public async Task AnUnknownPositionNamesEveryAcceptedSpelling(string raw) =>
        AssertRejected($"invalid position: {raw} ({PositionNames})", await Open(new ControlArgs { Message = "working", Position = raw }));

    [Theory]
    [InlineData("top-left", HudPosition.TopLeft)]
    [InlineData("center-right", HudPosition.CenterRight)]
    [InlineData("bottom-right", HudPosition.BottomRight)]
    [InlineData("top", HudPosition.TopCenter)]
    [InlineData("bottom", HudPosition.BottomCenter)]
    public async Task AcceptsEveryAnchorAndBothAliases(string raw, HudPosition expected) =>
        Assert.Equal(expected, SentSpec(await Open(new ControlArgs { Message = "working", Position = raw })).Position);

    [Fact]
    public async Task EveryPositionRoundTripsToTheHost()
    {
        foreach (var position in Enum.GetValues<HudPosition>())
            Assert.Equal(position, SentSpec(await Open(new ControlArgs { Message = "working", Position = HudPositions.Wire(position) })).Position);
    }

    [Theory]
    [InlineData("swirl")]
    [InlineData("Bar")]
    [InlineData("NONE")]
    public async Task AnUnknownSpinnerStyleNamesNoneToo(string raw) =>
        AssertRejected($"invalid spinner: {raw} (bar|braille|circle|blocks|dot|none)", await Open(new ControlArgs { Message = "working", Spinner = raw }));

    [Theory]
    [InlineData("bar", HudSpinner.Bar)]
    [InlineData("braille", HudSpinner.Braille)]
    [InlineData("circle", HudSpinner.Circle)]
    [InlineData("blocks", HudSpinner.Blocks)]
    [InlineData("dot", HudSpinner.Dot)]
    public async Task AcceptsEverySpinnerStyle(string raw, HudSpinner expected) =>
        Assert.Equal(expected, SentSpec(await Open(new ControlArgs { Message = "working", Spinner = raw })).Spinner);

    [Fact]
    public async Task TheReadBacksNoneIsNoSpinner() =>
        Assert.Null(SentSpec(await Open(new ControlArgs { Message = "working", Spinner = HudSpinners.NoneName })).Spinner);

    [Fact]
    public async Task OpenRoutesTheParsedSpecAndReturnsTheHostResponse()
    {
        var actions = new MockControlActions();
        var expected = new ControlResponse { Ok = true, Result = new ControlResult { Id = "session-id" } };
        actions.NextHudOpenResponse = expected;

        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest
        {
            Cmd = Command.SessionHudOpen,
            Target = "session-id",
            Args = new ControlArgs
            {
                SizePercent = 40, Message = "gathering options", Detail = "scanning 400 files",
                Spinner = "bar", Window = "window-id", Color = "#112233", Position = "top",
            },
        });

        Assert.Same(expected, response);
        Assert.Equal(
            new MockControlActions.Call.HudOpen("session-id", "window-id",
                new HudSpec("gathering options", "scanning 400 files", HudSpinner.Bar,
                    BackgroundColor: "#112233", SizePercent: 40, Position: HudPosition.TopCenter),
                new ControlHudPlacement()),
            Assert.Single(actions.Calls));
    }

    [Fact]
    public async Task UpdateRoutesTheParsedSpecAndReturnsTheHostResponse()
    {
        var actions = new MockControlActions();
        var expected = new ControlResponse { Ok = true, Result = new ControlResult { Id = "session-id" } };
        actions.NextHudUpdateResponse = expected;

        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest
        {
            Cmd = Command.SessionHudUpdate,
            Target = "session-id",
            Args = new ControlArgs { Message = "still working", Detail = "312 of 400", Position = "bottom" },
        });

        Assert.Same(expected, response);
        Assert.Equal(
            new MockControlActions.Call.HudUpdate("session-id", null,
                new HudSpec("still working", "312 of 400", Position: HudPosition.BottomCenter),
                new ControlHudPlacement()),
            Assert.Single(actions.Calls));
    }

    [Theory]
    [InlineData(Command.SessionHudOpen)]
    [InlineData(Command.SessionHudUpdate)]
    public async Task AHostFailureComesBackUnchanged(Command cmd)
    {
        var actions = new MockControlActions
        {
            NextHudOpenResponse = Err("overlay already open"),
            NextHudUpdateResponse = Err(HudErrors.NoHud),
        };
        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest { Cmd = cmd, Args = new ControlArgs { Message = "m" } });

        Assert.Same(cmd == Command.SessionHudOpen ? actions.NextHudOpenResponse : actions.NextHudUpdateResponse, response);
        Assert.Single(actions.Calls);
    }

    [Fact]
    public async Task PaneAndPaneIdRouteToOpenAndUpdate()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionHudOpen, Args = new ControlArgs { Message = "working", Pane = "split", PaneId = "stable-token" },
        });
        await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionHudUpdate, Args = new ControlArgs { Message = "done", Pane = "top", PaneId = "next-token" },
        });

        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.HudOpen(null, null, new HudSpec("working"), new ControlHudPlacement(OverlayPane.Right, "stable-token")),
            new MockControlActions.Call.HudUpdate(null, null, new HudSpec("done"), new ControlHudPlacement(OverlayPane.Left, "next-token")),
        }, actions.Calls);
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
        var (_, actions) = await Open(new ControlArgs { Message = "working", Pane = raw });
        Assert.Equal(new ControlHudPlacement(expected), Assert.IsType<MockControlActions.Call.HudOpen>(Assert.Single(actions.Calls)).Placement);
    }

    [Fact]
    public async Task AnEmptyPaneIdPassesThroughForTheHostToIgnore()
    {
        var (_, actions) = await Open(new ControlArgs { Message = "working", PaneId = "" });
        Assert.Equal(new ControlHudPlacement(null, ""), Assert.IsType<MockControlActions.Call.HudOpen>(Assert.Single(actions.Calls)).Placement);
    }

    [Theory]
    [InlineData("scratch")]
    [InlineData("")]
    [InlineData("overlay")]
    [InlineData("RIGHT")]
    public async Task AnInvalidPaneNeverReachesTheHost(string pane)
    {
        AssertRejected("--pane must be left or right", await Open(new ControlArgs { Message = "working", Pane = pane }));
        AssertRejected("--pane must be left or right", await Update(new ControlArgs { Message = "working", Pane = pane }));
    }

    [Fact]
    public async Task ThePaneIsCheckedBeforeTheMessage()
    {
        AssertRejected("--pane must be left or right", await Open(new ControlArgs { Pane = "scratch" }));
        AssertRejected("--pane must be left or right", await Update(new ControlArgs { Pane = "scratch" }));
    }

    [Theory]
    [InlineData(Command.SessionHudOpen)]
    [InlineData(Command.SessionHudUpdate)]
    public async Task OmittedFieldsTakeTheirDefaults(Command cmd)
    {
        var (_, actions) = await Post(cmd, new ControlArgs { Message = "working" });

        MockControlActions.Call expected = cmd == Command.SessionHudOpen
            ? new MockControlActions.Call.HudOpen(null, null, new HudSpec("working"), new ControlHudPlacement())
            : new MockControlActions.Call.HudUpdate(null, null, new HudSpec("working"), new ControlHudPlacement());
        Assert.Equal(expected, Assert.Single(actions.Calls));
    }

    [Theory]
    [InlineData(Command.SessionHudOpen)]
    [InlineData(Command.SessionHudUpdate)]
    public async Task StickyAndNoFrameReachTheHost(Command cmd)
    {
        var spec = SentSpec(await Post(cmd, new ControlArgs { Message = "caption", Sticky = true, Frame = false }));
        Assert.True(spec.Sticky);
        Assert.False(spec.Frame);
    }

    [Theory]
    [InlineData(Command.SessionHudOpen)]
    [InlineData(Command.SessionHudUpdate)]
    public async Task OmittedStickyAndFrameReachTheHostAsAFramedPanelWithItsMargin(Command cmd)
    {
        var spec = SentSpec(await Post(cmd, new ControlArgs { Message = "caption" }));
        Assert.False(spec.Sticky);
        Assert.True(spec.Frame);
    }

    [Fact]
    public async Task CloseRoutesTargetAndWindowAndIgnoresEverythingElse()
    {
        var actions = new MockControlActions();
        var expected = new ControlResponse { Ok = true, Result = new ControlResult { Id = "session-id" } };
        actions.NextHudCloseResponse = expected;

        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest
        {
            Cmd = Command.SessionHudClose,
            Target = "session-id",
            Args = new ControlArgs
            {
                Message = "", Window = "window-id", Color = "not a color", Position = "nowhere",
                Pane = "scratch", FontSize = 1000, Markdown = true,
            },
        });

        Assert.Same(expected, response);
        Assert.Equal(new MockControlActions.Call[] { new MockControlActions.Call.HudClose("session-id", "window-id") }, actions.Calls);
    }

    [Fact]
    public async Task CloseWithoutArgsRoutesNoWindow()
    {
        var (_, actions) = await Post(Command.SessionHudClose, null);
        Assert.Equal(new MockControlActions.Call[] { new MockControlActions.Call.HudClose(null, null) }, actions.Calls);
    }

    // each row breaks two neighboring rules of spec section 1.2; the earlier rule must answer
    public static TheoryData<ControlArgs, string> NeighborRules => new()
    {
        { new ControlArgs { Pane = "scratch" }, "--pane must be left or right" },
        { new ControlArgs { Message = "\t\n" }, "session.hud.open requires a message" },
        { new ControlArgs { Message = new string('a', 300) + "\u001B" }, "hud text must not contain control characters" },
        { new ControlArgs { Message = new string('a', 257), Detail = new string('d', 257) }, "hud message too long (max 256 characters)" },
        { new ControlArgs { Message = "m", Detail = new string('d', 257), Color = "blue" }, "hud detail too long (max 256 characters)" },
        { new ControlArgs { Message = "m", Color = "blue", TextColor = "nope" }, "invalid color: blue (#rrggbb)" },
        { new ControlArgs { Message = "m", TextColor = "nope", FontSize = 100 }, "invalid text color: nope (#rrggbb)" },
        { new ControlArgs { Message = "m", FontSize = 100, SizePercent = 0 }, "session.hud.open: --font-size must be 6...72 points" },
        { new ControlArgs { Message = "m", SizePercent = 0, HideAfter = -1 }, "session.hud.open: --size-percent must be 1...100" },
        { new ControlArgs { Message = "m", HideAfter = -1, Position = "nowhere" }, "session.hud.open: --hide-after must be 0...86400 seconds" },
        { new ControlArgs { Message = "m", Position = "nowhere", Spinner = "swirl" }, $"invalid position: nowhere ({PositionNames})" },
        { new ControlArgs { Message = "m", Spinner = "swirl", Markdown = true }, "invalid spinner: swirl (bar|braille|circle|blocks|dot|none)" },
    };

    [Theory]
    [MemberData(nameof(NeighborRules))]
    public async Task TheEarlierOfTwoBrokenRulesAnswers(ControlArgs args, string error) => AssertRejected(error, await Open(args));

    [Fact]
    public async Task UpdateRefusesAFontSizeBeforeTheSizePercent() =>
        AssertRejected("session.hud.update: --font-size is fixed at open; reopen the hud to change it",
            await Update(new ControlArgs { Message = "m", FontSize = 14, SizePercent = 0 }));
}
