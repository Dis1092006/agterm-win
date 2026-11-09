using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

// session.overlay.open/resize --width/--height (upstream 9bbb3408, ControlServer+SessionActions.swift).
// The port has only the session-wide program overlay.
public sealed class HeadlessOverlayPointsTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;
    private readonly ControlPipeClient _client;

    public HeadlessOverlayPointsTests()
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

    private Task<ControlResponse> Send(ControlRequest request) =>
        Task.Factory.StartNew(() => _client.Send(request), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private async Task<string> SelectedSession() =>
        (await Send(new ControlRequest { Cmd = Command.Tree })).Result!.Tree!
            .Workspaces.SelectMany(w => w.Sessions).Single(s => s.Active).Id;

    private Session StoreSession(string id) => _host.Store.SessionWithId(Guid.Parse(id))!;

    private static ControlRequest Open(string target, int? sizePercent = null, int? width = null, int? height = null) =>
        new()
        {
            Cmd = Command.SessionOverlayOpen, Target = target,
            Args = new ControlArgs { Command = "cat", SizePercent = sizePercent, Width = width, Height = height },
        };

    private static ControlRequest Resize(string target, int? sizePercent = null, int? width = null, int? height = null) =>
        new()
        {
            Cmd = Command.SessionOverlayResize, Target = target,
            Args = new ControlArgs { SizePercent = sizePercent, Width = width, Height = height },
        };

    [Fact]
    public async Task OpenKeepsThePointsBesideThePercent()
    {
        var id = await SelectedSession();

        Assert.True((await Send(Open(id, sizePercent: 60, height: 240))).Ok);

        Assert.Equal(60, StoreSession(id).OverlaySizePercent);
        Assert.Equal(new OverlayPointSize(Height: 240), StoreSession(id).OverlayPoints);
    }

    [Fact]
    public async Task ResizeMergesPointsAloneAndReplacesThemBesideAPercent()
    {
        var id = await SelectedSession();
        Assert.True((await Send(Open(id, sizePercent: 60, height: 240))).Ok);

        var merged = await Send(Resize(id, width: 300));
        Assert.Equal($$$"""{"ok":true,"result":{"id":"{{{id}}}"}}""", ControlJson.Serialize(merged));
        Assert.Equal(new OverlayPointSize(300, 240), StoreSession(id).OverlayPoints);

        Assert.True((await Send(Resize(id, sizePercent: 50, width: 400))).Ok);
        Assert.Equal(50, StoreSession(id).OverlaySizePercent);
        Assert.Equal(new OverlayPointSize(Width: 400), StoreSession(id).OverlayPoints);
    }

    [Fact]
    public async Task ResizeInPointsRefusesAFullOverlayAndNoOverlay()
    {
        var id = await SelectedSession();

        var none = await Send(Resize(id, height: 240));
        Assert.Equal("""{"ok":false,"error":"no overlay"}""", ControlJson.Serialize(none));

        Assert.True((await Send(Open(id))).Ok);
        var full = await Send(Resize(id, height: 240));
        Assert.Equal(OverlayPointError.NotFloating, full.Error);
        Assert.True(StoreSession(id).OverlayPoints.IsEmpty);
    }

    [Fact]
    public async Task ResizeInPointsRefusesAHud()
    {
        var id = await SelectedSession();
        var hud = await Send(new ControlRequest
        {
            Cmd = Command.SessionHudOpen, Target = id, Args = new ControlArgs { Message = "building" },
        });
        Assert.True(hud.Ok);

        var response = await Send(Resize(id, sizePercent: 40, height: 240));

        Assert.Equal(OverlayPointError.Hud, response.Error);
    }
}
