using AgTerm.ControlServer;
using AgTerm.Core.Hooks;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

public sealed class HeadlessHooksTests : IDisposable
{
    private sealed class RecordingLauncher : IHookLauncher
    {
        public List<(HookEntry Entry, ControlEvent Event, Action<int> OnExit)> Launches { get; } = new();

        public int Start(HookEntry entry, ControlEvent controlEvent, Action<string> onDeliveryFailure,
            Action<int> onExit)
        {
            lock (Launches) Launches.Add((entry, controlEvent, onExit));
            return 500 + Launches.Count;
        }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agterm-hooks-host-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingLauncher _launcher = new();
    private HeadlessHost? _host;
    private ControlPipeClient? _client;

    public HeadlessHooksTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        _host?.Dispose();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string HooksPath => Path.Combine(_dir, "hooks.conf");

    private void StartHost(bool withLauncher = true)
    {
        _host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _dir,
            ConfigDir = _dir,
            PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
            HookLauncher = withLauncher ? _launcher : null,
        });
        _host.Start();
        _client = new ControlPipeClient(_host.PipeName);
    }

    private Task<ControlResponse> Send(ControlRequest request) =>
        Task.Factory.StartNew(() => _client!.Send(request), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private async Task WaitForLaunches(int count)
    {
        for (var i = 0; i < 200; i++)
        {
            lock (_launcher.Launches) if (_launcher.Launches.Count >= count) return;
            await Task.Delay(25);
        }
        Assert.Fail($"expected {count} launches, saw {_launcher.Launches.Count}");
    }

    [Fact]
    public async Task Without_a_launcher_the_hooks_commands_say_hooks_are_not_available()
    {
        StartHost(withLauncher: false);
        var reload = await Send(new ControlRequest { Cmd = Command.HooksReload });
        var list = await Send(new ControlRequest { Cmd = Command.HooksList });
        Assert.False(reload.Ok);
        Assert.Equal("hooks are not available", reload.Error);
        Assert.Equal("hooks are not available", list.Error);
    }

    [Fact]
    public async Task List_reports_the_file_path_and_no_hooks_for_a_missing_file()
    {
        StartHost();
        var list = await Send(new ControlRequest { Cmd = Command.HooksList });
        Assert.True(list.Ok, list.Error);
        Assert.Equal(HooksPath, list.Result!.Hooks!.Path);
        Assert.Empty(list.Result.Hooks.Hooks);
    }

    [Fact]
    public async Task Reload_applies_a_changed_file_and_counts_diagnostics()
    {
        StartHost();
        File.WriteAllText(HooksPath, "on session.created echo made\non nonsense x\n");

        var reload = await Send(new ControlRequest { Cmd = Command.HooksReload });
        var list = await Send(new ControlRequest { Cmd = Command.HooksList });

        Assert.True(reload.Ok, reload.Error);
        Assert.Equal(1, reload.Result!.Count);
        var hook = Assert.Single(list.Result!.Hooks!.Hooks);
        Assert.Equal("session.created", hook.Kind);
        Assert.Equal("echo made", hook.Command);
        Assert.Equal(2, Assert.Single(list.Result.Hooks.Diagnostics).Line);
    }

    [Fact]
    public void Hooks_load_at_start_even_when_the_pipe_cannot_bind()
    {
        File.WriteAllText(HooksPath, "on session.created echo made\n");
        var pipeName = "agterm-test-" + Guid.NewGuid().ToString("N");
        using var taken = new System.IO.Pipes.NamedPipeServerStream(pipeName, System.IO.Pipes.PipeDirection.InOut, 1);
        _host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _dir,
            ConfigDir = _dir,
            PipeName = pipeName,
            HookLauncher = _launcher,
        });

        _host.Start();

        var list = _host.Actions.ListHooks();
        Assert.True(list.Ok, list.Error);
        Assert.Equal("echo made", Assert.Single(list.Result!.Hooks!.Hooks).Command);
    }

    [Fact]
    public async Task A_launched_hook_started_at_boot_sees_app_launched_and_later_events()
    {
        File.WriteAllText(HooksPath, "on app.launched echo up\non session.created echo made\n");
        StartHost();
        await WaitForLaunches(1);
        Assert.Equal(ControlEventKind.AppLaunched, _launcher.Launches[0].Event.Kind);

        var created = await Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { WorkspaceName = "work", CreateWorkspace = true, Cwd = _dir },
        });
        Assert.True(created.Ok, created.Error);
        await WaitForLaunches(2);

        Assert.Equal(ControlEventKind.SessionCreated, _launcher.Launches[1].Event.Kind);
        var list = await Send(new ControlRequest { Cmd = Command.HooksList });
        Assert.All(list.Result!.Hooks!.Hooks, h => Assert.NotNull(h.RunningPid));
    }

    [Fact]
    public async Task Hook_exit_releases_the_slot_so_the_queued_event_runs()
    {
        File.WriteAllText(HooksPath, "on session.created echo made\n");
        StartHost();
        for (var i = 0; i < 2; i++)
        {
            var created = await Send(new ControlRequest
            {
                Cmd = Command.SessionNew,
                Args = new ControlArgs { WorkspaceName = "w" + i, CreateWorkspace = true, Cwd = _dir },
            });
            Assert.True(created.Ok, created.Error);
        }
        await WaitForLaunches(1);
        await Task.Delay(150);
        Assert.Single(_launcher.Launches);

        _launcher.Launches[0].OnExit(0);

        await WaitForLaunches(2);
    }
}
