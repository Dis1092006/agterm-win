using AgTerm.ControlServer;
using AgTerm.Core.Hooks;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

// hooks.reload / hooks.list on the live application host: one controller shared by every window, loaded
// before app.launched, fed by the ring.
public sealed class LiveHooksControlTests : IDisposable
{
    private sealed class RecordingLauncher : IHookLauncher
    {
        private readonly object _gate = new();
        private readonly List<ControlEvent> _events = [];
        private int _pid = 100;

        public IReadOnlyList<ControlEvent> Events
        {
            get { lock (_gate) return _events.ToList(); }
        }

        public int Start(HookEntry entry, ControlEvent controlEvent, Action<string> onDeliveryFailure, Action<int> onExit)
        {
            lock (_gate)
            {
                _events.Add(controlEvent);
                return ++_pid;
            }
        }
    }

    private readonly string _stateDir = Directory.CreateTempSubdirectory("agterm-hooks-live-").FullName;
    private LiveApplicationHost? _application;

    public void Dispose()
    {
        _application?.Dispose();
        try { Directory.Delete(_stateDir, recursive: true); }
        catch (IOException) { }
    }

    private LiveApplicationHost Application(IHookLauncher? launcher, bool twoWindows = false) =>
        _application = new LiveApplicationHost(new LiveApplicationHostOptions
        {
            StateDir = _stateDir,
            PipeName = "agterm-hooks-live-" + Guid.NewGuid().ToString("N"),
            HomeDirectory = _stateDir,
            HookLauncher = launcher,
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

    private void WriteHooks(string text) => File.WriteAllText(Path.Combine(_stateDir, "hooks.conf"), text);

    private static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            Thread.Sleep(20);
        }
    }

    [Theory]
    [InlineData(Command.HooksReload)]
    [InlineData(Command.HooksList)]
    public void Without_a_launcher_the_commands_answer_unavailable(Command cmd)
    {
        var application = Application(launcher: null);
        application.Start();

        var response = Send(application, cmd);

        Assert.False(response.Ok);
        Assert.Equal("hooks are not available", response.Error);
    }

    [Theory]
    [InlineData(Command.HooksReload)]
    [InlineData(Command.HooksList)]
    public void The_commands_refuse_a_target_or_window(Command cmd)
    {
        var application = Application(new RecordingLauncher());
        application.Start();

        Assert.Equal($"{cmd.ToWire()} takes no target or --window", Send(application, cmd, target: "x").Error);
        Assert.Equal($"{cmd.ToWire()} takes no target or --window", Send(application, cmd, window: "x").Error);
    }

    [Fact]
    public void The_file_is_loaded_before_app_launched_so_that_hook_fires()
    {
        WriteHooks("on app.launched echo hi\n");
        var launcher = new RecordingLauncher();
        var application = Application(launcher);

        application.Start();

        WaitFor(() => launcher.Events.Any(e => e.Kind == ControlEventKind.AppLaunched));
        var list = Send(application, Command.HooksList);
        Assert.True(list.Ok, list.Error);
        var hook = Assert.Single(list.Result!.Hooks!.Hooks);
        Assert.Equal("app.launched", hook.Kind);
        Assert.Equal("echo hi", hook.Command);
    }

    [Fact]
    public void An_event_from_a_window_reaches_the_shared_controller_and_a_reload_picks_up_edits()
    {
        var launcher = new RecordingLauncher();
        var application = Application(launcher);
        var window = application.GetOpenWindow(application.LaunchWindowIds.Single());
        application.Start();
        Assert.Equal(0, Send(application, Command.HooksReload).Result!.Count);

        WriteHooks("on session.created echo created\nbogus line\n");
        Assert.Equal(1, Send(application, Command.HooksReload).Result!.Count);
        window.RunLocked(() => window.Store.AddSession(window.Store.Workspaces[0].Id, _stateDir));

        WaitFor(() => launcher.Events.Any(e => e.Kind == ControlEventKind.SessionCreated));
    }

    [Fact]
    public void A_second_window_shares_the_same_controller()
    {
        var launcher = new RecordingLauncher();
        var application = Application(launcher, twoWindows: true);
        application.Start();
        WriteHooks("on session.created echo created\n");
        Send(application, Command.HooksReload);

        var created = new ControlPipeClient(application.PipeName).Send(new ControlRequest { Cmd = Command.WindowNew });
        Assert.True(created.Ok, created.Error);

        var list = Send(application, Command.HooksList);
        Assert.Single(list.Result!.Hooks!.Hooks);
    }

    private sealed class ImmediateWindowController : ILiveWindowController
    {
        public Task OpenWindowAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ActivateWindowAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CloseWindowAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<LiveWindowPresentation?> CaptureWindowAsync(
            Guid windowId, CancellationToken cancellationToken) => Task.FromResult<LiveWindowPresentation?>(null);
        public Task<int> GetDisplayCountAsync(CancellationToken cancellationToken) => Task.FromResult(1);
        public Task<LiveWindowSize?> ResizeWindowAsync(
            Guid windowId, int width, int height, CancellationToken cancellationToken) => Task.FromResult<LiveWindowSize?>(null);
        public Task<LiveWindowMoveResult> MoveWindowAsync(
            Guid windowId, int x, int y, int? display, CancellationToken cancellationToken) =>
            Task.FromResult(new LiveWindowMoveResult(true, 1));
        public Task ToggleZoomAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ToggleFullscreenAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<LiveWindowMinimizeResult> MinimizeWindowAsync(Guid windowId, ControlToggleMode mode, CancellationToken cancellationToken) =>
            Task.FromResult(new LiveWindowMinimizeResult(LiveWindowMinimizeStatus.Applied, mode == ControlToggleMode.On));
    }
}
