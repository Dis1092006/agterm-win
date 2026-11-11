using AgTerm.Cli;
using AgTerm.Cli.Tests.TestSupport;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests;

// session overlay open/resize --width/--height (upstream 9bbb3408 OverlayCommandsTests)
[Collection(ConsoleCollection.Name)]
public class OverlayPointCommandsTests
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

    [Fact]
    public void OpenAndResizeSendPointSizes()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionOverlayOpen, Target = "active",
                Args = new ControlArgs { Command = "cat", SizePercent = 60, Width = 700, Height = 240 },
            },
            Request("session", "overlay", "open", "cat", "--size-percent", "60", "--width", "700", "--height", "240"));
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionOverlayResize, Target = "s", Args = new ControlArgs { Height = 300 },
            },
            Request("session", "overlay", "resize", "--height", "300", "--target", "s"));
    }

    [Theory]
    [InlineData("--width/--height need --size-percent", "session", "overlay", "open", "cat", "--height", "240")]
    [InlineData("--width/--height must be positive",
        "session", "overlay", "open", "cat", "--size-percent", "60", "--width", "0")]
    [InlineData("--full cannot be combined with --width/--height",
        "session", "overlay", "resize", "--full", "--height", "240")]
    [InlineData("--width/--height must be positive", "session", "overlay", "resize", "--width", "-1")]
    [InlineData("provide --size-percent PERCENT, --width/--height POINTS or --full", "session", "overlay", "resize")]
    public void PointSizesAreRefusedBeforeAnyConnection(string message, params string[] argv)
    {
        Assert.Equal(message, ValidationMessage(argv));
    }

    [Fact]
    public void HelpDescribesThePointOptions()
    {
        var open = Help("session", "overlay", "open");
        var resize = Help("session", "overlay", "resize");

        const string width = "Panel content width in points, in place of --size-percent's width (at most 80% of the session).";
        Assert.Contains(width, open);
        Assert.Contains(width, resize);
        const string height = "Panel content height in points, in place of --size-percent's height (at most 80% of the session); a page's title strip is added on top.";
        Assert.Contains(height, open);
        Assert.Contains(height, resize);
        Assert.Contains("Resize an open overlay: floating at a percent or in points, or back to full-pane.", resize);
        Assert.Contains("--width/--height alone change those axes of an overlay that is already floating.", resize);
    }
}
