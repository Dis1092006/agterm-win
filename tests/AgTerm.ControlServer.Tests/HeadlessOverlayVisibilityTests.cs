using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

// session.overlay.hide|show|toggle and the "overlay hidden" refusal (upstream 7f450d17,
// ControlServerOverlayVisibilityTests.swift). The port has only the session-wide slot.
public sealed class HeadlessOverlayVisibilityTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;
    private readonly ControlPipeClient _client;

    public HeadlessOverlayVisibilityTests()
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

    private static ControlRequest OverlayOpen(string target) =>
        new() { Cmd = Command.SessionOverlayOpen, Target = target, Args = new ControlArgs { Command = "cat" } };

    private static ControlRequest Visibility(Command cmd, string target, string? pane = null) =>
        new() { Cmd = cmd, Target = target, Args = pane is null ? null : new ControlArgs { Pane = pane } };

    private async Task<string> SessionWithOverlay()
    {
        var id = await SelectedSession();
        Assert.True((await Send(OverlayOpen(id))).Ok);
        return id;
    }

    [Fact]
    public async Task HideShowAndToggleReportTheStateTheyLeave()
    {
        var id = await SessionWithOverlay();

        var hidden = await Send(Visibility(Command.SessionOverlayHide, id));
        Assert.Equal($$$"""{"ok":true,"result":{"id":"{{{id}}}","text":"hidden"}}""", ControlJson.Serialize(hidden));
        Assert.True(StoreSession(id).OverlayHidden);
        Assert.True(StoreSession(id).OverlayActive);

        var toggled = await Send(Visibility(Command.SessionOverlayToggle, id));
        Assert.Equal("shown", toggled.Result?.Text);
        Assert.False(StoreSession(id).OverlayHidden);

        var shown = await Send(Visibility(Command.SessionOverlayShow, id));
        Assert.Equal("shown", shown.Result?.Text);
    }

    [Fact]
    public async Task VisibilityRefusesAnEmptySessionAndAnUnknownTarget()
    {
        var id = await SelectedSession();

        var empty = await Send(Visibility(Command.SessionOverlayToggle, id));
        var unknown = await Send(Visibility(Command.SessionOverlayHide, "nope"));

        Assert.Equal("""{"ok":false,"error":"no overlay"}""", ControlJson.Serialize(empty));
        Assert.False(unknown.Ok);
    }

    // the port has no pane overlays, so a pane scope holds nothing to hide
    [Fact]
    public async Task APaneScopeFindsNoOverlay()
    {
        var id = await SessionWithOverlay();

        var response = await Send(Visibility(Command.SessionOverlayHide, id, pane: "left"));

        Assert.Equal("no overlay", response.Error);
        Assert.False(StoreSession(id).OverlayHidden);
    }

    [Fact]
    public async Task AHudIsNotHideable()
    {
        var id = await SelectedSession();
        Assert.True((await Send(new ControlRequest
        {
            Cmd = Command.SessionHudOpen, Target = id, Args = new ControlArgs { Message = "building" },
        })).Ok);

        Assert.Equal("no overlay", (await Send(Visibility(Command.SessionOverlayHide, id))).Error);
    }

    [Fact]
    public async Task OpeningIntoAHiddenSlotIsRefusedByName()
    {
        var id = await SessionWithOverlay();
        Assert.True((await Send(Visibility(Command.SessionOverlayHide, id))).Ok);

        var response = await Send(OverlayOpen(id));

        Assert.Equal("""{"ok":false,"error":"overlay hidden"}""", ControlJson.Serialize(response));
        Assert.Equal("cat", StoreSession(id).OverlayCommand);
    }

    [Fact]
    public async Task AHudCannotOpenOverAHiddenProgram()
    {
        var id = await SessionWithOverlay();
        Assert.True((await Send(Visibility(Command.SessionOverlayHide, id))).Ok);

        var response = await Send(new ControlRequest
        {
            Cmd = Command.SessionHudOpen, Target = id, Args = new ControlArgs { Message = "building" },
        });

        Assert.Equal("overlay hidden", response.Error);
        Assert.False(StoreSession(id).HudActive);
    }

    [Fact]
    public async Task CloseAndResultStillReachAHiddenOverlay()
    {
        var id = await SessionWithOverlay();
        Assert.True((await Send(Visibility(Command.SessionOverlayHide, id))).Ok);

        Assert.Equal(OverlayResultError.StillRunning,
            (await Send(new ControlRequest { Cmd = Command.SessionOverlayResult, Target = id })).Error);
        Assert.True((await Send(new ControlRequest { Cmd = Command.SessionOverlayClose, Target = id })).Ok);

        Assert.False(StoreSession(id).OverlayHidden);
        Assert.True((await Send(OverlayOpen(id))).Ok);
    }
}
