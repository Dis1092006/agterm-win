using System.Text;
using AgTerm.Cli;

namespace AgTerm.Cli.Tests.Ported;

// Port of agtermctlKitTests/ClipboardCommandsTests.swift (upstream fbe72e25, #722). Upstream prints an
// OSC 52 write into the pane through zmx; agterm-win has no zmx, so the copy goes to the Windows
// clipboard of the machine the CLI runs on. Parsing and the empty/too-large refusals are upstream's.
public class ClipboardCommandsTests
{
    private static ClipboardCommands.Set Parse(params string[] argv) =>
        (ClipboardCommands.Set)Agtermctl.ParseAsRootAny(new[] { "clipboard", "set" }.Concat(argv).ToArray());

    private sealed class Harness
    {
        public StringWriter Out { get; } = new();
        public StringWriter Error { get; } = new();
        public List<string> Copied { get; } = new();
        public Exception? ClipboardFailure { get; set; }

        public CliEnvironment Environment => new()
        {
            Out = Out,
            Error = Error,
            SetClipboard = text =>
            {
                if (ClipboardFailure is not null) throw ClipboardFailure;
                Copied.Add(text);
            },
        };
    }

    private static ClipboardCommands.Set WithStdin(string[] argv, byte[] stdin, bool terminal = false) =>
        new()
        {
            Text = Parse(argv).Text,
            StdinStream = () => new MemoryStream(stdin),
            StdinIsTerminal = () => terminal,
        };

    [Fact]
    public void SetTakesItsTextAsTheArgument() =>
        Assert.Equal("hello world", Parse("hello world").Text);

    [Fact]
    public void SetWithoutAnArgumentLeavesTheTextToStandardInput() =>
        Assert.Null(Parse().Text);

    [Theory]
    [InlineData("--force")]
    [InlineData("- item one")]
    [InlineData("-1")]
    public void SetTakesDashLeadingTextOnlyAfterATerminator(string text)
    {
        Assert.ThrowsAny<Exception>(() => Parse(text));
        Assert.Equal(text, Parse("--", text).Text);
    }

    [Theory]
    [InlineData("one", "two")]
    [InlineData("--socket", "/tmp/s")]
    [InlineData("--target", "active")]
    [InlineData("--json")]
    public void SetRefusesASecondArgumentAndEveryControlOption(params string[] argv) =>
        Assert.ThrowsAny<Exception>(() => Parse(argv));

    [Fact]
    public void SetIsNotAControlCommand()
    {
        Assert.IsNotAssignableFrom<IRequestCommand>(
            Agtermctl.ParseAsRootAny(new[] { "clipboard", "set", "hello" }));
        Assert.Throws<CliParseException>(() => Agtermctl.ParseAsRoot(new[] { "clipboard", "set", "hello" }));
    }

    [Fact]
    public void EveryFailureHasItsOwnMessage()
    {
        Assert.Equal("nothing to copy", TerminalClipboard.Failure.EmptyText.Message);
        Assert.Equal(
            "text is too large to copy through the terminal: 9000000 bytes encoded, the limit is 8388544",
            TerminalClipboard.Failure.TooLarge(9_000_000).Message);
        Assert.Equal(
            "text is not valid UTF-8; the Windows clipboard holds Unicode text",
            TerminalClipboard.Failure.InvalidUtf8.Message);
        Assert.Equal(
            "could not set the Windows clipboard: error 5",
            TerminalClipboard.Failure.Unavailable(5).Message);
    }

    [Fact]
    public void ExecuteCopiesTheArgumentToTheClipboard()
    {
        var app = new Harness();
        Assert.Equal(0, Parse("hello world").Execute(app.Environment));
        Assert.Equal(new[] { "hello world" }, app.Copied);
    }

    [Fact]
    public void ExecuteCopiesStandardInputByteForByte()
    {
        var app = new Harness();
        var command = WithStdin(Array.Empty<string>(), Encoding.UTF8.GetBytes("line one\r\nline two\n"));
        Assert.Equal(0, command.Execute(app.Environment));
        Assert.Equal(new[] { "line one\r\nline two\n" }, app.Copied);
    }

    [Fact]
    public void ExecuteRefusesAnInteractiveStandardInputWithoutText()
    {
        var app = new Harness();
        var command = WithStdin(Array.Empty<string>(), Array.Empty<byte>(), terminal: true);
        var error = Assert.Throws<CliValidationException>(() => command.Execute(app.Environment));
        Assert.Equal("give the text as an argument or pipe it on standard input", error.Message);
        Assert.Empty(app.Copied);
    }

    [Fact]
    public void ExecuteRefusesEmptyText()
    {
        var app = new Harness();
        var command = WithStdin(Array.Empty<string>(), Array.Empty<byte>());
        var error = Assert.Throws<CliValidationException>(() => command.Execute(app.Environment));
        Assert.Equal("nothing to copy", error.Message);
        Assert.Empty(app.Copied);
    }

    [Fact]
    public void ExecuteRefusesTextWhoseEncodingPassesTheLimit()
    {
        var app = new Harness();
        // base64 of 6291456 bytes is 8388608 characters, over the 8388544 limit
        var command = WithStdin(Array.Empty<string>(), new byte[6_291_456].Select(_ => (byte)'a').ToArray());
        var error = Assert.Throws<CliValidationException>(() => command.Execute(app.Environment));
        Assert.Equal(
            "text is too large to copy through the terminal: 8388608 bytes encoded, the limit is 8388544",
            error.Message);
        Assert.Empty(app.Copied);
    }

    [Fact]
    public void ExecuteRefusesInvalidUtf8()
    {
        var app = new Harness();
        var command = WithStdin(Array.Empty<string>(), new byte[] { 0xFF, 0xFE, 0x41 });
        var error = Assert.Throws<CliValidationException>(() => command.Execute(app.Environment));
        Assert.Equal("text is not valid UTF-8; the Windows clipboard holds Unicode text", error.Message);
    }

    [Fact]
    public void ExecuteReportsAClipboardThatCannotBeSet()
    {
        var app = new Harness
        {
            ClipboardFailure = new TerminalClipboard.FailureException(TerminalClipboard.Failure.Unavailable(5)),
        };
        var error = Assert.Throws<CliValidationException>(() => Parse("x").Execute(app.Environment));
        Assert.Equal("could not set the Windows clipboard: error 5", error.Message);
    }
}
