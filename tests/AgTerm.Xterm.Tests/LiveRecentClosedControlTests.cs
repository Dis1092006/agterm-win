using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

// recent.list / recent.open / recent.clear on the live application host (upstream 6b564749). The
// routing rules follow upstream's WindowLibraryRecentClosedTests: an entry goes to the window that
// already holds its session, else the one holding its workspace, else the requested or active window.
public sealed class LiveRecentClosedControlTests : IDisposable
{
    private readonly string _stateDir = Directory.CreateTempSubdirectory("agterm-recent-live-").FullName;
    private LiveApplicationHost? _application;

    public void Dispose()
    {
        _application?.Dispose();
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private LiveApplicationHost Application(bool twoWindows = false) => _application = new LiveApplicationHost(
        new LiveApplicationHostOptions
        {
            StateDir = _stateDir,
            PipeName = "agterm-recent-live-" + Guid.NewGuid().ToString("N"),
            HomeDirectory = _stateDir,
            WindowController = twoWindows ? new ImmediateWindowController() : null,
        });

    private static ControlResponse Send(LiveApplicationHost application, Command cmd, string? target = null,
                                        string? window = null) =>
        new ControlPipeClient(application.PipeName).Send(new ControlRequest
        {
            Cmd = cmd,
            Target = target,
            Args = window is null ? null : new ControlArgs { Window = window },
        });

    private static Guid CloseTheOnlySession(LiveWindowHost window)
    {
        Guid sessionId = default;
        window.RunLocked(() =>
        {
            sessionId = window.Store.SelectedSessionId!.Value;
            window.Store.CloseSession(sessionId);
        });
        return sessionId;
    }

    private static string Upper(Guid id) => id.ToString("D").ToUpperInvariant();

    [Fact]
    public void An_empty_list_reports_nothing_and_open_is_refused()
    {
        var application = Application();
        application.Start();

        var list = Send(application, Command.RecentList);
        Assert.True(list.Ok, list.Error);
        Assert.Empty(list.Result!.Recent!);

        var open = Send(application, Command.RecentOpen);
        Assert.False(open.Ok);
        Assert.Equal("no recently closed items", open.Error);
    }

    [Theory]
    [InlineData(Command.RecentList)]
    [InlineData(Command.RecentClear)]
    public void List_and_clear_refuse_a_target_or_window(Command cmd)
    {
        var application = Application();
        application.Start();

        Assert.Equal($"{cmd.ToWire()} takes no target or --window", Send(application, cmd, target: "x").Error);
        Assert.Equal($"{cmd.ToWire()} takes no target or --window", Send(application, cmd, window: "x").Error);
    }

    [Fact]
    public void A_closed_session_is_listed_newest_first_and_open_reopens_and_consumes_it()
    {
        var application = Application();
        var windowId = application.LaunchWindowIds.Single();
        var window = application.GetOpenWindow(windowId);
        application.Start();
        var sessionId = CloseTheOnlySession(window);

        var item = Assert.Single(Send(application, Command.RecentList).Result!.Recent!);
        Assert.Equal("session", item.Kind);
        Assert.Equal(new[] { Upper(sessionId) }, item.SessionIds);

        var open = Send(application, Command.RecentOpen);
        Assert.True(open.Ok, open.Error);
        Assert.Equal(item.Id, open.Result!.RecentOpen!.Item.Id);
        Assert.Equal(Upper(windowId), open.Result.RecentOpen.Window);
        Assert.True(open.Result.RecentOpen.Complete);
        window.RunLocked(() => Assert.NotNull(window.Store.SessionWithId(sessionId)));
        Assert.Empty(application.RecentClosedItems);
    }

    [Fact]
    public void Open_takes_an_id_prefix_and_names_an_unknown_one()
    {
        var application = Application();
        var window = application.GetOpenWindow(application.LaunchWindowIds.Single());
        application.Start();
        CloseTheOnlySession(window);
        var id = Assert.Single(Send(application, Command.RecentList).Result!.Recent!).Id;

        var unknown = Send(application, Command.RecentOpen, target: "zzzz");
        Assert.Equal("no such recent item: zzzz", unknown.Error);

        var byPrefix = Send(application, Command.RecentOpen, target: id[..12]);
        Assert.True(byPrefix.Ok, byPrefix.Error);
        Assert.Equal(id, byPrefix.Result!.RecentOpen!.Item.Id);
    }

    [Fact]
    public void Clear_empties_the_list()
    {
        var application = Application();
        var window = application.GetOpenWindow(application.LaunchWindowIds.Single());
        application.Start();
        CloseTheOnlySession(window);

        Assert.True(Send(application, Command.RecentClear).Ok);
        Assert.Empty(Send(application, Command.RecentList).Result!.Recent!);
    }

    [Fact]
    public void A_clear_that_cannot_be_written_reports_it()
    {
        var application = Application();
        var window = application.GetOpenWindow(application.LaunchWindowIds.Single());
        application.Start();
        CloseTheOnlySession(window);

        // a shared-read handle lets the list still load but blocks the replace, so the write fails and
        // the store swallows it
        using var hold = new FileStream(Path.Combine(_stateDir, "recent-closed.json"),
            FileMode.Open, FileAccess.Read, FileShare.Read);
        var clear = Send(application, Command.RecentClear);

        Assert.False(clear.Ok);
        Assert.Equal("could not clear recent items", clear.Error);
    }

    [Fact]
    public void An_unknown_window_restores_nothing()
    {
        var application = Application();
        var window = application.GetOpenWindow(application.LaunchWindowIds.Single());
        application.Start();
        CloseTheOnlySession(window);

        var open = Send(application, Command.RecentOpen, window: "nope");

        Assert.Equal("no such window: nope", open.Error);
        Assert.Single(application.RecentClosedItems);
    }

    [Fact]
    public async Task An_entry_goes_to_the_window_holding_its_workspace_not_the_requested_one()
    {
        var application = Application(twoWindows: true);
        var firstId = application.LaunchWindowIds.Single();
        var created = await application.CreateWindowAsync();
        Assert.True(created.Ok, created.Error);
        var secondId = Guid.Parse(created.Result!.Id!);
        var first = application.GetOpenWindow(firstId);
        var second = application.GetOpenWindow(secondId);
        application.Start();
        Guid closed = default;
        first.RunLocked(() =>
        {
            var workspace = first.Store.Workspaces[0];
            closed = first.Store.AddSession(workspace.Id, _stateDir)!.Id;
            first.Store.CloseSession(closed);
        });
        var secondBefore = second.CaptureSessionIds();

        var open = Send(application, Command.RecentOpen, window: secondId.ToString("D"));

        Assert.True(open.Ok, open.Error);
        Assert.Equal(Upper(firstId), open.Result!.RecentOpen!.Window);
        first.RunLocked(() => Assert.NotNull(first.Store.SessionWithId(closed)));
        Assert.Equal(secondBefore, second.CaptureSessionIds());
    }

    [Fact]
    public async Task An_entry_whose_session_is_held_by_another_window_goes_there()
    {
        var application = Application(twoWindows: true);
        var firstId = application.LaunchWindowIds.Single();
        var created = await application.CreateWindowAsync();
        var secondId = Guid.Parse(created.Result!.Id!);
        var first = application.GetOpenWindow(firstId);
        var second = application.GetOpenWindow(secondId);
        application.Start();
        // closed with a long grace, the session stays held by the first window's pending close
        Guid held = default;
        first.RunLocked(() =>
        {
            held = first.Store.SelectedSessionId!.Value;
            first.Store.AddSession(first.Store.Workspaces[0].Id, _stateDir);
            first.Store.SoftCloseSession(held, grace: 60);
        });
        first.RunLocked(() => Assert.Contains(held, first.Store.HeldSessionIds()));

        var open = Send(application, Command.RecentOpen, window: secondId.ToString("D"));

        Assert.True(open.Ok, open.Error);
        Assert.Equal(Upper(firstId), open.Result!.RecentOpen!.Window);
        first.RunLocked(() => Assert.NotNull(first.Store.SessionWithId(held)));
        Assert.DoesNotContain(held, second.CaptureSessionIds());
    }

    private sealed class ImmediateWindowController : ILiveWindowController
    {
        public Task OpenWindowAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ActivateWindowAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CloseWindowAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<LiveWindowPresentation?> CaptureWindowAsync(
            Guid windowId, CancellationToken cancellationToken) => Task.FromResult<LiveWindowPresentation?>(null);
        public Task<int> GetDisplayCountAsync(CancellationToken cancellationToken) => Task.FromResult(1);
        public Task ResizeWindowAsync(
            Guid windowId, int width, int height, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<LiveWindowMoveResult> MoveWindowAsync(
            Guid windowId, int x, int y, int? display, CancellationToken cancellationToken) =>
            Task.FromResult(new LiveWindowMoveResult(true, 1));
        public Task ToggleZoomAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ToggleFullscreenAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
