using AgTerm.ControlServer;
using AgTerm.Core.Hooks;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

public sealed class HookProcessRunnerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "agterm-hook-" + Guid.NewGuid().ToString("N"));
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(20);

    public HookProcessRunnerTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    private static ControlEvent Event(
        ControlEventKind kind = ControlEventKind.Status,
        string? session = "S1",
        string? workspace = "W1",
        string? window = "WIN1",
        string? status = "blocked",
        string? host = null) => new()
        {
            Seq = 7,
            Ts = 1.5,
            Kind = kind,
            Session = session,
            Workspace = workspace,
            Window = window,
            Payload = new ControlEventPayload { Status = status, Host = host },
        };

    private static HookEntry Entry(string command, ControlEventKind kind = ControlEventKind.Status) =>
        new(new HookIdentity(kind, command), 1);

    private sealed record Outcome(int Pid, int ExitCode, List<string> Failures);

    private async Task<Outcome> RunAsync(HookProcessRunner runner, string command, ControlEvent? ev = null)
    {
        var exit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new List<string>();
        var pid = runner.Start(Entry(command), ev ?? Event(),
            failure => { lock (failures) failures.Add(failure); },
            code => exit.TrySetResult(code));
        var code = await exit.Task.WaitAsync(_timeout);
        lock (failures) return new Outcome(pid, code, failures.ToList());
    }

    private string OutFile => Path.Combine(_directory, "out.txt");

    [Fact]
    public async Task The_event_is_written_to_stdin_as_one_json_line()
    {
        var runner = new HookProcessRunner(() => "pipe");
        var ev = Event();

        var outcome = await RunAsync(runner, $"findstr \"^\" > \"{OutFile}\"", ev);

        Assert.Equal(0, outcome.ExitCode);
        Assert.Empty(outcome.Failures);
        Assert.True(outcome.Pid > 0);
        Assert.Equal(ControlJson.Serialize(ev) + "\n", (await File.ReadAllTextAsync(OutFile)).Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task The_fixed_variables_come_from_the_event_and_the_socket_provider()
    {
        var runner = new HookProcessRunner(() => "the-pipe");
        var ev = Event(status: "active", host: "box");

        await RunAsync(runner,
            $"echo %AGT_EVENT_KIND%^|%AGT_EVENT_STATUS%^|%AGT_EVENT_HOST%^|%AGT_SESSION_ID%^|%AGT_WORKSPACE_ID%^|%AGT_WINDOW_ID%^|%AGT_SOCKET% > \"{OutFile}\"",
            ev);

        Assert.Equal("status|active|box|S1|W1|WIN1|the-pipe", (await File.ReadAllTextAsync(OutFile)).Trim());
    }

    [Fact]
    public async Task A_field_the_event_lacks_is_set_empty_even_when_the_environment_holds_one()
    {
        var runner = new HookProcessRunner(() => "pipe");
        var ev = Event(kind: ControlEventKind.AppLaunched, session: null, workspace: null, window: null, status: null);
        var script = Path.Combine(_directory, "probe.cmd");
        await File.WriteAllTextAsync(script,
            $"@echo [%AGT_SESSION_ID%][%AGT_WORKSPACE_ID%][%AGT_WINDOW_ID%][%AGT_EVENT_STATUS%][%AGT_EVENT_HOST%]> \"{OutFile}\"\r\n");
        Environment.SetEnvironmentVariable("AGT_SESSION_ID", "stale");
        try
        {
            await RunAsync(runner, $"call \"{script}\"", ev);
        }
        finally { Environment.SetEnvironmentVariable("AGT_SESSION_ID", null); }

        Assert.Equal("[][][][][]", (await File.ReadAllTextAsync(OutFile)).Trim());
    }

    [Fact]
    public async Task The_cli_directory_leads_the_path()
    {
        var cliDirectory = Path.Combine(_directory, "cli");
        var runner = new HookProcessRunner(() => "pipe", cliDirectory);

        await RunAsync(runner, $"echo %PATH% > \"{OutFile}\"");

        Assert.StartsWith(cliDirectory + ";", (await File.ReadAllTextAsync(OutFile)).Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_exit_code_is_reported()
    {
        var outcome = await RunAsync(new HookProcessRunner(() => "pipe"), "exit 7");

        Assert.Equal(7, outcome.ExitCode);
        Assert.Empty(outcome.Failures);
    }

    [Fact]
    public async Task A_child_that_never_reads_stdin_is_not_a_delivery_failure()
    {
        var outcome = await RunAsync(new HookProcessRunner(() => "pipe"), "exit 0");

        Assert.Empty(outcome.Failures);
    }

    [Fact]
    public void A_missing_shell_throws_and_starts_nothing()
    {
        var runner = new HookProcessRunner(() => "pipe", shellPath: Path.Combine(_directory, "no-such-shell.exe"));
        var called = false;

        Assert.ThrowsAny<Exception>(() => runner.Start(
            Entry("exit 0"), Event(), _ => called = true, _ => called = true));

        Assert.False(called);
    }

    [Fact]
    public async Task An_encode_failure_is_reported_before_the_exit()
    {
        var runner = new HookProcessRunner(() => "pipe",
            encode: _ => throw new InvalidOperationException("boom"));

        var outcome = await RunAsync(runner, "exit 0");

        Assert.Equal(["encode: boom"], outcome.Failures);
    }

    [Fact]
    public async Task Output_from_the_child_does_not_block_it()
    {
        var outcome = await RunAsync(new HookProcessRunner(() => "pipe"),
            "for /l %i in (1,1,2000) do @echo line %i & echo err %i 1>&2");

        Assert.Equal(0, outcome.ExitCode);
    }
}
