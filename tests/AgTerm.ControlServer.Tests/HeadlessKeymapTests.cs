using AgTerm.ControlServer;
using AgTerm.Core.Keymap;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

public sealed class HeadlessKeymapTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;
    private readonly ControlPipeClient _client;

    public HeadlessKeymapTests()
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

    private string KeymapPath => Path.Combine(_stateDir, "keymap.conf");

    [Fact]
    public void Actions_expose_the_keymap_path_they_load()
    {
        Assert.Equal(KeymapPath, _host.Actions.KeymapPath);
    }

    private Task<ControlResponse> Send(ControlRequest request) =>
        Task.Factory.StartNew(() => _client.Send(request), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private Task<ControlResponse> List() => Send(new ControlRequest { Cmd = Command.KeymapList });

    private Task<ControlResponse> Run(string name, string? target = null) => Send(new ControlRequest
    {
        Cmd = Command.KeymapRun,
        Target = target,
        Args = new ControlArgs { Name = name },
    });

    private async Task<string> NewSession()
    {
        var response = await Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { WorkspaceName = "work", CreateWorkspace = true, Cwd = _stateDir },
        });
        Assert.True(response.Ok, response.Error);
        return response.Result!.Id!;
    }

    private void ArmKeymapFile(string text)
    {
        File.WriteAllText(KeymapPath, text);
        _host.Actions.LoadKeymap();
    }

    [Fact]
    public async Task List_reports_the_keymap_file_path_the_actions_and_the_commands()
    {
        ArmKeymapFile("command \"Build\" ctrl+b dotnet build\n");

        var response = await List();

        Assert.True(response.Ok, response.Error);
        var keymap = response.Result!.Keymap!;
        Assert.Equal(KeymapPath, keymap.Path);
        Assert.Equal(41, keymap.Actions.Length);
        var command = Assert.Single(keymap.Commands);
        Assert.Equal("Build", command.Name);
        Assert.Equal("ctrl+b", command.Shortcut);
    }

    [Fact]
    public async Task List_follows_a_reload_of_a_changed_file()
    {
        ArmKeymapFile("command \"Build\" ctrl+b dotnet build\n");
        File.WriteAllText(KeymapPath, "command \"Test\" ctrl+t dotnet test\n");

        var reload = await Send(new ControlRequest { Cmd = Command.KeymapReload });
        var response = await List();

        Assert.True(reload.Ok, reload.Error);
        Assert.Equal("Test", Assert.Single(response.Result!.Keymap!.Commands).Name);
    }

    [Fact]
    public async Task A_command_only_the_reloaded_file_has_is_listed_and_runs_right_after_the_reload()
    {
        ArmKeymapFile("command \"Build\" ctrl+b dotnet build\n");
        await NewSession();
        string? seenName = null;
        _host.Actions.CustomCommandRunner = (_, _, command) =>
        {
            seenName = command.Name;
            return null;
        };
        File.WriteAllText(KeymapPath, "command \"Deploy\" ctrl+d dotnet publish\n");

        var reload = await Send(new ControlRequest { Cmd = Command.KeymapReload });
        var run = await Run("Deploy");
        var list = await List();

        Assert.True(reload.Ok, reload.Error);
        Assert.True(run.Ok, run.Error);
        Assert.Equal("Deploy", seenName);
        Assert.Equal("Deploy", Assert.Single(list.Result!.Keymap!.Commands).Name);
    }

    [Fact]
    public async Task Run_without_a_runner_says_custom_commands_are_not_available()
    {
        ArmKeymapFile("command \"Build\" ctrl+b dotnet build\n");

        var response = await Run("Build");

        Assert.False(response.Ok);
        Assert.Equal("custom commands are not available", response.Error);
    }

    [Fact]
    public async Task Run_of_an_unknown_name_says_so_before_anything_else()
    {
        ArmKeymapFile("command \"Build\" ctrl+b dotnet build\n");

        var response = await Run("Nope");

        Assert.Equal("no custom command named Nope", response.Error);
    }

    [Fact]
    public async Task Run_matches_the_name_exactly()
    {
        ArmKeymapFile("command \"Build\" ctrl+b dotnet build\n");

        var response = await Run(" Build");

        Assert.Equal("no custom command named  Build", response.Error);
    }

    [Fact]
    public async Task Run_hands_the_command_and_the_resolved_session_to_the_runner()
    {
        ArmKeymapFile("command \"Build\" ctrl+b dotnet build\n");
        var sessionId = await NewSession();
        Guid? seenSession = null;
        string? seenName = null;
        _host.Actions.CustomCommandRunner = (_, id, command) =>
        {
            seenSession = id;
            seenName = command.Name;
            return null;
        };

        var response = await Run("Build");

        Assert.True(response.Ok, response.Error);
        Assert.Equal(sessionId, response.Result!.Id);
        Assert.Equal(Guid.Parse(sessionId), seenSession);
        Assert.Equal("Build", seenName);
    }

    [Fact]
    public async Task Run_reports_the_reason_the_runner_gives()
    {
        ArmKeymapFile("command \"Build\" ctrl+b dotnet build\n");
        await NewSession();
        _host.Actions.CustomCommandRunner = (_, _, _) => "no shell";

        var response = await Run("Build");

        Assert.False(response.Ok);
        Assert.Equal("Build did not start: no shell", response.Error);
    }

    [Fact]
    public async Task Run_with_a_bad_target_gives_the_resolver_error_and_skips_the_runner()
    {
        ArmKeymapFile("command \"Build\" ctrl+b dotnet build\n");
        await NewSession();
        var called = false;
        _host.Actions.CustomCommandRunner = (_, _, _) =>
        {
            called = true;
            return null;
        };

        var response = await Run("Build", target: "zzzz");

        Assert.Equal("no such session: zzzz", response.Error);
        Assert.False(called);
    }
}
