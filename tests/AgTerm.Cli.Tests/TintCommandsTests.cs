using AgTerm.Cli;
using AgTerm.Cli.Tests.TestSupport;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests;

// session tint, workspace tint and the creation tint of session new / workspace new
// (parity/adopted.json, upstream f3fea57b TintCommandsTests)
public class TintCommandsTests
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

    [Fact]
    public void SessionTintSetsAColorWithoutPlacement()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionTint, Target = "s1",
                Args = new ControlArgs { Mode = "set", Tint = "red" },
            },
            Request("session", "tint", "Red", "--target", "s1"));
    }

    [Fact]
    public void SessionTintSplitsTheOnList()
    {
        Assert.Equal(["icon", "name"], Request("session", "tint", "blue", "--on", "icon,name").Args!.TintOn);
    }

    [Fact]
    public void SessionTintClears()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionTint, Target = "active",
                Args = new ControlArgs { Mode = "clear" },
            },
            Request("session", "tint", "clear"));
    }

    [Fact]
    public void WorkspaceTintTargetsTheWorkspaceCommand()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.WorkspaceTint, Target = "w1",
                Args = new ControlArgs { Mode = "set", Tint = "gray", TintOn = ["name"], Window = "win" },
            },
            Request("workspace", "tint", "gray", "--on", "name", "--target", "w1", "--window", "win"));
    }

    [Theory]
    [InlineData("session|tint|teal", "tint must be one of: red|orange|yellow|green|blue|purple|gray, or clear")]
    [InlineData("session|tint|clear|--on|icon", "--on does not apply to clear")]
    [InlineData("session|tint|red|--on|icon,strip", "--on must list icon and/or name, comma-separated")]
    [InlineData("workspace|tint|red|--on|", "--on must list icon and/or name, comma-separated")]
    public void TintRejectsBadInputAtParseTime(string argv, string error)
    {
        Assert.Equal(error, ValidationMessage(argv.Split('|')));
    }

    [Fact]
    public void SessionNewCarriesTheCreationTint()
    {
        var args = Request("session", "new", "--tint", "Green", "--tint-on", "icon,name").Args!;

        Assert.Equal("green", args.Tint);
        Assert.Equal(["icon", "name"], args.TintOn);
    }

    [Fact]
    public void WorkspaceNewCarriesTheCreationTint()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.WorkspaceNew,
                Args = new ControlArgs { Name = "prod", Tint = "red" },
            },
            Request("workspace", "new", "prod", "--tint", "red"));
    }

    [Theory]
    [InlineData("session|new|--tint-on|icon", "--tint-on requires --tint")]
    [InlineData("workspace|new|--tint|teal", "--tint must be one of: red|orange|yellow|green|blue|purple|gray")]
    [InlineData("workspace|new|--tint|red|--tint-on|both", "--tint-on must list icon and/or name, comma-separated")]
    public void CreationTintRejectsBadInputAtParseTime(string argv, string error)
    {
        Assert.Equal(error, ValidationMessage(argv.Split('|')));
    }
}
