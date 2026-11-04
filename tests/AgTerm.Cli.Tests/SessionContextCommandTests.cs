using AgTerm.Cli;
using AgTerm.Cli.Tests.TestSupport;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests;

// session context: the command taken from upstream after the pin (parity/adopted.json, 73b63b71)
public class SessionContextCommandTests
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
    public void SetSendsTheTextVerbatimToTheActiveSession()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionContext, Target = "active",
                Args = new ControlArgs { Mode = "set", Text = "  PR #517: reap ordering  " },
            },
            Request("session", "context", "  PR #517: reap ordering  "));
    }

    [Fact]
    public void SetWithTargetAndWindow()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionContext, Target = "9f3c",
                Args = new ControlArgs { Mode = "set", Text = "issue 12", Window = "w1" },
            },
            Request("session", "context", "issue 12", "--target", "9f3c", "--window", "w1"));
    }

    [Fact]
    public void ClearSendsNoText()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionContext, Target = "active",
                Args = new ControlArgs { Mode = "clear" },
            },
            Request("session", "context", "--clear"));
    }

    [Fact]
    public void NeitherTextNorClearIsAUsageError()
    {
        Assert.Equal("provide exactly one of a TEXT or --clear", ValidationMessage("session", "context"));
    }

    [Fact]
    public void BothTextAndClearIsAUsageError()
    {
        Assert.Equal("provide exactly one of a TEXT or --clear",
            ValidationMessage("session", "context", "PR #517", "--clear"));
    }
}
