using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

// session.overlay.open --html and session.overlay.reload|navigate in the headless host (upstream
// 11fe64eb, ControlServerHtmlOverlayTests.swift). The headless host keeps the page in the store but
// has no web view, so navigate answers "html overlay not realized".
public sealed class HeadlessHtmlOverlayTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;
    private readonly ControlPipeClient _client;

    public HeadlessHtmlOverlayTests()
    {
        _stateDir = Path.Combine(Path.GetTempPath(), "agterm-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_stateDir);
        _host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
        });
        _host.Start();
        _client = new ControlPipeClient(_host.PipeName);
    }

    public void Dispose()
    {
        _host.Dispose();
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private Task<ControlResponse> Send(Command cmd, string? target, ControlArgs? args = null) =>
        Task.Factory.StartNew(() => _client.Send(new ControlRequest { Cmd = cmd, Target = target, Args = args }),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private async Task<string> SelectedSession() =>
        (await Send(Command.Tree, null)).Result!.Tree!
            .Workspaces.SelectMany(w => w.Sessions).Single(s => s.Active).Id;

    private async Task<string> NewSession() => (await Send(Command.SessionNew, null)).Result!.Id!;

    private Session StoreSession(string id) => _host.Store.SessionWithId(Guid.Parse(id))!;

    private string PageFile => Path.Combine(_stateDir, "r.html");

    private Task<ControlResponse> OpenPage(string target, ControlArgs? extra = null) =>
        Send(Command.SessionOverlayOpen, target, (extra ?? new ControlArgs()) with { Html = PageFile, Cwd = _stateDir });

    [Fact]
    public async Task OpenAnswersTheSessionAndThePageId()
    {
        var id = await SelectedSession();

        var response = await OpenPage(id, new ControlArgs { Navigation = true, Javascript = true, SizePercent = 70 });

        Assert.True(response.Ok, response.Error);
        var session = StoreSession(id);
        var page = session.HtmlOverlay!;
        Assert.Equal(id, response.Result!.Id);
        Assert.Equal(page.Id.ToString("D").ToUpperInvariant(), response.Result.PageId);
        Assert.Equal(new HtmlSource.File(PageFile, _stateDir), page.Source);
        Assert.True(page.Navigation);
        Assert.True(page.Javascript);
        Assert.Equal(70, session.OverlaySizePercent);
        Assert.Null(session.OverlaySurface);
    }

    [Fact]
    public async Task ThePageShowsInTheTree()
    {
        var id = await SelectedSession();
        await OpenPage(id);

        var node = (await Send(Command.Tree, null)).Result!.Tree!
            .Workspaces.SelectMany(w => w.Sessions).Single(s => s.Id == id);

        var page = Assert.Single(node.HtmlOverlays!);
        Assert.Equal(PageFile, page.File);
        Assert.Equal("loading", page.State);
    }

    [Fact]
    public async Task AnOccupiedSlotRefusesAPage()
    {
        var id = await SelectedSession();
        await Send(Command.SessionOverlayOpen, id, new ControlArgs { Command = "cat" });

        var response = await OpenPage(id);

        Assert.Equal("overlay already open", response.Error);
    }

    [Fact]
    public async Task APageRefusesAProgram()
    {
        var id = await SelectedSession();
        await OpenPage(id);

        var response = await Send(Command.SessionOverlayOpen, id, new ControlArgs { Command = "cat" });

        Assert.Equal("overlay already open", response.Error);
    }

    [Fact]
    public async Task AHiddenSlotRefusesAPage()
    {
        var id = await SelectedSession();
        await Send(Command.SessionOverlayOpen, id, new ControlArgs { Command = "cat" });
        await Send(Command.SessionOverlayHide, id);

        var response = await OpenPage(id);

        Assert.Equal(OverlayVisibilityError.Hidden, response.Error);
    }

    [Fact]
    public async Task FollowSelectsTheSession()
    {
        var other = await NewSession();
        var first = (await Send(Command.Tree, null)).Result!.Tree!.Workspaces
            .SelectMany(w => w.Sessions).First(s => s.Id != other).Id;
        await Send(Command.SessionSelect, first);

        await OpenPage(other, new ControlArgs { Follow = true });

        Assert.Equal(other, await SelectedSession());
    }

    [Fact]
    public async Task ResultOfAPageIsNoResult()
    {
        var id = await SelectedSession();
        await OpenPage(id);

        var response = await Send(Command.SessionOverlayResult, id);

        Assert.Equal(OverlayHtmlError.NoResult, response.Error);
    }

    [Fact]
    public async Task CloseReleasesThePage()
    {
        var id = await SelectedSession();
        await OpenPage(id);

        var response = await Send(Command.SessionOverlayClose, id);

        Assert.True(response.Ok, response.Error);
        Assert.Null(StoreSession(id).HtmlOverlay);
        Assert.False(StoreSession(id).OverlayActive);
    }

    [Fact]
    public async Task ReloadAsksForTheOriginalOrTheCurrentPage()
    {
        var id = await SelectedSession();
        await OpenPage(id);

        var original = await Send(Command.SessionOverlayReload, id);
        var current = await Send(Command.SessionOverlayReload, id, new ControlArgs { Current = true });

        Assert.True(original.Ok, original.Error);
        Assert.Equal(id, original.Result!.Id);
        Assert.True(current.Ok, current.Error);
        var page = StoreSession(id).HtmlOverlay!;
        Assert.Equal(2, page.ReloadRevision);
        Assert.Equal(HtmlReloadTarget.Current, page.ReloadTarget);
    }

    [Fact]
    public async Task ReloadNeedsAPage()
    {
        var id = await SelectedSession();

        Assert.Equal(OverlayHtmlError.NoOverlay, (await Send(Command.SessionOverlayReload, id)).Error);
        await Send(Command.SessionOverlayOpen, id, new ControlArgs { Command = "cat" });
        Assert.Equal(OverlayHtmlError.NotHtml, (await Send(Command.SessionOverlayReload, id)).Error);
    }

    [Fact]
    public async Task APaneHoldsNoPage()
    {
        var id = await SelectedSession();
        await OpenPage(id);

        var response = await Send(Command.SessionOverlayReload, id, new ControlArgs { Pane = "left" });

        Assert.Equal(OverlayHtmlError.NoOverlay, response.Error);
    }

    [Fact]
    public async Task NavigateNeedsAWebView()
    {
        var id = await SelectedSession();

        Assert.Equal(OverlayHtmlError.NoOverlay,
                     (await Send(Command.SessionOverlayNavigate, id, new ControlArgs { To = "back" })).Error);
        await OpenPage(id);
        Assert.Equal(OverlayHtmlError.NotRealized,
                     (await Send(Command.SessionOverlayNavigate, id, new ControlArgs { To = "back" })).Error);
    }

    [Fact]
    public async Task AHostWithoutPagesRefusesHtml()
    {
        _host.Actions.HtmlOverlaysSupported = false;
        var id = await SelectedSession();

        var response = await OpenPage(id);

        Assert.Equal(OverlayHtmlError.Unsupported, response.Error);
        Assert.False(StoreSession(id).OverlayActive);
        Assert.True((await Send(Command.SessionOverlayOpen, id, new ControlArgs { Command = "cat" })).Ok);
    }
}
