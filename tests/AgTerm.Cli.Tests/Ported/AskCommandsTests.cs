using AgTerm.Cli;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests.Ported;

// Ported from agtermctlKitTests AskCommandsTests.swift parse and validation cases (upstream ed831ab).
public class AskCommandsTests
{
    private static AskCommands.Open Open(params string[] argv) =>
        Assert.IsType<AskCommands.Open>(Agtermctl.ParseAsRoot(new[] { "ask" }.Concat(argv).ToArray()));

    private static string Rejection(params string[] argv)
    {
        var error = Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "ask" }.Concat(argv).ToArray()));
        return Agtermctl.Message(error);
    }

    private static readonly ControlAskButton Yes = new() { Id = "yes", Label = "Yes" };

    [Fact]
    public void TheRootDefaultsToABlockingOpenWithoutATarget()
    {
        var command = Open("Continue?", "--button", "yes=Yes");
        var request = command.MakeRequest();

        Assert.Equal(WireCommand.AskOpen, request.Cmd);
        Assert.Null(request.Target);
        Assert.Equal(new[] { Yes }, request.Args!.Buttons);
        Assert.Equal("terminal", request.Args.Style);
        Assert.Equal("right", request.Args.Align);
        Assert.Equal("Continue?", request.Args.Title);
        Assert.False(command.NoBlock);
        Assert.Equal("{\"cmd\":\"ask.open\",\"args\":{\"title\":\"Continue?\",\"buttons\":[{\"id\":\"yes\",\"label\":\"Yes\"}],\"style\":\"terminal\",\"align\":\"right\"}}",
            ControlJson.Serialize(request));
    }

    [Theory]
    [InlineData("right", null, false)]
    [InlineData(null, "pane-token", false)]
    [InlineData("left", "pane-token", false)]
    [InlineData("right", null, true)]
    [InlineData(null, "pane-token", true)]
    [InlineData("left", "pane-token", true)]
    public void TerminalPanePlacementBuildsARequestWithoutATarget(string? pane, string? paneId, bool explicitStyle)
    {
        var argv = new List<string> { "Continue?", "--button", "yes=Yes", "--window", "window-id" };
        if (pane is not null) argv.AddRange(new[] { "--pane", pane });
        if (paneId is not null) argv.AddRange(new[] { "--pane-id", paneId });
        if (explicitStyle) argv.AddRange(new[] { "--style", "terminal" });

        var request = Open(argv.ToArray()).MakeRequest();

        Assert.Null(request.Target);
        Assert.Equal(pane, request.Args!.Pane);
        Assert.Equal(paneId, request.Args.PaneId);
        Assert.Equal("window-id", request.Args.Window);
        Assert.Equal("terminal", request.Args.Style);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void FixedWidthMapsToTheWire(int width) =>
        Assert.Equal(width, Open("Choose", "--button", "ok", "--width", width.ToString()).MakeRequest().Args!.Width);

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("9")]
    [InlineData("101")]
    [InlineData("50.5")]
    [InlineData("wide")]
    [InlineData("")]
    [InlineData("999999999999999999999")]
    public void InvalidWidthIsRefused(string width) =>
        Assert.Equal("width must be 10 to 100", Rejection("Choose", "--button", "ok", "--width", width));

    [Fact]
    public void EveryOpenOptionMapsToTheWire()
    {
        var command = Open(
            "Keep changes?", "--message", "Choose an action.", "--button", "save=Save=keep",
            "--button", "no", "--button", "discard=Discard", "--hotkey", "save=S", "--hotkey", "no=n",
            "--default", "save", "--destructive", "discard", "--style", "gui", "--align", "left",
            "--target", "session-id", "--pane", "right", "--pane-id", "pane-token", "--window", "window-id",
            "--follow", "--no-block", "--socket", "ask-cli", "--json");

        var request = command.MakeRequest();

        Assert.Equal("session-id", request.Target);
        var args = request.Args!;
        Assert.True(args.Follow);
        Assert.Equal("Choose an action.", args.Message);
        Assert.Equal(new[]
        {
            new ControlAskButton { Id = "save", Label = "Save=keep", Hotkey = "S" },
            new ControlAskButton { Id = "no", Label = "no", Hotkey = "n" },
            new ControlAskButton { Id = "discard", Label = "Discard" },
        }, args.Buttons);
        Assert.Equal("save", args.DefaultButton);
        Assert.Equal("discard", args.DestructiveButton);
        Assert.Equal("gui", args.Style);
        Assert.Equal("left", args.Align);
        Assert.Equal("window-id", args.Window);
        Assert.Equal("right", args.Pane);
        Assert.Equal("pane-token", args.PaneId);
        Assert.Equal("Keep changes?", args.Title);
        Assert.True(command.NoBlock);
        Assert.True(command.Options.Json);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("result")]
    [InlineData("cancel")]
    public void AnExplicitOpenAcceptsReservedWordsAsTitles(string title) =>
        Assert.Equal(title, Open("open", title, "--button", "ok").MakeRequest().Args!.Title);

    [Theory]
    [InlineData("unknown style", "Question", "--button", "ok", "--style", "other")]
    [InlineData("unknown align", "Question", "--button", "ok", "--align", "other")]
    [InlineData("ask requires at least one --button", "Question")]
    [InlineData("ask.open requires a title", " ", "--button", "ok")]
    [InlineData("--hotkey requires ID=LETTER", "Question", "--button", "ok", "--hotkey", "o")]
    [InlineData("unknown hotkey button: missing", "Question", "--button", "ok", "--hotkey", "missing=x")]
    [InlineData("hotkey already assigned to button: ok", "Question", "--button", "ok", "--hotkey", "ok=o", "--hotkey", "ok=k")]
    [InlineData("--pane requires a session", "Question", "--button", "ok", "--style", "gui", "--pane", "right")]
    [InlineData("--pane requires a session", "Question", "--button", "ok", "--style", "gui", "--pane-id", "token")]
    [InlineData("--pane requires a session", "Question", "--button", "ok", "--style", "gui", "--pane", "left", "--pane-id", "token")]
    [InlineData("--pane must be left or right", "Question", "--button", "ok", "--target", "active", "--pane", "scratch")]
    [InlineData("--hotkey requires --button", "Question", "--input", "line", "--hotkey", "ok=o")]
    [InlineData("--hotkey requires --button", "Question", "--choice", "a", "--hotkey", "ok=o")]
    [InlineData("--choice-subtitle requires --choice", "Question", "--button", "ok", "--choice-subtitle", "a=x")]
    [InlineData("--choice-subtitle requires ID=TEXT", "Question", "--choice", "a", "--choice-subtitle", "a")]
    [InlineData("unknown subtitle choice: b", "Question", "--choice", "a", "--choice-subtitle", "b=x")]
    [InlineData("subtitle already set for choice: a", "Question", "--choice", "a", "--choice-subtitle", "a=x", "--choice-subtitle", "a=y")]
    public void InvalidSyntaxIsRefused(string expected, params string[] argv) => Assert.Equal(expected, Rejection(argv));

    [Fact]
    public void ChoiceFormMapsItemsSubtitlesAndChecks()
    {
        var request = Open("Review", "--choice", "398=issue #398", "--choice", "395", "--choice-subtitle", "398=waits on you",
            "--multiple", "--checked", "395", "--checked", "398").MakeRequest();
        var args = request.Args!;

        Assert.Null(args.Buttons);
        Assert.Equal(new[]
        {
            new ControlAskChoice { Id = "398", Label = "issue #398", Subtitle = "waits on you" },
            new ControlAskChoice { Id = "395", Label = "395" },
        }, args.Choices);
        Assert.True(args.Multiple);
        Assert.Equal(new[] { "395", "398" }, args.Checked);
    }

    [Fact]
    public void ARadioListSendsNeitherMultipleNorAnEmptyCheckedList()
    {
        var request = Open("Pick", "--choice", "a=A=1", "--choice-subtitle", "a=x=y").MakeRequest();

        Assert.Equal("{\"cmd\":\"ask.open\",\"args\":{\"title\":\"Pick\",\"style\":\"terminal\",\"align\":\"right\",\"choices\":[{\"id\":\"a\",\"label\":\"A=1\",\"subtitle\":\"x=y\"}]}}",
            ControlJson.Serialize(request));
    }

    [Fact]
    public void ASubtitleMatchesACanonicallyEquivalentChoiceId() =>
        Assert.Equal("x", Open("Pick", "--choice", "caf\u00E9", "--choice-subtitle", "cafe\u0301=x").MakeRequest().Args!.Choices![0].Subtitle);

    [Fact]
    public void InputFormNeedsNoButtonsAndMapsItsOptions()
    {
        var request = Open("Name", "--input", "multiline", "--value", "draft", "--placeholder", "type here", "--cancel", "cancel").MakeRequest();

        Assert.Null(request.Args!.Buttons);
        Assert.Equal("{\"cmd\":\"ask.open\",\"args\":{\"title\":\"Name\",\"style\":\"terminal\",\"align\":\"right\",\"cancelButton\":\"cancel\",\"input\":\"multiline\",\"placeholder\":\"type here\",\"value\":\"draft\"}}",
            ControlJson.Serialize(request));
    }

    [Fact]
    public void FormKeepsCallerButtons()
    {
        var args = Open("Name", "--input", "line", "--button", "save=Save", "--button", "skip=Skip", "--cancel", "skip").MakeRequest().Args!;

        Assert.Equal(new[] { "save", "skip" }, args.Buttons!.Select(button => button.Id));
        Assert.Equal("skip", args.CancelButton);
        Assert.Equal("line", args.Input);
        Assert.Null(args.Multiple);
        Assert.Null(args.Checked);
        Assert.Null(args.Choices);
    }

    [Fact]
    public void APlainAskMapsItsCancelButton() =>
        Assert.Equal("no", Open("Q", "--button", "yes", "--button", "no", "--cancel", "no").MakeRequest().Args!.CancelButton);

    [Fact]
    public void OpenHelpExplainsTheForms()
    {
        var open = AskCommands.Build(new CommandBindings()).Subcommands.Single(c => c.Name == "open");
        string? Help(string option) => open.Options.Single(o => o.Name == option).Description;

        Assert.Equal("Ask a question and wait for its button answer. --input adds a text field and --choice a radio list (checkboxes with --multiple); either form defaults its buttons to ok=OK and cancel=Cancel, Cancel ending like Esc. The answer carries the field's text or the selected ids.",
            open.Description);
        Assert.Equal("ID of the button that ends the dialog like Esc, returning nothing.", Help("--cancel"));
        Assert.Equal("Add a text field: line or multiline.", Help("--input"));
        Assert.Equal("Initial text of the --input field.", Help("--value"));
        Assert.Equal("Hint shown while the --input field is empty.", Help("--placeholder"));
        Assert.Equal("List item ID=LABEL, or one token for both. Repeat for each item (max 50).", Help("--choice"));
        Assert.Equal("Item description ID=TEXT, drawn dimmed under its label.", Help("--choice-subtitle"));
        Assert.Equal("Make the --choice list checkboxes instead of radio buttons.", Help("--multiple"));
        Assert.Equal("ID of an item selected at open. Repeat with --multiple.", Help("--checked"));
    }

    [Fact]
    public void ResultAndCancelRouteTheIdAndWindow()
    {
        var result = Assert.IsType<AskCommands.Result>(Agtermctl.ParseAsRoot(new[] { "ask", "result", "ask-id", "--window", "w" }));
        var cancel = Assert.IsType<AskCommands.Cancel>(Agtermctl.ParseAsRoot(new[] { "ask", "cancel", "ask-id", "--window", "w" }));
        Assert.Equal("{\"cmd\":\"ask.result\",\"target\":\"ask-id\",\"args\":{\"window\":\"w\"}}", ControlJson.Serialize(result.MakeRequest()));
        Assert.Equal("{\"cmd\":\"ask.cancel\",\"target\":\"ask-id\",\"args\":{\"window\":\"w\"}}", ControlJson.Serialize(cancel.MakeRequest()));
    }

    [Fact]
    public void ValidationErrorsExitSixtyFour()
    {
        var error = new StringWriter();
        Assert.Equal(64, Agtermctl.Run(new[] { "ask", "Q", "--button", "ok", "--width", "-1", "--socket", "isolated" },
            new CliEnvironment { Error = error, Out = new StringWriter(), GetEnv = _ => null }));
        Assert.Equal("error: width must be 10 to 100", error.ToString().Trim());
    }
}
