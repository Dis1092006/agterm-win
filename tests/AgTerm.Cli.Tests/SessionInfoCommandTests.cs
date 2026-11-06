using AgTerm.Cli;
using AgTerm.Cli.Tests.TestSupport;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests;

// session info: the command taken from upstream after the pin (parity/adopted.json, 80f822bf)
public class SessionInfoCommandTests
{
    private static ControlRequest Request(params string[] argv) =>
        Agtermctl.ParseAsRoot(argv).MakeRequest();

    private static string? ValidationMessage(params string[] argv)
    {
        try
        {
            Agtermctl.ParseAsRoot(argv);
            return null;
        }
        catch (Exception e)
        {
            return Agtermctl.Message(e);
        }
    }

    private static System.CommandLine.Command Info() =>
        SessionCommands.Build(new CommandBindings()).Subcommands.Single(c => c.Name == "info");

    [Fact]
    public void SetsMarkdownAndHideAfterVerbatim()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionInfo, Target = "s1",
                Args = new ControlArgs { Mode = "set", Text = "**3** ^[ok](color: \"green\")", Markdown = true, HideAfter = 30, Window = "w1" },
            },
            Request("session", "info", "**3** ^[ok](color: \"green\")", "--markdown", "--hide-after", "30",
                "--target", "s1", "--window", "w1"));
    }

    [Fact]
    public void DefaultsToPlainUntimedOnTheActiveSession()
    {
        var request = Request("session", "info", "**not bold**");

        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionInfo, Target = "active",
                Args = new ControlArgs { Mode = "set", Text = "**not bold**" },
            },
            request);
        Assert.Null(request.Args!.Markdown);
        Assert.Null(request.Args!.HideAfter);
    }

    [Fact]
    public void Clears()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionInfo, Target = "active",
                Args = new ControlArgs { Mode = "clear" },
            },
            Request("session", "info", "--clear"));
    }

    [Theory]
    [InlineData("provide exactly one of a TEXT or --clear", "session", "info")]
    [InlineData("provide exactly one of a TEXT or --clear", "session", "info", "x", "--clear")]
    [InlineData("--clear takes no --markdown or --hide-after", "session", "info", "--clear", "--markdown")]
    [InlineData("--clear takes no --markdown or --hide-after", "session", "info", "--clear", "--hide-after", "5")]
    [InlineData("hide-after must be 0...86400 seconds", "session", "info", "x", "--hide-after=-1")]
    [InlineData("hide-after must be 0...86400 seconds", "session", "info", "x", "--hide-after", "-1")]
    [InlineData("hide-after must be 0...86400 seconds", "session", "info", "x", "--hide-after", "86401")]
    public void RejectsBadFormsAtParseTime(string message, params string[] argv) =>
        Assert.Equal(message, ValidationMessage(argv));

    [Fact]
    public void TakesExactlyUpstreamsOptions()
    {
        Assert.Equal(["--clear", "--markdown", "--hide-after", "--target", "--socket", "--json", "--window"],
            Info().Options.Select(o => o.Name));
        Assert.Equal(["text"], Info().Arguments.Select(a => a.Name));
    }

    [Theory]
    [InlineData("TEXT", "The line (omit with --clear); max 512 UTF-8 bytes, one visible line.")]
    [InlineData("--clear", "Remove the line.")]
    [InlineData("--markdown", "Read TEXT as inline markdown with ^[text](color: \"NAME\") and ^[text](dim: true).")]
    [InlineData("--hide-after", "Clear the line by itself after SECONDS, 0...86400, 0 for never; each set restarts it. Use a positive value and refresh activity lines so they expire if the caller stops.")]
    public void HelpIsUpstreams(string option, string expected) =>
        Assert.Equal(expected, option == "TEXT"
            ? Info().Arguments.Single().Description
            : Info().Options.Single(o => o.Name == option).Description);

    [Fact]
    public void TheDescriptionIsUpstreams()
    {
        Assert.Equal(
            "Set or clear the info line drawn under a session's sidebar row.\n\n"
            + "The line shows current activity, such as a status or a count. Each set replaces the whole line: "
            + "omitting --markdown sends plain text and omitting --hide-after cancels an earlier expiry. It is not "
            + "kept across a relaunch. Settings > Interface > Session info picks where it shows: Always (default), "
            + "Flagged (the flagged view only) or Off; that hides it, never clears it.\n"
            + "Markdown example: '**3** agents ^[running](color: \"green\") ^[idle](dim: true)'. Colors: "
            + TintColors.ValidNamesList + "; the value must be quoted.",
            Info().Description);
    }
}
