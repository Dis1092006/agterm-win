using System.Buffers.Binary;
using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

// session.overlay.open --image and a clicked picture path in the headless host (upstream #844,
// ControlServer+ImagePage.swift). The host reads the picture's header to size the panel; the page
// itself is drawn by the app.
public sealed class HeadlessImagePageTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;
    private readonly ControlPipeClient _client;

    public HeadlessImagePageTests()
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

    private Session StoreSession(string id) => _host.Store.SessionWithId(Guid.Parse(id))!;

    private string Picture(int width, int height, string name = "shot.png")
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        var path = Path.Combine(_stateDir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private Task<ControlResponse> OpenImage(string target, string path, ControlArgs? extra = null) =>
        Send(Command.SessionOverlayOpen, target, (extra ?? new ControlArgs()) with { Image = path });

    [Fact]
    public async Task AnImageWithoutASizeFitsThePicture()
    {
        var id = await SelectedSession();
        var path = Picture(600, 400);

        var response = await OpenImage(id, path);

        Assert.True(response.Ok, response.Error);
        var session = StoreSession(id);
        Assert.Equal(new HtmlSource.Image(path), session.HtmlOverlay!.Source);
        Assert.False(session.HtmlOverlay.Navigation);
        Assert.False(session.HtmlOverlay.Javascript);
        Assert.Equal(ImagePage.BasePercent, session.OverlaySizePercent);
        Assert.Equal(ImagePage.Points((600, 400), zoom: 1), session.OverlayPoints);
    }

    [Fact]
    public async Task ThePictureIsMeasuredAtTheDisplayScaleAndThePageZoomInsideTheSession()
    {
        var id = await SelectedSession();
        _host.Actions.DisplayScale = 2;
        _host.Store.SetHudMetrics(Guid.Parse(id), new HudSessionMetrics(12, 7, 14, new HudRect(500, 300), null, null, 12));
        await Send(Command.SessionOverlayOpen, id, new ControlArgs { Url = "http://localhost:5173/" });
        await Send(Command.FontInc, id);
        await Send(Command.SessionOverlayClose, id);
        var zoom = _host.Actions.HtmlZoom;
        Assert.NotEqual(1, zoom);

        Assert.True((await OpenImage(id, Picture(1800, 1200))).Ok);

        Assert.Equal(ImagePage.Points((900, 600), zoom, new HudRect(500, 300)), StoreSession(id).OverlayPoints);
    }

    [Fact]
    public async Task AGivenSizeIsKept()
    {
        var id = await SelectedSession();

        Assert.True((await OpenImage(id, Picture(600, 400), new ControlArgs { SizePercent = 60, Width = 300 })).Ok);

        var session = StoreSession(id);
        Assert.Equal(60, session.OverlaySizePercent);
        Assert.Equal(new OverlayPointSize(300), session.OverlayPoints);
    }

    [Fact]
    public async Task TheTreeNamesThePicture()
    {
        var id = await SelectedSession();
        var path = Picture(600, 400);
        await OpenImage(id, path);

        var node = (await Send(Command.Tree, null)).Result!.Tree!
            .Workspaces.SelectMany(w => w.Sessions).Single(s => s.Id == id);

        var page = Assert.Single(node.HtmlOverlays!);
        Assert.Equal(path, page.Image);
        Assert.Null(page.File);
        Assert.Null(page.Url);
    }

    [Fact]
    public async Task AFileThatIsNoPictureIsRefusedWithUpstreamText()
    {
        var id = await SelectedSession();
        var fake = Path.Combine(_stateDir, "fake.png");
        File.WriteAllText(fake, "not a picture");
        var missing = Path.Combine(_stateDir, "missing.png");

        Assert.Equal($"session.overlay.open: not a png, jpg, gif or webp image: {fake}", (await OpenImage(id, fake)).Error);
        Assert.Equal($"session.overlay.open: unreadable image: {missing}", (await OpenImage(id, missing)).Error);
        Assert.Null(StoreSession(id).HtmlOverlay);
    }

    [Fact]
    public async Task AHiddenSlotIsNamedBeforeThePictureIsRead()
    {
        var id = await SelectedSession();
        await Send(Command.SessionOverlayOpen, id, new ControlArgs { Command = "cat" });
        await Send(Command.SessionOverlayHide, id);

        var response = await OpenImage(id, Path.Combine(_stateDir, "missing.png"));

        Assert.Equal(OverlayVisibilityError.Hidden, response.Error);
    }

    [Fact]
    public async Task AClickedPictureOpensSizedToItself()
    {
        var id = await SelectedSession();
        var path = Picture(600, 400);
        var probe = ImageProbe.Load(path, out _)!;

        Assert.True(_host.Actions.CanShowImageLink(Guid.Parse(id)));
        Assert.True(_host.Actions.OpenImageLink(Guid.Parse(id), path, probe));

        var session = StoreSession(id);
        Assert.Equal(new HtmlSource.Image(path), session.HtmlOverlay!.Source);
        Assert.Equal(ImagePage.BasePercent, session.OverlaySizePercent);
        Assert.Equal(ImagePage.Points((600, 400), zoom: 1), session.OverlayPoints);
    }

    [Fact]
    public async Task AClickedPictureIsRefusedOverAHudAZoomedTerminalOrAnOccupiedSlot()
    {
        var id = await SelectedSession();
        var session = Guid.Parse(id);
        var path = Picture(600, 400);
        var probe = ImageProbe.Load(path, out _)!;

        Assert.False(_host.Actions.CanShowImageLink(Guid.NewGuid()));
        Assert.False(_host.Actions.OpenImageLink(Guid.NewGuid(), path, probe));

        Assert.True((await Send(Command.SessionHudOpen, id, new ControlArgs { Message = "building" })).Ok);
        Assert.False(_host.Actions.CanShowImageLink(session));
        Assert.False(_host.Actions.OpenImageLink(session, path, probe));
        Assert.True(StoreSession(id).HudActive);
        await Send(Command.SessionHudClose, id);

        Assert.True((await Send(Command.SurfaceZoom, null, new ControlArgs { Mode = "show" })).Ok);
        Assert.False(_host.Actions.CanShowImageLink(session));
        Assert.False(_host.Actions.OpenImageLink(session, path, probe));
        await Send(Command.SurfaceZoom, null, new ControlArgs { Mode = "hide" });

        await Send(Command.SessionOverlayOpen, id, new ControlArgs { Command = "cat" });
        Assert.False(_host.Actions.OpenImageLink(session, path, probe));
        Assert.Null(StoreSession(id).HtmlOverlay);
    }
}
