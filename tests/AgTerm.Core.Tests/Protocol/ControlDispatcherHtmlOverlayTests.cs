using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Protocol;

// session.overlay.open --html and session.overlay.reload|navigate at the dispatcher (upstream
// ControlDispatcher+Overlay.swift, 11fe64eb).
public class ControlDispatcherHtmlOverlayTests
{
    private const string File = @"C:\work\r.html";

    private static ControlResponse Err(string error) => new() { Ok = false, Error = error };

    private static async Task<(ControlResponse? Response, MockControlActions Actions)> Post(Command cmd, ControlArgs? args)
    {
        var actions = new MockControlActions();
        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest { Cmd = cmd, Target = "s", Args = args });
        return (response, actions);
    }

    private static async Task ExpectError(Command cmd, ControlArgs args, string error)
    {
        var (response, actions) = await Post(cmd, args);
        Assert.Equal(Err(error), response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task OpenRoutesAFilePageWithItsGrant()
    {
        var (_, actions) = await Post(Command.SessionOverlayOpen, new ControlArgs
        {
            Html = File, Cwd = @"C:\work", Navigation = true, Javascript = true,
            SizePercent = 60, Color = "#102030", Follow = true, Window = "win",
        });

        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.OverlayOpen("s", "win",
                new ControlSessionOverlayOpenOptions("", Cwd: null, Wait: false, SizePercent: 60,
                    BackgroundColor: "#102030", Follow: true,
                    Page: new HtmlSource.File(File, @"C:\work"), Navigation: true, Javascript: true)),
        }, actions.Calls);
    }

    [Fact]
    public async Task OpenRoutesAFilePageWithoutAGrant()
    {
        var (_, actions) = await Post(Command.SessionOverlayOpen, new ControlArgs { Html = File });

        var call = Assert.IsType<MockControlActions.Call.OverlayOpen>(Assert.Single(actions.Calls));
        Assert.Equal(new HtmlSource.File(File, null), call.Options.Page);
        Assert.False(call.Options.Navigation);
        Assert.False(call.Options.Javascript);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    public async Task OpenRoutesAUrlPageAsGiven(bool? javascript)
    {
        var (_, actions) = await Post(Command.SessionOverlayOpen, new ControlArgs
        {
            Url = "HTTP://LocalHost:5173/app", SizePercent = 60, Navigation = true, Javascript = javascript,
        });

        var call = Assert.IsType<MockControlActions.Call.OverlayOpen>(Assert.Single(actions.Calls));
        var page = Assert.IsType<HtmlSource.Url>(call.Options.Page);
        Assert.Equal("HTTP://LocalHost:5173/app", page.Address.OriginalString);
        Assert.Equal("", call.Options.Command);
        Assert.Null(call.Options.Cwd);
        Assert.Equal(60, call.Options.SizePercent);
        Assert.True(call.Options.Navigation);
        Assert.Equal(javascript == true, call.Options.Javascript);
    }

    [Theory]
    [InlineData(File, null, null, false, OverlayHtmlError.HtmlAndUrl)]
    [InlineData(null, "cat", null, false, OverlayHtmlError.CommandAndUrl)]
    [InlineData(null, null, null, true, OverlayHtmlError.WaitWithUrl)]
    [InlineData(null, null, @"C:\work", false, OverlayHtmlError.CwdWithUrl)]
    public Task AUrlPageRefusesWhatAPageCannotTake(string? html, string? command, string? cwd, bool wait, string error) =>
        ExpectError(Command.SessionOverlayOpen,
                    new ControlArgs { Url = "http://localhost:5173/", Html = html, Command = command, Cwd = cwd, Wait = wait,
                                      Color = "bad" },
                    error);

    [Theory]
    [InlineData("ftp://example.com/")]
    [InlineData("localhost:5173")]
    [InlineData(@"C:\work\r.html")]
    public Task AUrlMustBeAbsoluteHttp(string url) =>
        ExpectError(Command.SessionOverlayOpen, new ControlArgs { Url = url, Color = "bad" }, OverlayHtmlError.InvalidUrl);

    [Fact]
    public void TheUrlErrorsAreUpstreamText()
    {
        Assert.Equal("session.overlay.open takes --html or --url, not both", OverlayHtmlError.HtmlAndUrl);
        Assert.Equal("session.overlay.open takes a command or --url, not both", OverlayHtmlError.CommandAndUrl);
        Assert.Equal("session.overlay.open: --wait cannot be combined with --url", OverlayHtmlError.WaitWithUrl);
        Assert.Equal("session.overlay.open: --cwd cannot be combined with --url", OverlayHtmlError.CwdWithUrl);
        Assert.Equal("session.overlay.open: --url must be an absolute http or https URL", OverlayHtmlError.InvalidUrl);
    }

    [Fact]
    public async Task AProgramCarriesNoPage()
    {
        var (_, actions) = await Post(Command.SessionOverlayOpen, new ControlArgs { Command = "htop", Cwd = "/tmp" });

        var call = Assert.IsType<MockControlActions.Call.OverlayOpen>(Assert.Single(actions.Calls));
        Assert.Null(call.Options.Page);
        Assert.Equal("/tmp", call.Options.Cwd);
    }

    [Fact]
    public Task APageTakesNoCommand() =>
        ExpectError(Command.SessionOverlayOpen, new ControlArgs { Html = File, Command = "htop", Color = "bad" },
                    OverlayHtmlError.CommandAndHtml);

    [Fact]
    public Task APageTakesNoWait() =>
        ExpectError(Command.SessionOverlayOpen, new ControlArgs { Html = File, Wait = true }, OverlayHtmlError.WaitWithHtml);

    [Fact]
    public Task NavigationNeedsAPage() =>
        ExpectError(Command.SessionOverlayOpen, new ControlArgs { Navigation = true }, OverlayHtmlError.NavigationWithoutPage);

    [Fact]
    public Task JavascriptNeedsAPage() =>
        ExpectError(Command.SessionOverlayOpen, new ControlArgs { Javascript = true, Command = "htop" },
                    OverlayHtmlError.JavascriptWithoutPage);

    [Fact]
    public Task FalseFlagsNeedNoPage() =>
        ExpectError(Command.SessionOverlayOpen, new ControlArgs { Navigation = false, Javascript = false },
                    "session.overlay.open requires a command");

    [Theory]
    [InlineData(@"r.html", null, "html file must be an absolute path")]
    [InlineData(File, "work", "cwd must be an absolute path")]
    [InlineData(File, @"C:\", "cwd must not be / or the home directory")]
    [InlineData(File, @"C:\other", "html file is outside cwd")]
    public Task TheGrantIsCheckedBeforeTheColor(string html, string? cwd, string error) =>
        ExpectError(Command.SessionOverlayOpen, new ControlArgs { Html = html, Cwd = cwd, Color = "bad" },
                    "session.overlay.open: " + error);

    [Fact]
    public Task TheHomeDirectoryIsNoGrant()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return ExpectError(Command.SessionOverlayOpen, new ControlArgs { Html = Path.Combine(home, "r.html"), Cwd = home },
                           "session.overlay.open: cwd must not be / or the home directory");
    }

    [Fact]
    public Task APageKeepsTheSizeChecks() =>
        ExpectError(Command.SessionOverlayOpen, new ControlArgs { Html = File, Width = 300 }, OverlayPointError.NeedsPercent);

    [Fact]
    public async Task ReloadRoutesTheTarget()
    {
        var (_, actions) = await Post(Command.SessionOverlayReload, new ControlArgs { Window = "win" });
        var (_, current) = await Post(Command.SessionOverlayReload, new ControlArgs { Current = true, Pane = "left" });

        Assert.Equal([new MockControlActions.Call.OverlayReload("s", "win", null, false)], actions.Calls);
        Assert.Equal([new MockControlActions.Call.OverlayReload("s", null, OverlayPane.Left, true)], current.Calls);
    }

    [Fact]
    public Task ReloadRefusesABadPane() =>
        ExpectError(Command.SessionOverlayReload, new ControlArgs { Pane = "middle" }, PaneOverlayError.InvalidPane);

    [Theory]
    [InlineData("back", HtmlNavigation.Back)]
    [InlineData("forward", HtmlNavigation.Forward)]
    [InlineData("browser", HtmlNavigation.Browser)]
    [InlineData("finder", HtmlNavigation.Finder)]
    public async Task NavigateRoutesTheStep(string to, HtmlNavigation navigation)
    {
        var (_, actions) = await Post(Command.SessionOverlayNavigate, new ControlArgs { To = to, Window = "win" });

        Assert.Equal([new MockControlActions.Call.OverlayNavigate("s", "win", null, navigation)], actions.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("up")]
    [InlineData("Back")]
    public Task NavigateRefusesAnUnknownStepBeforeThePane(string? to) =>
        ExpectError(Command.SessionOverlayNavigate, new ControlArgs { To = to, Pane = "middle" }, OverlayHtmlError.Navigation);

    [Fact]
    public Task NavigateRefusesABadPane() =>
        ExpectError(Command.SessionOverlayNavigate, new ControlArgs { To = "back", Pane = "middle" }, PaneOverlayError.InvalidPane);

    [Fact]
    public void TheCommandsHaveUpstreamWireNames()
    {
        Assert.Equal("session.overlay.reload", Command.SessionOverlayReload.ToWire());
        Assert.Equal("session.overlay.navigate", Command.SessionOverlayNavigate.ToWire());
    }

    [Fact]
    public void TheArgsUseUpstreamKeys()
    {
        var args = ControlJson.Deserialize<ControlArgs>(
            "{\"html\":\"C:\\\\r.html\",\"url\":\"http://a/\",\"current\":true,\"navigation\":true,\"javascript\":true}")!;

        Assert.Equal(@"C:\r.html", args.Html);
        Assert.Equal("http://a/", args.Url);
        Assert.True(args.Current);
        Assert.True(args.Navigation);
        Assert.True(args.Javascript);
    }

    [Fact]
    public void ThePageIdIsWrittenAsPageID() =>
        Assert.Equal("{\"id\":\"S\",\"pageID\":\"P\"}", ControlJson.Serialize(new ControlResult { Id = "S", PageId = "P" }));
}
