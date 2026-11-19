using AgTerm.Cli;
using AgTerm.Cli.Tests.TestSupport;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests;

// session overlay open --html, reload and navigate (parity/adopted.json, upstream 11fe64eb
// OverlayCommandsTests and OverlayPageCommands.swift)
[Collection(ConsoleCollection.Name)]
public class OverlayHtmlCommandsTests
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
    public void OpenSendsAFilePageWithAbsolutePaths()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionOverlayOpen, Target = "s",
                Args = new ControlArgs
                {
                    Html = Path.GetFullPath("r.html"), Cwd = Path.GetFullPath("."),
                    Navigation = true, Javascript = true, SizePercent = 60,
                },
            },
            Request("session", "overlay", "open", "--html", "r.html", "--cwd", ".", "--navigation", "--js",
                    "--size-percent", "60", "--target", "s"));
    }

    [Fact]
    public void OpenSendsAUrlAsTyped()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionOverlayOpen, Target = "s",
                Args = new ControlArgs { Url = "HTTP://LocalHost:5173/app", Navigation = true, Javascript = true },
            },
            Request("session", "overlay", "open", "--url", "HTTP://LocalHost:5173/app", "--navigation", "--js",
                    "--target", "s"));
    }

    [Fact]
    public void AProgramKeepsItsCwdAsTyped()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionOverlayOpen, Target = "active",
                Args = new ControlArgs { Command = "htop", Cwd = "." },
            },
            Request("session", "overlay", "open", "htop", "--cwd", "."));
    }

    [Theory]
    [InlineData("provide exactly one of COMMAND, --html, --url or --image", "session", "overlay", "open")]
    [InlineData("provide exactly one of COMMAND, --html, --url or --image", "session", "overlay", "open", "htop", "--html", "r.html")]
    [InlineData("provide exactly one of COMMAND, --html, --url or --image", "session", "overlay", "open", "htop", "--url", "http://a/")]
    [InlineData("provide exactly one of COMMAND, --html, --url or --image", "session", "overlay", "open", "--html", "r.html", "--url", "http://a/")]
    [InlineData("a page cannot be combined with --wait or --block", "session", "overlay", "open", "--html", "r.html", "--wait")]
    [InlineData("a page cannot be combined with --wait or --block", "session", "overlay", "open", "--html", "r.html", "--block")]
    [InlineData("a page cannot be combined with --wait or --block", "session", "overlay", "open", "--url", "http://a/", "--wait")]
    [InlineData("--cwd cannot be combined with --url", "session", "overlay", "open", "--url", "http://a/", "--cwd", ".")]
    [InlineData("--navigation requires --html or --url", "session", "overlay", "open", "htop", "--navigation")]
    [InlineData("--js requires --html or --url", "session", "overlay", "open", "htop", "--js")]
    public void OpenRefusesAMismatchedPage(string message, params string[] argv) =>
        Assert.Equal(message, ValidationMessage(argv));

    private const string ImageFlags = "--image takes no --block, --wait, --navigation, --js or --cwd";

    // upstream openRejectsAMissingOrConflictingContent (#844), without the flags the port does not have
    [Theory]
    [InlineData("provide exactly one of COMMAND, --html, --url or --image", "revdiff", "--image", "a.png")]
    [InlineData("provide exactly one of COMMAND, --html, --url or --image", "--html", "r.html", "--image", "a.png")]
    [InlineData("provide exactly one of COMMAND, --html, --url or --image", "--url", "http://a/", "--image", "a.png")]
    [InlineData(ImageFlags, "--image", "a.png", "--block")]
    [InlineData(ImageFlags, "--image", "a.png", "--wait")]
    [InlineData(ImageFlags, "--image", "a.png", "--navigation")]
    [InlineData(ImageFlags, "--image", "a.png", "--js")]
    [InlineData(ImageFlags, "--image", "a.png", "--cwd", ".")]
    [InlineData("--width/--height need --size-percent", "--image", "a.png", "--width", "400")]
    public void AnImageOpenRefusesWhatAPictureCannotTake(string message, params string[] argv) =>
        Assert.Equal(message, ValidationMessage(["session", "overlay", "open", .. argv]));

    [Fact]
    public void AnImageOpenSendsTheResolvedPathAndNoPageFlags()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionOverlayOpen, Target = "active",
                Args = new ControlArgs { Image = Path.GetFullPath(Path.Combine("shots", "a.png")), Follow = true },
            },
            Request("session", "overlay", "open", "--image", @"shots\..\shots\a.png", "--follow"));
    }

    [Fact]
    public void ReloadSendsCurrentAndPane()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SessionOverlayReload, Target = "s" },
            Request("session", "overlay", "reload", "--target", "s"));
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionOverlayReload, Target = "active",
                Args = new ControlArgs { Pane = "left", Current = true, Window = "w" },
            },
            Request("session", "overlay", "reload", "--current", "--pane", "left", "--window", "w"));
        Assert.Equal("--pane must be left or right", ValidationMessage("session", "overlay", "reload", "--pane", "scratch"));
    }

    [Theory]
    [InlineData("back")]
    [InlineData("forward")]
    [InlineData("browser")]
    [InlineData("finder")]
    public void NavigateSendsTheStep(string step)
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SessionOverlayNavigate, Target = "active",
                Args = new ControlArgs { To = step, Pane = "right" },
            },
            Request("session", "overlay", "navigate", step, "--pane", "right"));
    }

    [Fact]
    public void NavigateRefusesAnUnknownStep()
    {
        Assert.Equal("step must be back, forward, browser, or finder",
                     ValidationMessage("session", "overlay", "navigate", "up"));
        Assert.Equal("--pane must be left or right",
                     ValidationMessage("session", "overlay", "navigate", "back", "--pane", "scratch"));
    }

    [Fact]
    public void HelpNamesThePageFlags()
    {
        var open = Help("session", "overlay", "open");
        Assert.Contains("Show this local HTML file instead of running COMMAND.", open, StringComparison.Ordinal);
        Assert.Contains("Show this http or https URL instead of running COMMAND; links to its own origin load in place. "
                        + "localhost means the PC running agterm.", open, StringComparison.Ordinal);
        Assert.Contains("showing a page with --html, --url or --image.", open, StringComparison.Ordinal);
        Assert.Contains("omit with --html, --url or --image.", open, StringComparison.Ordinal);
        Assert.Contains("Show this png, jpg, gif or webp picture instead of running COMMAND. Without --size-percent the panel "
                        + "fits the picture, up to 80% of the session; a larger picture is scaled down.", open, StringComparison.Ordinal);
        Assert.Contains("With --html or --url, let the page run its own JavaScript (off by default)", open, StringComparison.Ordinal);
        Assert.Contains("Without --cwd, the page has no file access.", open, StringComparison.Ordinal);
        Assert.Contains("--current the page shown now", Help("session", "overlay", "reload"), StringComparison.Ordinal);
        Assert.Contains("reveal its current file in Explorer", Help("session", "overlay", "navigate"), StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayHelpListsReloadAndNavigateAfterResize()
    {
        var help = Help("session", "overlay");
        help = help[help.IndexOf("Commands:", StringComparison.Ordinal)..];
        var order = new[] { "open", "close", "hide", "show", "toggle", "resize", "reload", "navigate", "result" }
            .Select(verb => help.IndexOf($" {verb} ", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.OrderBy(i => i), order);
    }
}
