using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

public sealed class HeadlessHudTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;
    private readonly ControlPipeClient _client;

    public HeadlessHudTests()
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

    private async Task<string> NewSession(string workspaceName = "work")
    {
        var response = await Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs
            {
                WorkspaceName = workspaceName,
                CreateWorkspace = true,
                Cwd = _stateDir,
            },
        });
        Assert.True(response.Ok, response.Error);
        Assert.NotNull(response.Result?.Id);
        return response.Result!.Id!;
    }

    private static ControlRequest Hud(Command cmd, string? target = null, string message = "working",
                                      string? pane = null, string? paneId = null, double? hideAfter = null,
                                      int? sizePercent = null) => new()
    {
        Cmd = cmd,
        Target = target,
        Args = new ControlArgs
        {
            Message = message,
            Pane = pane,
            PaneId = paneId,
            HideAfter = hideAfter,
            SizePercent = sizePercent,
        },
    };

    private static ControlRequest CloseHud(string target) => new() { Cmd = Command.SessionHudClose, Target = target };

    private static ControlRequest OverlayOpen(string target) =>
        new() { Cmd = Command.SessionOverlayOpen, Target = target, Args = new ControlArgs { Command = "cat" } };

    private static ControlRequest OverlayResize(string target, int? sizePercent = null, bool? full = null) =>
        new() { Cmd = Command.SessionOverlayResize, Target = target, Args = new ControlArgs { SizePercent = sizePercent, Full = full } };

    private static ControlRequest OverlayResult(string target) => new() { Cmd = Command.SessionOverlayResult, Target = target };

    private async Task<ControlTree> Tree() => (await Send(new ControlRequest { Cmd = Command.Tree })).Result!.Tree!;

    private async Task<ControlSessionNode> SessionNode(string id) =>
        (await Tree()).Workspaces.SelectMany(w => w.Sessions).Single(s => s.Id == id);

    private async Task<string> SelectedSession() =>
        (await Tree()).Workspaces.SelectMany(w => w.Sessions).Single(s => s.Active).Id;

    private Session StoreSession(string id) => _host.Store.SessionWithId(Guid.Parse(id))!;

    private async Task Split(string id, string mode) =>
        Assert.True((await Send(new ControlRequest { Cmd = Command.SessionSplit, Target = id, Args = new ControlArgs { Mode = mode } })).Ok);

    private async Task Focus(string id, string pane) =>
        Assert.True((await Send(new ControlRequest { Cmd = Command.SessionFocus, Target = id, Args = new ControlArgs { Pane = pane } })).Ok);

    private sealed class NoTimer : IDisposable
    {
        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task OpenUpdateAndCloseAnswerTheSessionIdAndTheTreeReadsTheHudBack()
    {
        var id = await SelectedSession();
        var open = await Send(new ControlRequest
        {
            Cmd = Command.SessionHudOpen,
            Target = id,
            Args = new ControlArgs
            {
                Message = "gathering options",
                Detail = "scanning 400 files",
                Spinner = "braille",
                Color = "#2a1a3a",
                TextColor = "#e0e0e0",
                SizePercent = 35,
                Position = "top",
            },
        });

        Assert.Equal($$$"""{"ok":true,"result":{"id":"{{{id}}}"}}""", ControlJson.Serialize(open));
        var node = await SessionNode(id);
        Assert.Equal(new ControlHudNode
        {
            Message = "gathering options",
            Detail = "scanning 400 files",
            Spinner = "braille",
            BackgroundColor = "#2a1a3a",
            TextColor = "#e0e0e0",
            SizePercent = 35,
            HeightPercent = 10,
            Position = "top-center",
            HideAfter = 0,
            Markdown = false,
            Sticky = false,
            Frame = true,
        }, node.Hud);
        Assert.False(node.Overlay);
        Assert.Null(node.OverlaySizePercent);

        var update = await Send(Hud(Command.SessionHudUpdate, id, "done"));
        Assert.Equal($$$"""{"ok":true,"result":{"id":"{{{id}}}"}}""", ControlJson.Serialize(update));
        Assert.Equal(new ControlHudNode
        {
            Message = "done",
            Spinner = "none",
            BackgroundColor = "#2a1a3a",
            SizePercent = 80,
            HeightPercent = 10,
            Position = "center",
            HideAfter = 0,
            Markdown = false,
            Sticky = false,
            Frame = true,
        }, (await SessionNode(id)).Hud);

        var close = await Send(CloseHud(id));
        Assert.Equal($$$"""{"ok":true,"result":{"id":"{{{id}}}"}}""", ControlJson.Serialize(close));
        Assert.Null((await SessionNode(id)).Hud);
        Assert.Equal("no hud", (await Send(CloseHud(id))).Error);
        Assert.Equal("no hud", (await Send(Hud(Command.SessionHudUpdate, id))).Error);
    }

    private CommandFailureHudResult OpenFailurePanel(string id, OverlayPane? pane)
    {
        CommandFailureHudResult result = null!;
        var spec = new HudSpec("Command 'build' failed: exit 2", "boom", Position: HudPosition.Center, HideAfter: 10);
        _host.RunLocked(() => result = _host.Actions.OpenCommandFailureHud(Guid.Parse(id), spec, pane));
        return result;
    }

    [Fact]
    public async Task A_failure_panel_opens_over_the_session_with_its_auto_hide()
    {
        var id = await SelectedSession();

        var result = OpenFailurePanel(id, pane: null);

        Assert.True(result.Response.Ok, result.Response.Error);
        Assert.Null(result.Fallback);
        var hud = (await SessionNode(id)).Hud!;
        Assert.Equal("Command 'build' failed: exit 2", hud.Message);
        Assert.Equal(10, hud.HideAfter);
    }

    [Fact]
    public async Task A_failure_panel_for_a_hidden_pane_falls_back_to_the_whole_session()
    {
        var id = await SelectedSession();

        var result = OpenFailurePanel(id, OverlayPane.Right);

        Assert.True(result.Response.Ok, result.Response.Error);
        Assert.Equal("pane not visible", result.Fallback);
        Assert.Null((await SessionNode(id)).Hud!.Pane);
    }

    [Fact]
    public async Task A_failure_panel_does_not_replace_a_program_overlay()
    {
        var id = await SelectedSession();
        Assert.True((await Send(OverlayOpen(id))).Ok);

        var result = OpenFailurePanel(id, pane: null);

        Assert.Equal("overlay already open", result.Response.Error);
        Assert.Null((await SessionNode(id)).Hud);
    }

    [Fact]
    public async Task AProgramOverlayRefusesAHudAndHudCommandsLeaveItRunning()
    {
        var id = await SelectedSession();
        Assert.True((await Send(OverlayOpen(id))).Ok);

        Assert.Equal("overlay already open", (await Send(Hud(Command.SessionHudOpen, id))).Error);
        Assert.Equal("no hud", (await Send(Hud(Command.SessionHudUpdate, id))).Error);
        Assert.Equal("no hud", (await Send(CloseHud(id))).Error);

        Assert.True((await SessionNode(id)).Overlay);
        Assert.Null((await SessionNode(id)).Hud);
    }

    [Fact]
    public async Task ASecondOpenReplacesTheFirst()
    {
        var id = await SelectedSession();
        Assert.True((await Send(Hud(Command.SessionHudOpen, id, "first", sizePercent: 40))).Ok);

        Assert.True((await Send(Hud(Command.SessionHudOpen, id, "second"))).Ok);

        Assert.Equal("second", (await SessionNode(id)).Hud!.Message);
        Assert.Equal(80, (await SessionNode(id)).Hud!.SizePercent);
    }

    [Fact]
    public async Task PaneRejectionsOnOpenAndAMissingSplitOnUpdate()
    {
        var id = await SelectedSession();
        _host.RunLocked(() => StoreSession(id).ScratchSurface = new FakeSurface { PaneToken = "scratch-token" });

        Assert.Equal("pane not visible", (await Send(Hud(Command.SessionHudOpen, id, pane: "right"))).Error);
        Assert.Equal("unknown pane id: missing-token", (await Send(Hud(Command.SessionHudOpen, id, paneId: "missing-token"))).Error);
        Assert.Equal("hud pane must be left or right", (await Send(Hud(Command.SessionHudOpen, id, paneId: "scratch-token"))).Error);
        Assert.Null((await SessionNode(id)).Hud);

        Assert.True((await Send(Hud(Command.SessionHudOpen, id))).Ok);
        Assert.Equal("session has no split", (await Send(Hud(Command.SessionHudUpdate, id, "update", pane: "right"))).Error);
        Assert.Equal("working", (await SessionNode(id)).Hud!.Message);
    }

    [Fact]
    public async Task APaneIdOverridesTheRoleAndTheHudMeasuresAgainstThatPane()
    {
        var id = await SelectedSession();
        await Split(id, "on");
        _host.RunLocked(() =>
        {
            var session = StoreSession(id);
            session.Surface = new FakeSurface { PaneToken = "left-token" };
            session.SplitSurface = new FakeSurface { PaneToken = "right-token" };
            _host.Store.SetHudMetrics(session.Id, new HudSessionMetrics(12, 8, 18,
                new HudRect(1604, 640), new HudRect(600, 640), new HudRect(1000, 640), 12));
        });

        var opened = await Send(Hud(Command.SessionHudOpen, id, new string('x', 60), pane: "left", paneId: "right-token"));

        Assert.True(opened.Ok, opened.Error);
        var hud = (await SessionNode(id)).Hud!;
        Assert.Equal("right", hud.Pane);
        // 64 columns of 8 px plus 16 px of padding: 528 px of the right pane's 1000, where the left
        // pane's 600 would read the cap of 80
        Assert.Equal(53, hud.SizePercent);
        Assert.Equal(11, hud.HeightPercent);
    }

    [Fact]
    public async Task AHiddenPaneRefusesAnOpenButKeepsItsHudThroughAnUpdate()
    {
        var id = await SelectedSession();
        await Split(id, "on");
        Assert.True((await Send(Hud(Command.SessionHudOpen, id, "first", pane: "right"))).Ok);
        await Focus(id, "left");
        await Split(id, "off");

        Assert.Equal("pane not visible", (await Send(Hud(Command.SessionHudOpen, id, "again", pane: "right"))).Error);
        var updated = await Send(Hud(Command.SessionHudUpdate, id, "second", pane: "right"));

        Assert.True(updated.Ok, updated.Error);
        Assert.Equal("second", (await SessionNode(id)).Hud!.Message);
        Assert.Equal("right", (await SessionNode(id)).Hud!.Pane);
        await Split(id, "on");
        Assert.Equal("right", (await SessionNode(id)).Hud!.Pane);
    }

    [Fact]
    public async Task AnUpdateReplacesThePaneScopeAndAnUnknownIdFallsBackToThePane()
    {
        var id = await SelectedSession();
        await Split(id, "on");
        Assert.True((await Send(Hud(Command.SessionHudOpen, id, "first", pane: "right"))).Ok);

        Assert.True((await Send(Hud(Command.SessionHudUpdate, id, "second", pane: "right", paneId: "unknown"))).Ok);
        Assert.Equal("right", (await SessionNode(id)).Hud!.Pane);

        Assert.True((await Send(Hud(Command.SessionHudUpdate, id, "third"))).Ok);
        Assert.Null((await SessionNode(id)).Hud!.Pane);
    }

    [Fact]
    public async Task OverlayOpenTakesTheSlotFromAHud()
    {
        var id = await SelectedSession();
        Assert.True((await Send(Hud(Command.SessionHudOpen, id))).Ok);

        Assert.True((await Send(OverlayOpen(id))).Ok);

        Assert.True((await SessionNode(id)).Overlay);
        Assert.Null((await SessionNode(id)).Hud);
    }

    [Fact]
    public async Task OverlayCloseClosesAHudAndThenFindsNoOverlay()
    {
        var id = await SelectedSession();
        Assert.True((await Send(Hud(Command.SessionHudOpen, id))).Ok);

        var closed = await Send(new ControlRequest { Cmd = Command.SessionOverlayClose, Target = id });

        Assert.Equal($$$"""{"ok":true,"result":{"id":"{{{id}}}"}}""", ControlJson.Serialize(closed));
        Assert.Null((await SessionNode(id)).Hud);
        Assert.Equal("no overlay", (await Send(new ControlRequest { Cmd = Command.SessionOverlayClose, Target = id })).Error);
    }

    [Fact]
    public async Task OverlayResizeSetsTheHudWidthAndRefusesFull()
    {
        var id = await SelectedSession();
        Assert.True((await Send(Hud(Command.SessionHudOpen, id))).Ok);

        var resized = await Send(OverlayResize(id, sizePercent: 35));

        Assert.True(resized.Ok, resized.Error);
        Assert.Equal(35, (await SessionNode(id)).Hud!.SizePercent);
        Assert.Equal("working", (await SessionNode(id)).Hud!.Message);
        Assert.Equal("a hud is always floating: pass --size-percent, not --full", (await Send(OverlayResize(id, full: true))).Error);
        Assert.Equal(35, (await SessionNode(id)).Hud!.SizePercent);

        Assert.True((await Send(OverlayResize(id, sizePercent: 5))).Ok);
        Assert.Equal(10, (await SessionNode(id)).Hud!.SizePercent);
        Assert.False((await SessionNode(id)).Overlay);
    }

    [Fact]
    public async Task OverlayResultRefusesAHudAndAClosedHudLeavesNoResult()
    {
        var id = await SelectedSession();
        Assert.True((await Send(OverlayOpen(id))).Ok);
        _host.RunLocked(() =>
        {
            _host.Store.RecordOverlayExit(Guid.Parse(id), 3);
            _host.Store.CloseOverlay(Guid.Parse(id));
        });
        Assert.Equal(3, (await Send(OverlayResult(id))).Result!.ExitCode);

        Assert.True((await Send(Hud(Command.SessionHudOpen, id))).Ok);
        Assert.Equal("no overlay result: the slot holds a hud", (await Send(OverlayResult(id))).Error);
        Assert.NotNull((await SessionNode(id)).Hud);

        Assert.True((await Send(CloseHud(id))).Ok);
        Assert.Equal("no overlay result", (await Send(OverlayResult(id))).Error);
    }

    // review focus 4: the program took the slot from the HUD, so when it exits the HUD stays gone and
    // overlay.result reports the program, not the HUD
    [Fact]
    public async Task AProgramThatTookTheSlotFromAHudReportsItsOwnExitCode()
    {
        var id = await SelectedSession();
        Assert.True((await Send(Hud(Command.SessionHudOpen, id))).Ok);
        Assert.True((await Send(OverlayOpen(id))).Ok);

        // the headless host runs no process; this is what the live host's exit path does
        _host.RunLocked(() =>
        {
            _host.Store.RecordOverlayExit(Guid.Parse(id), 7);
            _host.Store.CloseOverlay(Guid.Parse(id));
        });

        var node = await SessionNode(id);
        Assert.Null(node.Hud);
        Assert.False(node.Overlay);
        var result = await Send(OverlayResult(id));
        Assert.True(result.Ok, result.Error);
        Assert.Equal(7, result.Result!.ExitCode);
    }

    [Fact]
    public async Task SessionTextReadsThePaneUnderAHud()
    {
        var id = await SelectedSession();
        _host.RunLocked(() => StoreSession(id).Surface = new FakeSurface { ScreenText = "PS> dir" });
        Assert.True((await Send(Hud(Command.SessionHudOpen, id))).Ok);

        var text = await Send(new ControlRequest { Cmd = Command.SessionText, Target = id });

        Assert.True(text.Ok, text.Error);
        Assert.Equal("PS> dir", text.Result!.Text);
    }

    [Fact]
    public async Task TheAutoHideClosesAHudOnABackgroundSessionAndKeepsTheSelection()
    {
        var background = await SelectedSession();
        var selected = await NewSession();
        (TimeSpan Delay, Action Fire)? armed = null;
        _host.RunLocked(() => _host.Store.HudAutoHideScheduler = (delay, fire) =>
        {
            armed = (delay, fire);
            return new NoTimer();
        });

        Assert.True((await Send(Hud(Command.SessionHudOpen, background, hideAfter: 5))).Ok);
        Assert.Equal(TimeSpan.FromSeconds(5), armed!.Value.Delay);
        Assert.Equal(5, (await SessionNode(background)).Hud!.HideAfter);

        _host.RunLocked(armed.Value.Fire);

        Assert.Null((await SessionNode(background)).Hud);
        Assert.Equal(selected, await SelectedSession());
    }
}
