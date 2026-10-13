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

    private ControlResponse Send(ControlRequest request) => _client.Send(request);

    private string NewSession(string workspaceName = "work")
    {
        var response = Send(new ControlRequest
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

    private ControlTree Tree() => Send(new ControlRequest { Cmd = Command.Tree }).Result!.Tree!;

    private ControlSessionNode SessionNode(string id) =>
        Tree().Workspaces.SelectMany(w => w.Sessions).Single(s => s.Id == id);

    private string SelectedSession() =>
        Tree().Workspaces.SelectMany(w => w.Sessions).Single(s => s.Active).Id;

    private Session StoreSession(string id) => _host.Store.SessionWithId(Guid.Parse(id))!;

    private void Split(string id, string mode) =>
        Assert.True(Send(new ControlRequest { Cmd = Command.SessionSplit, Target = id, Args = new ControlArgs { Mode = mode } }).Ok);

    private void Focus(string id, string pane) =>
        Assert.True(Send(new ControlRequest { Cmd = Command.SessionFocus, Target = id, Args = new ControlArgs { Pane = pane } }).Ok);

    private sealed class NoTimer : IDisposable
    {
        public void Dispose()
        {
        }
    }

    [Fact]
    public void OpenUpdateAndCloseAnswerTheSessionIdAndTheTreeReadsTheHudBack()
    {
        var id = SelectedSession();
        var open = Send(new ControlRequest
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
        var node = SessionNode(id);
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

        var update = Send(Hud(Command.SessionHudUpdate, id, "done"));
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
        }, SessionNode(id).Hud);

        var close = Send(CloseHud(id));
        Assert.Equal($$$"""{"ok":true,"result":{"id":"{{{id}}}"}}""", ControlJson.Serialize(close));
        Assert.Null(SessionNode(id).Hud);
        Assert.Equal("no hud", Send(CloseHud(id)).Error);
        Assert.Equal("no hud", Send(Hud(Command.SessionHudUpdate, id)).Error);
    }

    [Fact]
    public void AProgramOverlayRefusesAHudAndHudCommandsLeaveItRunning()
    {
        var id = SelectedSession();
        Assert.True(Send(OverlayOpen(id)).Ok);

        Assert.Equal("overlay already open", Send(Hud(Command.SessionHudOpen, id)).Error);
        Assert.Equal("no hud", Send(Hud(Command.SessionHudUpdate, id)).Error);
        Assert.Equal("no hud", Send(CloseHud(id)).Error);

        Assert.True(SessionNode(id).Overlay);
        Assert.Null(SessionNode(id).Hud);
    }

    [Fact]
    public void ASecondOpenReplacesTheFirst()
    {
        var id = SelectedSession();
        Assert.True(Send(Hud(Command.SessionHudOpen, id, "first", sizePercent: 40)).Ok);

        Assert.True(Send(Hud(Command.SessionHudOpen, id, "second")).Ok);

        Assert.Equal("second", SessionNode(id).Hud!.Message);
        Assert.Equal(80, SessionNode(id).Hud!.SizePercent);
    }

    [Fact]
    public void PaneRejectionsOnOpenAndAMissingSplitOnUpdate()
    {
        var id = SelectedSession();
        _host.RunLocked(() => StoreSession(id).ScratchSurface = new FakeSurface { PaneToken = "scratch-token" });

        Assert.Equal("pane not visible", Send(Hud(Command.SessionHudOpen, id, pane: "right")).Error);
        Assert.Equal("unknown pane id: missing-token", Send(Hud(Command.SessionHudOpen, id, paneId: "missing-token")).Error);
        Assert.Equal("hud pane must be left or right", Send(Hud(Command.SessionHudOpen, id, paneId: "scratch-token")).Error);
        Assert.Null(SessionNode(id).Hud);

        Assert.True(Send(Hud(Command.SessionHudOpen, id)).Ok);
        Assert.Equal("session has no split", Send(Hud(Command.SessionHudUpdate, id, "update", pane: "right")).Error);
        Assert.Equal("working", SessionNode(id).Hud!.Message);
    }

    [Fact]
    public void APaneIdOverridesTheRoleAndTheHudMeasuresAgainstThatPane()
    {
        var id = SelectedSession();
        Split(id, "on");
        _host.RunLocked(() =>
        {
            var session = StoreSession(id);
            session.Surface = new FakeSurface { PaneToken = "left-token" };
            session.SplitSurface = new FakeSurface { PaneToken = "right-token" };
            _host.Store.SetHudMetrics(session.Id, new HudSessionMetrics(12, 8, 18,
                new HudRect(1604, 640), new HudRect(600, 640), new HudRect(1000, 640)));
        });

        var opened = Send(Hud(Command.SessionHudOpen, id, new string('x', 60), pane: "left", paneId: "right-token"));

        Assert.True(opened.Ok, opened.Error);
        var hud = SessionNode(id).Hud!;
        Assert.Equal("right", hud.Pane);
        // 64 columns of 8 px plus 16 px of padding: 528 px of the right pane's 1000, where the left
        // pane's 600 would read the cap of 80
        Assert.Equal(53, hud.SizePercent);
        Assert.Equal(11, hud.HeightPercent);
    }

    [Fact]
    public void AHiddenPaneRefusesAnOpenButKeepsItsHudThroughAnUpdate()
    {
        var id = SelectedSession();
        Split(id, "on");
        Assert.True(Send(Hud(Command.SessionHudOpen, id, "first", pane: "right")).Ok);
        Focus(id, "left");
        Split(id, "off");

        Assert.Equal("pane not visible", Send(Hud(Command.SessionHudOpen, id, "again", pane: "right")).Error);
        var updated = Send(Hud(Command.SessionHudUpdate, id, "second", pane: "right"));

        Assert.True(updated.Ok, updated.Error);
        Assert.Equal("second", SessionNode(id).Hud!.Message);
        Assert.Equal("right", SessionNode(id).Hud!.Pane);
        Split(id, "on");
        Assert.Equal("right", SessionNode(id).Hud!.Pane);
    }

    [Fact]
    public void AnUpdateReplacesThePaneScopeAndAnUnknownIdFallsBackToThePane()
    {
        var id = SelectedSession();
        Split(id, "on");
        Assert.True(Send(Hud(Command.SessionHudOpen, id, "first", pane: "right")).Ok);

        Assert.True(Send(Hud(Command.SessionHudUpdate, id, "second", pane: "right", paneId: "unknown")).Ok);
        Assert.Equal("right", SessionNode(id).Hud!.Pane);

        Assert.True(Send(Hud(Command.SessionHudUpdate, id, "third")).Ok);
        Assert.Null(SessionNode(id).Hud!.Pane);
    }

    [Fact]
    public void OverlayOpenTakesTheSlotFromAHud()
    {
        var id = SelectedSession();
        Assert.True(Send(Hud(Command.SessionHudOpen, id)).Ok);

        Assert.True(Send(OverlayOpen(id)).Ok);

        Assert.True(SessionNode(id).Overlay);
        Assert.Null(SessionNode(id).Hud);
    }

    [Fact]
    public void OverlayCloseClosesAHudAndThenFindsNoOverlay()
    {
        var id = SelectedSession();
        Assert.True(Send(Hud(Command.SessionHudOpen, id)).Ok);

        var closed = Send(new ControlRequest { Cmd = Command.SessionOverlayClose, Target = id });

        Assert.Equal($$$"""{"ok":true,"result":{"id":"{{{id}}}"}}""", ControlJson.Serialize(closed));
        Assert.Null(SessionNode(id).Hud);
        Assert.Equal("no overlay", Send(new ControlRequest { Cmd = Command.SessionOverlayClose, Target = id }).Error);
    }

    [Fact]
    public void OverlayResizeSetsTheHudWidthAndRefusesFull()
    {
        var id = SelectedSession();
        Assert.True(Send(Hud(Command.SessionHudOpen, id)).Ok);

        var resized = Send(OverlayResize(id, sizePercent: 35));

        Assert.True(resized.Ok, resized.Error);
        Assert.Equal(35, SessionNode(id).Hud!.SizePercent);
        Assert.Equal("working", SessionNode(id).Hud!.Message);
        Assert.Equal("a hud is always floating: pass --size-percent, not --full", Send(OverlayResize(id, full: true)).Error);
        Assert.Equal(35, SessionNode(id).Hud!.SizePercent);

        Assert.True(Send(OverlayResize(id, sizePercent: 5)).Ok);
        Assert.Equal(10, SessionNode(id).Hud!.SizePercent);
        Assert.False(SessionNode(id).Overlay);
    }

    [Fact]
    public void OverlayResultRefusesAHudAndAClosedHudLeavesNoResult()
    {
        var id = SelectedSession();
        Assert.True(Send(OverlayOpen(id)).Ok);
        _host.RunLocked(() =>
        {
            _host.Store.RecordOverlayExit(Guid.Parse(id), 3);
            _host.Store.CloseOverlay(Guid.Parse(id));
        });
        Assert.Equal(3, Send(OverlayResult(id)).Result!.ExitCode);

        Assert.True(Send(Hud(Command.SessionHudOpen, id)).Ok);
        Assert.Equal("no overlay result: the slot holds a hud", Send(OverlayResult(id)).Error);
        Assert.NotNull(SessionNode(id).Hud);

        Assert.True(Send(CloseHud(id)).Ok);
        Assert.Equal("no overlay result", Send(OverlayResult(id)).Error);
    }

    // review focus 4: the program took the slot from the HUD, so when it exits the HUD stays gone and
    // overlay.result reports the program, not the HUD
    [Fact]
    public void AProgramThatTookTheSlotFromAHudReportsItsOwnExitCode()
    {
        var id = SelectedSession();
        Assert.True(Send(Hud(Command.SessionHudOpen, id)).Ok);
        Assert.True(Send(OverlayOpen(id)).Ok);

        // the headless host runs no process; this is what the live host's exit path does
        _host.RunLocked(() =>
        {
            _host.Store.RecordOverlayExit(Guid.Parse(id), 7);
            _host.Store.CloseOverlay(Guid.Parse(id));
        });

        var node = SessionNode(id);
        Assert.Null(node.Hud);
        Assert.False(node.Overlay);
        var result = Send(OverlayResult(id));
        Assert.True(result.Ok, result.Error);
        Assert.Equal(7, result.Result!.ExitCode);
    }

    [Fact]
    public void SessionTextReadsThePaneUnderAHud()
    {
        var id = SelectedSession();
        _host.RunLocked(() => StoreSession(id).Surface = new FakeSurface { ScreenText = "PS> dir" });
        Assert.True(Send(Hud(Command.SessionHudOpen, id)).Ok);

        var text = Send(new ControlRequest { Cmd = Command.SessionText, Target = id });

        Assert.True(text.Ok, text.Error);
        Assert.Equal("PS> dir", text.Result!.Text);
    }

    [Fact]
    public void TheAutoHideClosesAHudOnABackgroundSessionAndKeepsTheSelection()
    {
        var background = SelectedSession();
        var selected = NewSession();
        (TimeSpan Delay, Action Fire)? armed = null;
        _host.RunLocked(() => _host.Store.HudAutoHideScheduler = (delay, fire) =>
        {
            armed = (delay, fire);
            return new NoTimer();
        });

        Assert.True(Send(Hud(Command.SessionHudOpen, background, hideAfter: 5)).Ok);
        Assert.Equal(TimeSpan.FromSeconds(5), armed!.Value.Delay);
        Assert.Equal(5, SessionNode(background).Hud!.HideAfter);

        _host.RunLocked(armed.Value.Fire);

        Assert.Null(SessionNode(background).Hud);
        Assert.Equal(selected, SelectedSession());
    }
}
