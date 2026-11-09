using AgTerm.Cli;
using AgTerm.Cli.Tests.TestSupport;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests;

// session overlay hide, show and toggle, and the tree tag of a hidden overlay
// (parity/adopted.json, upstream 7f450d17 OverlayCommandsTests and SocketClientTests)
[Collection(ConsoleCollection.Name)]
public class OverlayVisibilityCommandsTests
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

    // the rendered `--help`, with its line wrapping collapsed
    private static string Help(params string[] path)
    {
        var writer = new StringWriter();
        var original = Console.Out;
        Console.SetOut(writer);
        try
        {
            Agtermctl.Run([.. path, "--help"]);
        }
        finally
        {
            Console.SetOut(original);
        }
        return string.Join(' ', writer.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    [Theory]
    [InlineData("hide", WireCommand.SessionOverlayHide)]
    [InlineData("show", WireCommand.SessionOverlayShow)]
    [InlineData("toggle", WireCommand.SessionOverlayToggle)]
    public void VisibilityCommandsSendTheirCommandPaneAndTarget(string verb, WireCommand cmd)
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = cmd, Target = "s" },
            Request("session", "overlay", verb, "--target", "s"));
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = cmd, Target = "active",
                Args = new ControlArgs { Pane = "right", Window = "w" },
            },
            Request("session", "overlay", verb, "--pane", "right", "--window", "w"));
        Assert.Equal("--pane must be left or right", ValidationMessage("session", "overlay", verb, "--pane", "scratch"));
    }

    [Fact]
    public void VisibilityHelpStatesTheSessionScopeAndTheToggleRule()
    {
        var helps = new[]
        {
            Help("session", "overlay", "hide"), Help("session", "overlay", "show"), Help("session", "overlay", "toggle"),
        };
        foreach (var help in helps)
        {
            Assert.Contains("every overlay of the session", help, StringComparison.Ordinal);
            Assert.Contains("--pane", help, StringComparison.Ordinal);
        }
        Assert.Contains("if any is shown, otherwise show them all", helps[2], StringComparison.Ordinal);
        Assert.Contains("stays in `tree` (hiddenOverlays)", helps[0], StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayHelpListsTheVisibilityVerbsBetweenCloseAndResize()
    {
        var help = Help("session", "overlay");
        help = help[help.IndexOf("Commands:", StringComparison.Ordinal)..];
        var order = new[] { "open", "close", "hide", "show", "toggle", "resize", "result" }
            .Select(verb => help.IndexOf($" {verb} ", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.OrderBy(i => i), order);
    }

    [Theory]
    [InlineData(null, " (overlay)")]
    [InlineData("session", " (overlay hidden)")]
    [InlineData("left", " (overlay)")]
    public void FormatTreeTagsAHiddenSessionOverlay(string? hidden, string tag)
    {
        var session = new ControlSessionNode
        {
            Id = "s1", Name = "shell", Cwd = "/tmp", Active = true, Split = false, Overlay = true,
            HiddenOverlays = hidden is null ? null : [hidden],
        };
        var tree = new ControlTree
        {
            Workspaces = [new ControlWorkspaceNode { Id = "w1", Name = "work", Active = true, Sessions = [session] }],
        };
        var lines = ClientOutput.FormatResponse(new ControlResponse
        {
            Ok = true, Result = new ControlResult { Tree = tree },
        }, json: false).Split('\n');
        Assert.Equal($"  * shell{tag}  [s1]  /tmp", lines[1]);
    }
}
