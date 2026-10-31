using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;
using Xunit;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// upstream agtermApp.swift: the main pane's onFontSizeChange writes store.setFontSize, and a
/// promoted split survivor adopts that wiring. Split, scratch and overlay panes are not wired. The
/// port reports only from the pane's own font actions, so the chord and font.inc/dec/reset save the
/// size, and the next launch opens the pane at it.
/// </summary>
public sealed class LiveFontSizePersistenceTests : IDisposable
{
    private readonly string _stateDir = Directory.CreateTempSubdirectory("agterm-live-font-persist-").FullName;
    private readonly List<(SurfaceOpenRequest Request, FakeSurface Surface)> _opened = [];

    public void Dispose()
    {
        try { Directory.Delete(_stateDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Font_command_on_the_main_pane_saves_the_size_and_the_next_launch_opens_at_it()
    {
        Guid sessionId;
        using (var host = CreateHost())
        {
            host.Start();
            var client = new ControlPipeClient(host.PipeName);
            sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);

            AssertOk(client.Send(new ControlRequest { Cmd = Command.FontInc, Target = sessionId.ToString("D") }));
            AssertOk(client.Send(new ControlRequest { Cmd = Command.FontInc, Target = sessionId.ToString("D") }));
            Assert.Equal(16, await SavedSizeAsync(host, sessionId));

            AssertOk(client.Send(new ControlRequest { Cmd = Command.FontReset, Target = sessionId.ToString("D") }));
            Assert.Equal(14, await SavedSizeAsync(host, sessionId));

            AssertOk(client.Send(new ControlRequest { Cmd = Command.FontDec, Target = sessionId.ToString("D") }));
            Assert.Equal(13, await SavedSizeAsync(host, sessionId));
        }

        lock (_opened) _opened.Clear();
        using (var restarted = CreateHost())
        {
            Assert.Equal(13, await SavedSizeAsync(restarted, sessionId));
            SurfaceOpenRequest main;
            lock (_opened) main = Assert.Single(_opened, entry => entry.Request.Kind == LiveSurfaceKind.Main).Request;
            Assert.Equal(13, main.Options.BaseFontSize);
        }
    }

    [Fact]
    public async Task Chord_saves_the_main_pane_size_but_not_split_scratch_or_overlay()
    {
        using var host = CreateHost();
        host.Start();
        var client = new ControlPipeClient(host.PipeName);
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);
        AssertOk(client.Send(new ControlRequest { Cmd = Command.SessionSplit, Args = new ControlArgs { Mode = "on" } }));
        AssertOk(client.Send(new ControlRequest { Cmd = Command.SessionScratch, Args = new ControlArgs { Mode = "on" } }));
        AssertOk(client.Send(new ControlRequest
        {
            Cmd = Command.SessionOverlayOpen,
            Args = new ControlArgs { Command = "overlay-tool" },
        }));

        await host.PerformFontActionAsync("increase_font_size:1", Token(LiveSurfaceKind.Split, sessionId));
        await host.PerformFontActionAsync("increase_font_size:1", Token(LiveSurfaceKind.Scratch, sessionId));
        await host.PerformFontActionAsync("increase_font_size:1", Token(LiveSurfaceKind.Overlay, sessionId));
        AssertOk(client.Send(new ControlRequest
        {
            Cmd = Command.FontInc,
            Target = sessionId.ToString("D"),
            Args = new ControlArgs { Pane = "right" },
        }));
        Assert.Equal(16, Opened(LiveSurfaceKind.Split, sessionId).FontSize);
        Assert.Equal(15, Opened(LiveSurfaceKind.Scratch, sessionId).FontSize);
        Assert.Equal(15, Opened(LiveSurfaceKind.Overlay, sessionId).FontSize);
        Assert.Null(await SavedSizeAsync(host, sessionId));

        await host.PerformFontActionAsync("increase_font_size:1", Token(LiveSurfaceKind.Main, sessionId));
        Assert.Equal(15, await SavedSizeAsync(host, sessionId));
    }

    // the split survivor takes the main slot when the main shell exits, and from then on saves its
    // own size like a main pane (upstream handlePaneExit re-wires onFontSizeChange).
    [Fact]
    public async Task Promoted_split_survivor_saves_its_size()
    {
        using var host = CreateHost();
        host.Start();
        var client = new ControlPipeClient(host.PipeName);
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);
        AssertOk(client.Send(new ControlRequest { Cmd = Command.SessionSplit, Args = new ControlArgs { Mode = "on" } }));
        var survivor = Opened(LiveSurfaceKind.Split, sessionId);

        await host.PerformFontActionAsync("increase_font_size:1", survivor.PaneToken);
        Assert.Null(await SavedSizeAsync(host, sessionId));

        await host.StoreCoordinator.MutateAsync(store => store.ClosePrimaryPane(sessionId));
        await host.PerformFontActionAsync("increase_font_size:1", survivor.PaneToken);

        Assert.Equal(16, await SavedSizeAsync(host, sessionId));
    }

    // a dashboard tile size lives on the dashboard, not on the pane, so it is not saved. Untouched
    // mode resizes the pane itself, and that is saved like any main-pane chord.
    [Fact]
    public async Task Dashboard_tile_chord_is_not_saved_and_untouched_mode_chord_is()
    {
        using var host = CreateHost();
        host.Start();
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);
        await host.StoreCoordinator.MutateAsync(_ => Assert.True(host.Host.Actions.SetDashboard(
            [sessionId.ToString("D")], window: null, close: false, new DashboardFontMode.Fixed(11), mru: false).Ok));

        await host.PerformFontActionAsync("increase_font_size:1");
        Assert.Null(await SavedSizeAsync(host, sessionId));

        await host.CloseDashboardAsync();
        await host.StoreCoordinator.MutateAsync(_ => Assert.True(host.Host.Actions.SetDashboard(
            [sessionId.ToString("D")], window: null, close: false, new DashboardFontMode.Untouched(), mru: false).Ok));
        await host.PerformFontActionAsync("increase_font_size:1");

        Assert.Equal(15, await SavedSizeAsync(host, sessionId));
    }

    private LiveControlHost CreateHost() => new(new LiveControlHostOptions
    {
        StateDir = _stateDir,
        ConfigDir = _stateDir,
        PipeName = "agterm-live-font-persist-" + Guid.NewGuid().ToString("N"),
        DefaultShell = "test-shell.exe",
        HomeDirectory = _stateDir,
        BaseFontSize = 14,
        OpenSurface = (request, _) =>
        {
            var surface = new FakeSurface
            {
                PaneToken = Token(request.Kind, request.SessionId),
                BaseFontSize = request.Options.BaseFontSize,
                FontSize = request.Options.BaseFontSize,
            };
            lock (_opened) _opened.Add((request, surface));
            return Task.FromResult<IHeadlessSurface>(surface);
        },
    });

    private static string Token(LiveSurfaceKind kind, Guid? sessionId) => $"{kind}-{sessionId:N}";

    private FakeSurface Opened(LiveSurfaceKind kind, Guid sessionId)
    {
        lock (_opened)
            return Assert.Single(_opened, entry => entry.Request.Kind == kind && entry.Request.SessionId == sessionId).Surface;
    }

    private static Task<double?> SavedSizeAsync(LiveControlHost host, Guid sessionId) =>
        host.StoreCoordinator.ReadAsync(store => store.SessionWithId(sessionId)!.FontSize);

    private static void AssertOk(ControlResponse response) => Assert.True(response.Ok, response.Error);
}
