using AgTerm.Cli;
using AgTerm.Cli.Tests.TestSupport;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests.Ported;

// Ported from the session hud cases of agtermctlKitTests CommandsTests.swift (upstream ed831ab),
// plus the help texts of SessionHudCommands.swift, which upstream does not test.
public class HudCommandsTests
{
    private static ControlRequest Hud(params string[] argv) =>
        Agtermctl.ParseAsRoot(new[] { "session", "hud" }.Concat(argv).ToArray()).MakeRequest();

    private static string? Rejection(params string[] argv)
    {
        try
        {
            Agtermctl.ParseAsRoot(new[] { "session", "hud" }.Concat(argv).ToArray());
            return null;
        }
        catch (Exception e)
        {
            return Agtermctl.Message(e);
        }
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hud-{Guid.NewGuid():N}.txt");

        public TempFile(byte[] content) => File.WriteAllBytes(Path, content);

        public TempFile(string content) : this(System.Text.Encoding.UTF8.GetBytes(content)) { }

        public void Dispose() => File.Delete(Path);
    }

    [Fact]
    public void OpenIsTheDefaultSubcommand() =>
        Assert.Equal("""{"cmd":"session.hud.open","target":"active","args":{"message":"gathering options"}}""",
            ControlJson.Serialize(Hud("gathering options")));

    [Fact]
    public void OpenWithEveryUpstreamOption()
    {
        // --background-color maps to ControlArgs.color, as the overlay group does
        var expected = new ControlRequest
        {
            Cmd = WireCommand.SessionHudOpen, Target = "9f3c",
            Args = new ControlArgs
            {
                SizePercent = 40, Message = "building index", Detail = "3 of 12 repos", Spinner = "bar",
                Window = "w1", Color = "#2a1a3a", Position = "top",
            },
        };
        CanonicalJson.AssertEqual(expected, Hud("building index", "--detail", "3 of 12 repos", "--spinner",
            "--position", "top", "--background-color", "#2a1a3a", "--size-percent", "40",
            "--target", "9f3c", "--window", "w1"));
    }

    [Fact]
    public void EveryOpenOptionReachesTheWireInSwiftKeyOrder() =>
        Assert.Equal(
            """{"cmd":"session.hud.open","target":"9f3c","args":{"color":"#2a1a3a","textColor":"#e0e0e0","position":"top","pane":"right","paneID":"tok","sizePercent":40,"message":"building index","detail":"3 of 12 repos","spinner":"braille","hideAfter":2.5,"markdown":true,"sticky":true,"frame":false,"window":"w1","fontSize":18}}""",
            ControlJson.Serialize(Hud("building index", "--detail", "3 of 12 repos", "--spinner-style", "braille",
                "--position", "top", "--background-color", "#2a1a3a", "--text-color", "#e0e0e0",
                "--size-percent", "40", "--sticky", "--no-frame", "--markdown", "--font-size", "18",
                "--pane", "right", "--pane-id", "tok", "--hide-after", "2.5", "--target", "9f3c", "--window", "w1")));

    [Fact]
    public void EveryUpdateOptionReachesTheWireInSwiftKeyOrder() =>
        Assert.Equal(
            """{"cmd":"session.hud.update","target":"9f3c","args":{"textColor":"#e0e0e0","position":"bottom-left","pane":"left","paneID":"tok","sizePercent":25,"message":"ready","detail":"12 repos","spinner":"bar","hideAfter":0,"markdown":true,"sticky":true,"frame":false,"window":"w1"}}""",
            ControlJson.Serialize(Hud("update", "ready", "--detail", "12 repos", "--spinner",
                "--position", "bottom-left", "--text-color", "#e0e0e0", "--size-percent", "25", "--sticky",
                "--no-frame", "--markdown", "--pane", "left", "--pane-id", "tok", "--hide-after", "0",
                "--target", "9f3c", "--window", "w1")));

    [Fact]
    public void OpenOmitsEveryUnsetField() =>
        Assert.Equal("""{"message":"wait"}""", ControlJson.Serialize(Hud("wait").Args));

    [Fact]
    public void UpdateOmitsEveryUnsetField() =>
        Assert.Equal("""{"message":"wait"}""", ControlJson.Serialize(Hud("update", "wait").Args));

    // the bare flag resolves to the default style here, so the socket only ever carries a name
    [Fact]
    public void ABareSpinnerFlagSendsTheDefaultStyle() => Assert.Equal("bar", Hud("working", "--spinner").Args!.Spinner);

    [Theory]
    [InlineData("bar")]
    [InlineData("braille")]
    [InlineData("circle")]
    [InlineData("blocks")]
    [InlineData("dot")]
    public void ASpinnerStyleImpliesTheSpinner(string style) =>
        Assert.Equal(style, Hud("working", "--spinner-style", style).Args!.Spinner);

    [Fact]
    public void ASpinnerStyleWinsOverTheBareFlag() =>
        Assert.Equal("braille", Hud("working", "--spinner", "--spinner-style", "braille").Args!.Spinner);

    [Fact]
    public void NoSpinnerFlagSendsNoSpinner() => Assert.Null(Hud("working").Args!.Spinner);

    // `none` is what tree reports for a static panel, so the CLI takes the value tree handed back
    [Theory]
    [InlineData("working", "--spinner-style", "none")]
    [InlineData("working", "--spinner", "--spinner-style", "none")]
    [InlineData("update", "done", "--spinner-style", "none")]
    public void NoneSendsNoSpinnerEvenNextToTheBareFlag(params string[] argv) => Assert.Null(Hud(argv).Args!.Spinner);

    [Fact]
    public void UpdateSwitchesTheStyleInPlace() =>
        Assert.Equal("dot", Hud("update", "still working", "--spinner-style", "dot").Args!.Spinner);

    [Theory]
    [InlineData(false, null, null)]
    [InlineData(true, null, "bar")]
    [InlineData(false, "dot", "dot")]
    [InlineData(true, "circle", "circle")]
    [InlineData(false, "none", null)]
    [InlineData(true, "none", null)]
    public void SpinnerValueResolvesTheTwoWaysToAskForOne(bool spinner, string? style, string? expected) =>
        Assert.Equal(expected, HudCommands.SpinnerValue(spinner, style));

    [Theory]
    [InlineData("open")]
    [InlineData("update")]
    [InlineData("close")]
    public void AnExplicitOpenPostsAMessageNamedLikeASubcommand(string message) =>
        Assert.Equal($$$"""{"cmd":"session.hud.open","target":"active","args":{"message":"{{{message}}}"}}""",
            ControlJson.Serialize(Hud("open", message)));

    [Theory]
    [InlineData("top-left")]
    [InlineData("top-center")]
    [InlineData("top-right")]
    [InlineData("center-left")]
    [InlineData("center")]
    [InlineData("center-right")]
    [InlineData("bottom-left")]
    [InlineData("bottom-center")]
    [InlineData("bottom-right")]
    public void OpenAcceptsEveryPosition(string position) =>
        Assert.Equal(position, Hud("wait", "--position", position).Args!.Position);

    // the CLI takes everything the socket takes and sends the alias as typed; the server resolves it
    [Theory]
    [InlineData("top")]
    [InlineData("bottom")]
    public void ThePositionAliasesAreSentAsTyped(string alias)
    {
        Assert.Equal(alias, Hud("wait", "--position", alias).Args!.Position);
        Assert.Equal(alias, Hud("update", "wait", "--position", alias).Args!.Position);
    }

    [Theory]
    [InlineData("middle")]
    [InlineData("Top")]
    [InlineData("")]
    public void ABadPositionIsRefused(string position)
    {
        const string expected = "position must be one of: top-left, top-center, top-right, center-left, center, center-right, bottom-left, bottom-center, bottom-right, top, bottom";
        Assert.Equal(expected, Rejection("wait", "--position", position));
        Assert.Equal(expected, Rejection("update", "wait", "--position", position));
    }

    [Fact]
    public void AnUnknownSpinnerStyleIsRefusedBeforeTheSocket()
    {
        const string expected = "spinner style must be one of: bar, braille, circle, blocks, dot, none";
        Assert.Equal(expected, Rejection("working", "--spinner-style", "swirl"));
        Assert.Equal(expected, Rejection("update", "working", "--spinner-style", "Bar"));
    }

    [Fact]
    public void OpenAndUpdateCarryThePaneSelectors()
    {
        var open = Hud("wait", "--pane", "right", "--pane-id", "stable-token").Args!;
        var update = Hud("update", "done", "--pane", "left", "--pane-id", "stable-token").Args!;
        Assert.Equal(("right", "stable-token"), (open.Pane, open.PaneId));
        Assert.Equal(("left", "stable-token"), (update.Pane, update.PaneId));
    }

    [Theory]
    [InlineData("primary")]
    [InlineData("top")]
    [InlineData("split")]
    [InlineData("bottom")]
    public void ThePaneAliasesAreSentAsTyped(string pane) => Assert.Equal(pane, Hud("wait", "--pane", pane).Args!.Pane);

    [Theory]
    [InlineData("scratch")]
    [InlineData("middle")]
    public void AnInvalidPaneIsRefused(string pane)
    {
        Assert.Equal("--pane must be left or right", Rejection("wait", "--pane", pane));
        Assert.Equal("--pane must be left or right", Rejection("update", "done", "--pane", pane));
    }

    [Fact]
    public void OpenAndUpdateCarryTheTextColor()
    {
        Assert.Equal("#7ec07e", Hud("wait", "--text-color", "#7ec07e").Args!.TextColor);
        Assert.Equal("#e0e0e0", Hud("update", "done", "--text-color", "#e0e0e0").Args!.TextColor);
    }

    [Fact]
    public void ABadTextColorIsRefused()
    {
        Assert.Equal("text-color must be a #rrggbb hex value", Rejection("wait", "--text-color", "7ec07"));
        Assert.Equal("text-color must be a #rrggbb hex value", Rejection("update", "done", "--text-color", "#gggggg"));
    }

    [Fact]
    public void ABadBackgroundColorIsRefused() =>
        Assert.Equal("background-color must be a #rrggbb hex value", Rejection("wait", "--background-color", "purple"));

    // a color without '#' is valid, as for session background, and goes to the wire as typed
    [Fact]
    public void ABackgroundColorWithoutAHashIsSentAsTyped() =>
        Assert.Equal("2a1a3a", Hud("wait", "--background-color", "2a1a3a").Args!.Color);

    [Fact]
    public void TheFlagsAreSentAsTrueOrFalseOrOmitted()
    {
        foreach (var verb in new[] { new[] { "t" }, new[] { "update", "t" } })
        {
            var set = Hud(verb.Concat(new[] { "--markdown", "--sticky", "--no-frame" }).ToArray()).Args!;
            Assert.Equal<(bool?, bool?, bool?)>((true, true, false), (set.Markdown, set.Sticky, set.Frame));
            var unset = Hud(verb).Args!;
            Assert.Equal<(bool?, bool?, bool?)>((null, null, null), (unset.Markdown, unset.Sticky, unset.Frame));
        }
    }

    [Fact]
    public void OpenCarriesAFontSize()
    {
        Assert.Equal(18, Hud("big", "--font-size", "18").Args!.FontSize);
        Assert.Equal(6.5, Hud("small", "--font-size", "6.5").Args!.FontSize);
        Assert.Null(Hud("same").Args!.FontSize);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("73")]
    [InlineData("5.99")]
    [InlineData("inf")]
    public void AFontSizeOutsideTheRangeIsRefused(string points) =>
        Assert.Equal("font-size must be 6...72 points", Rejection("tiny", "--font-size", points));

    [Fact]
    public void UpdateTakesNoFontSizeAndNoBackgroundColor()
    {
        Assert.NotNull(Rejection("update", "done", "--font-size", "14"));
        Assert.NotNull(Rejection("update", "done", "--background-color", "#112233"));
    }

    [Fact]
    public void OpenAndUpdateCarryAHideAfter()
    {
        Assert.Equal(2.5, Hud("wait", "--hide-after", "2.5").Args!.HideAfter);
        Assert.Equal(0, Hud("wait", "--hide-after", "0").Args!.HideAfter);
        Assert.Equal(86400, Hud("update", "wait", "--hide-after", "86400").Args!.HideAfter);
        Assert.Null(Hud("wait").Args!.HideAfter);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("86400.5")]
    [InlineData("inf")]
    public void AHideAfterOutsideTheRangeIsRefused(string seconds)
    {
        Assert.Equal("hide-after must be 0...86400 seconds", Rejection("wait", "--hide-after", seconds));
        Assert.Equal("hide-after must be 0...86400 seconds", Rejection("update", "wait", "--hide-after", seconds));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("150")]
    public void ABadSizePercentIsRefusedOnOpen(string percent) =>
        Assert.Equal("--size-percent must be between 1 and 100", Rejection("wait", "--size-percent", percent));

    [Fact]
    public void ABadSizePercentIsRefusedOnUpdate() =>
        Assert.Equal("--size-percent must be between 1 and 100", Rejection("update", "wait", "--size-percent", "101"));

    // 5 is valid input: the app clamps it to 10 later, so the CLI must not refuse it
    [Fact]
    public void ASmallSizePercentIsSentForTheAppToClamp() =>
        Assert.Equal(5, Hud("wait", "--size-percent", "5").Args!.SizePercent);

    [Fact]
    public void ExactlyOneMessageSourceIsRequired()
    {
        Assert.Equal("provide MESSAGE or --file", Rejection());
        Assert.Equal("provide MESSAGE or --file", Rejection("open"));
        Assert.Equal("provide MESSAGE or --file", Rejection("update"));
        Assert.Equal("MESSAGE and --file are mutually exclusive", Rejection("both", "--file", "x.md"));
        Assert.Equal("MESSAGE and --file are mutually exclusive", Rejection("update", "both", "--file", "x.md"));
    }

    [Theory]
    [InlineData("background-color must be a #rrggbb hex value", "wait", "--background-color", "purple", "--text-color", "nope")]
    [InlineData("text-color must be a #rrggbb hex value", "wait", "--text-color", "nope", "--position", "middle")]
    [InlineData("position must be one of: top-left, top-center, top-right, center-left, center, center-right, bottom-left, bottom-center, bottom-right, top, bottom", "wait", "--position", "middle", "--spinner-style", "swirl")]
    [InlineData("spinner style must be one of: bar, braille, circle, blocks, dot, none", "wait", "--spinner-style", "swirl", "--hide-after", "-1")]
    [InlineData("hide-after must be 0...86400 seconds", "--hide-after", "-1")]
    [InlineData("provide MESSAGE or --file", "--font-size", "5")]
    [InlineData("font-size must be 6...72 points", "wait", "--font-size", "5", "--size-percent", "0")]
    [InlineData("--size-percent must be between 1 and 100", "wait", "--size-percent", "0", "--pane", "scratch")]
    public void OpenChecksInUpstreamOrder(string expected, params string[] argv) => Assert.Equal(expected, Rejection(argv));

    [Theory]
    [InlineData("text-color must be a #rrggbb hex value", "update", "done", "--text-color", "nope", "--position", "middle")]
    [InlineData("position must be one of: top-left, top-center, top-right, center-left, center, center-right, bottom-left, bottom-center, bottom-right, top, bottom", "update", "done", "--position", "middle", "--spinner-style", "swirl")]
    [InlineData("spinner style must be one of: bar, braille, circle, blocks, dot, none", "update", "done", "--spinner-style", "swirl", "--hide-after", "-1")]
    [InlineData("hide-after must be 0...86400 seconds", "update", "--hide-after", "-1")]
    [InlineData("provide MESSAGE or --file", "update", "--size-percent", "0")]
    [InlineData("--size-percent must be between 1 and 100", "update", "done", "--size-percent", "0", "--pane", "scratch")]
    public void UpdateChecksInUpstreamOrder(string expected, params string[] argv) => Assert.Equal(expected, Rejection(argv));

    [Fact]
    public void TheMessageIsReadFromAFile()
    {
        using var file = new TempFile("# Tasks\n\n- build  \n\n");
        Assert.Equal("# Tasks\n\n- build  \n", Hud("--file", file.Path, "--markdown").Args!.Message);
        Assert.Equal("# Tasks\n\n- build  \n", Hud("update", "--file", file.Path).Args!.Message);
    }

    [Fact]
    public void CrlfLineEndingsFromAFileBecomeLf()
    {
        using var file = new TempFile("# Tasks\r\n\r\n- build\r\n");
        Assert.Equal("# Tasks\n\n- build", Hud("--file", file.Path, "--markdown").Args!.Message);
    }

    [Fact]
    public void ACrlfTerminatorIsDroppedFromAPlainFile()
    {
        using var file = new TempFile("done\r\n");
        Assert.Equal("done", Hud("--file", file.Path).Args!.Message);
    }

    [Fact]
    public void ALoneCarriageReturnStaysForTheServerToRefuse()
    {
        using var file = new TempFile("a\rb\n");
        Assert.Equal("a\rb", Hud("--file", file.Path).Args!.Message);
    }

    [Fact]
    public void AFileWithoutATerminatorIsSentWhole()
    {
        using var file = new TempFile("привет");
        Assert.Equal("привет", Hud("--file", file.Path).Args!.Message);
    }

    // Windows editors write a byte order mark; it is not part of the message
    [Fact]
    public void ALeadingByteOrderMarkIsDropped()
    {
        using var file = new TempFile(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes("done\r\n")).ToArray());
        Assert.Equal("done", Hud("--file", file.Path).Args!.Message);
        Assert.Equal("done", Hud("update", "--file", file.Path).Args!.Message);
    }

    [Fact]
    public void AByteOrderMarkInsideTheTextStays()
    {
        using var file = new TempFile("a\uFEFFb");
        Assert.Equal("a\uFEFFb", Hud("--file", file.Path).Args!.Message);
    }

    [Fact]
    public void AnUnreadableFileIsRefusedAtParseTime()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"agterm-hud-missing-{Guid.NewGuid():N}.md");
        var error = Rejection("--file", missing)!;
        var prefix = $"cannot read --file {missing}: ";
        Assert.StartsWith(prefix, error);
        Assert.True(error.Length > prefix.Length, "the reason after the path is the .NET exception message");
        Assert.StartsWith(prefix, Rejection("update", "--file", missing));
    }

    [Fact]
    public void ANonUtf8FileIsRefusedAtParseTime()
    {
        using var file = new TempFile(new byte[] { 0xff, 0xfe, 0x00 });
        Assert.Equal($"--file {file.Path} is not valid UTF-8", Rejection("--file", file.Path));
        Assert.Equal($"--file {file.Path} is not valid UTF-8", Rejection("update", "--file", file.Path));
    }

    // upstream reads the file after every other check, so a bad option wins over a bad file
    [Fact]
    public void TheFileIsReadAfterEveryOtherCheck()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"agterm-hud-missing-{Guid.NewGuid():N}.md");
        Assert.Equal("--pane must be left or right", Rejection("--file", missing, "--pane", "scratch"));
        Assert.Equal("--pane must be left or right", Rejection("update", "--file", missing, "--pane", "scratch"));
    }

    [Fact]
    public void UpstreamUpdateCases()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionHudUpdate, Target = "9f3c",
                Args = new ControlArgs { Message = "ready", Detail = "12 repos", Position = "bottom" },
            },
            Hud("update", "ready", "--detail", "12 repos", "--position", "bottom", "--target", "9f3c"));
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionHudUpdate, Target = "active",
                Args = new ControlArgs { SizePercent = 25, Message = "still working", Spinner = "bar" },
            },
            Hud("update", "still working", "--spinner", "--size-percent", "25"));
    }

    [Fact]
    public void CloseSendsOnlyTheTargetAndTheWindow()
    {
        Assert.Equal("""{"cmd":"session.hud.close","target":"active"}""", ControlJson.Serialize(Hud("close")));
        Assert.Equal("""{"cmd":"session.hud.close","target":"9f3c","args":{"window":"w1"}}""",
            ControlJson.Serialize(Hud("close", "--target", "9f3c", "--window", "w1")));
        Assert.NotNull(Rejection("close", "--position", "top"));
    }

    private static System.CommandLine.Command Group() => HudCommands.Build(new CommandBindings());

    private static System.CommandLine.Command Verb(string name) => Group().Subcommands.Single(c => c.Name == name);

    private static string? HelpOf(System.CommandLine.Command command, string option) =>
        option == "MESSAGE"
            ? command.Arguments.Single().Description
            : command.Options.Single(o => o.Name == option).Description;

    [Fact]
    public void TheCommandDescriptionsAreUpstreams()
    {
        Assert.Equal("Post, update, or close a passive message panel over a session.", Group().Description);
        Assert.Equal("Post a message panel over the session; the session keeps focus and stays typable.", Verb("open").Description);
        Assert.Equal("Replace the panel's text in place (no re-spawn, no blink).", Verb("update").Description);
        Assert.Equal("Take the message panel down (a program overlay in the same slot is left alone).", Verb("close").Description);
    }

    [Theory]
    [InlineData("MESSAGE", "Message shown in the panel (omit with --file).")]
    [InlineData("--file", "Read the message from FILE instead of the argument.")]
    [InlineData("--markdown", "Render the message as markdown, up to 4096 characters. A single newline inside a paragraph is a soft break; end a line with two spaces or a backslash to break it. A link is underlined and opens on a Ctrl+click.")]
    [InlineData("--font-size", "The panel's own font size in points, 6-72; omit to use the session's. Fixed for the panel's life.")]
    [InlineData("--detail", "Dim second line under the message (e.g. what the caller is waiting on).")]
    [InlineData("--spinner", "Animate a spinner glyph in the panel, in the default style.")]
    [InlineData("--spinner-style", "Spinner style: bar, braille, circle, blocks, dot, none (default: bar). Implies --spinner; none leaves the panel static.")]
    [InlineData("--position", "Placement in the pane: top-left, top-center, top-right, center-left, center, center-right, bottom-left, bottom-center, bottom-right (default: center), the same anchors session background takes. Every anchor off center holds a fixed margin at that edge. Here top and bottom are also accepted, for top-center and bottom-center.")]
    [InlineData("--background-color", "Solid background color (#rrggbb) for the panel, independent of the session's own.")]
    [InlineData("--text-color", "Color (#rrggbb) for the panel's text; omit to keep the terminal foreground.")]
    [InlineData("--size-percent", "Set the panel's WIDTH to PERCENT (1-100) of the pane instead of measuring the message; bounded to 10-80, or up to 100 with --sticky off center. Height always follows the message.")]
    [InlineData("--sticky", "Put the panel flush against the edge or corner --position names, with no margin; center has no edge and ignores it. Add --size-percent 100 for a strip across the pane.")]
    [InlineData("--no-frame", "Draw no border, no rounded corners and no blank row above and below the text.")]
    [InlineData("--pane", "Anchor inside primary/left/top or split/right/bottom; omit for the whole session.")]
    [InlineData("--pane-id", "Stable pane token ($AGTERM_PANE_ID); overrides --pane when it resolves.")]
    [InlineData("--hide-after", "Take the panel down by itself after SECONDS, 0...86400; omit or 0 to leave it up until something closes it. The clock runs whether or not the session is on screen.")]
    [InlineData("--target", "Target session/workspace id, unique prefix, or 'active'.")]
    public void OpenHelpIsUpstreams(string option, string expected) => Assert.Equal(expected, HelpOf(Verb("open"), option));

    [Theory]
    [InlineData("MESSAGE", "New message; it replaces the old one entirely (omit with --file).")]
    [InlineData("--file", "Read the new message from FILE instead of the argument.")]
    [InlineData("--markdown", "Render the message as markdown; omit to return the panel to plain text.")]
    [InlineData("--detail", "Dim second line under the message; omit to drop the old one.")]
    [InlineData("--spinner", "Keep (or start) the spinner in the default style; omit to stop it.")]
    [InlineData("--spinner-style", "Switch the spinner to bar, braille, circle, blocks, dot, none; implies --spinner, and repaints the live panel without a re-spawn. none stops it.")]
    [InlineData("--position", "Move the panel to top-left, top-center, top-right, center-left, center, center-right, bottom-left, bottom-center, bottom-right, top, bottom (default: center).")]
    [InlineData("--text-color", "Recolor the panel's text (#rrggbb); omit to return it to the terminal foreground.")]
    [InlineData("--size-percent", "Resize the panel's WIDTH to PERCENT (1-100) of the pane instead of measuring the message; bounded to 10-80, or up to 100 with --sticky off center. Height always follows the message.")]
    [InlineData("--sticky", "Put the panel flush against the edge or corner --position names, with no margin; center has no edge and ignores it. Add --size-percent 100 for a strip across the pane. Omit to return the panel to its edge margin.")]
    [InlineData("--no-frame", "Draw no border, no rounded corners and no blank row above and below the text. Omit to bring the frame back.")]
    [InlineData("--pane", "Anchor inside primary/left/top or split/right/bottom; omit to return to whole-session placement.")]
    [InlineData("--pane-id", "Stable pane token ($AGTERM_PANE_ID); repeat it on update to keep pane scope.")]
    [InlineData("--hide-after", "Restart the panel's auto-hide at SECONDS, 0...86400; omit or 0 to cancel it, like every other option an update replaces rather than patches.")]
    [InlineData("--target", "Target session/workspace id, unique prefix, or 'active'.")]
    public void UpdateHelpIsUpstreams(string option, string expected) => Assert.Equal(expected, HelpOf(Verb("update"), option));

    [Fact]
    public void EachVerbTakesExactlyUpstreamsOptions()
    {
        string[] client = { "--target", "--socket", "--json", "--window" };
        Assert.Equal(new[] { "--file", "--markdown", "--font-size", "--detail", "--spinner", "--spinner-style", "--position",
                "--background-color", "--text-color", "--size-percent", "--sticky", "--no-frame", "--pane", "--pane-id",
                "--hide-after" }.Concat(client),
            Verb("open").Options.Select(o => o.Name));
        Assert.Equal(new[] { "--file", "--markdown", "--detail", "--spinner", "--spinner-style", "--position",
                "--text-color", "--size-percent", "--sticky", "--no-frame", "--pane", "--pane-id", "--hide-after" }.Concat(client),
            Verb("update").Options.Select(o => o.Name));
        Assert.Equal(client, Verb("close").Options.Select(o => o.Name));
        Assert.Empty(Verb("close").Arguments);
    }

    [Fact]
    public void TheGroupTakesOpensArgumentAndOptions()
    {
        var group = Group();
        var open = group.Subcommands.Single(c => c.Name == "open");
        Assert.Equal(open.Arguments.Select(a => (a.Name, a.Description)), group.Arguments.Select(a => (a.Name, a.Description)));
        Assert.Equal(open.Options.Select(o => (o.Name, o.Description)), group.Options.Select(o => (o.Name, o.Description)));
    }

    [Fact]
    public void ValidationErrorsExitSixtyFour()
    {
        var error = new StringWriter();
        Assert.Equal(64, Agtermctl.Run(new[] { "session", "hud", "wait", "--size-percent", "0", "--socket", "isolated" },
            new CliEnvironment { Error = error, Out = new StringWriter(), GetEnv = _ => null }));
        Assert.Equal("error: --size-percent must be between 1 and 100", error.ToString().Trim());
    }

    [Fact]
    public void ASuccessfulOpenPrintsOkNotTheId()
    {
        var output = new StringWriter();
        var sent = new List<ControlRequest>();
        var exit = Agtermctl.Run(new[] { "session", "hud", "wait", "--socket", "isolated" }, new CliEnvironment
        {
            Out = output, Error = new StringWriter(), GetEnv = _ => null,
            Send = (_, request) =>
            {
                sent.Add(request);
                return new ControlResponse { Ok = true, Result = new ControlResult { Id = "SESSION" } };
            },
        });
        Assert.Equal(0, exit);
        Assert.Equal(WireCommand.SessionHudOpen, Assert.Single(sent).Cmd);
        Assert.Equal("ok", output.ToString().Trim());
    }

    // the file is read in Validate, so its failure is a usage error like upstream's ValidationError
    [Fact]
    public void AFileFailureExitsSixtyFourAndSendsNothing()
    {
        var error = new StringWriter();
        var sent = new List<ControlRequest>();
        var missing = Path.Combine(Path.GetTempPath(), $"agterm-hud-missing-{Guid.NewGuid():N}.md");
        var exit = Agtermctl.Run(new[] { "session", "hud", "--file", missing, "--socket", "isolated" }, new CliEnvironment
        {
            Out = new StringWriter(), Error = error, GetEnv = _ => null,
            Send = (_, request) =>
            {
                sent.Add(request);
                return new ControlResponse { Ok = true };
            },
        });
        Assert.Equal(64, exit);
        Assert.Empty(sent);
        Assert.StartsWith($"error: cannot read --file {missing}: ", error.ToString());
    }
}
