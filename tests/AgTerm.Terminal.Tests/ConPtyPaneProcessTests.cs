using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AgTerm.Terminal.Tests;

/// <summary>
/// What closing a pane does to the processes started inside it. The pane runs cmd, cmd runs
/// PaneProcessHelper (attached to the pane console), and the helper starts the child under test.
/// </summary>
public class ConPtyPaneProcessTests : IDisposable
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

    private readonly StringBuilder _output = new();
    private readonly List<Process> _started = new();
    private readonly List<string> _markers = new();
    private ConPtySession? _pty;

    private sealed record Pane(ConPtySession Pty, Process Helper, Process Conhost, Process? Spawned);

    private Pane StartHelper(
        string mode, IReadOnlyDictionary<string, string>? environment = null,
        Func<IntPtr, long?>? consoleHostQuery = null)
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var assembly = typeof(PaneProcessHelper).Assembly.Location;
        var command = $"\"{dotnet}\" \"{assembly}\" {PaneProcessHelper.Scenario}";
        var extra = new Dictionary<string, string> { [PaneProcessHelper.ModeVariable] = mode };
        if (environment is not null)
            foreach (var (key, value) in environment) extra[key] = value;
        _pty = ConPtySession.Start(new ConPtyStartInfo
        {
            Argv = WindowsCommandLine.CmdShellArgv(Cmd, command),
            CommandLine = WindowsCommandLine.ForCmdShell(Cmd, command),
            ExtraEnvironment = extra,
            Cols = 200,
            Rows = 30,
            ConsoleHostQuery = consoleHostQuery,
            OnOutput = chunk =>
            {
                lock (_output) _output.Append(Encoding.UTF8.GetString(chunk));
            },
        });
        var text = WaitForOutput("READY");
        var helper = Pin(Pid(text, "HELPER"), own: false);
        _markers.Add(Path.Combine(Path.GetTempPath(), $"agterm-pane-helper-{helper.Id}.vbs"));
        var conhost = Pin(Pid(text, "CONHOST"), own: false);
        var spawned = text.Contains("SPAWNED pid=") ? Pin(Pid(text, "SPAWNED"), own: true) : null;
        return new Pane(_pty, helper, conhost, spawned);
    }

    private Process Pin(int pid, bool own)
    {
        var process = Process.GetProcessById(pid);
        _ = process.Handle; // holds the pid: it cannot be reused while the handle is open
        if (own) _started.Add(process);
        return process;
    }

    private static int Pid(string text, string name)
    {
        var match = Regex.Match(text, $@"{name} pid=(\d+)");
        Assert.True(match.Success, $"no {name} pid in:\n{text}");
        return int.Parse(match.Groups[1].Value);
    }

    private string WaitForOutput(string contains)
    {
        var deadline = Environment.TickCount64 + (long)Budget.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            string text;
            lock (_output) text = _output.ToString();
            if (text.Contains(contains)) return text;
            Thread.Sleep(25);
        }
        lock (_output) Assert.Fail($"output never contained \"{contains}\"; got:\n{_output}");
        return "";
    }

    private static void Close(ConPtySession pty)
    {
        pty.Kill();
        pty.Dispose();
    }

    public void Dispose()
    {
        _pty?.Dispose();
        foreach (var process in _started)
        {
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
            process.Dispose();
        }
        foreach (var marker in _markers)
        {
            try
            {
                File.Delete(marker);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void Closing_the_pane_ends_a_child_its_creator_left_suspended_and_the_console_host()
    {
        // the helper's child holds the console but never runs; nothing else will end it
        var pane = StartHelper("suspended");

        Close(pane.Pty);

        Assert.True(pane.Spawned!.WaitForExit(TimeSpan.FromSeconds(15)),
            "the suspended child outlived the pane");
        Assert.True(pane.Conhost.WaitForExit(TimeSpan.FromSeconds(15)),
            "the pane's console host outlived the pane");
    }

    [Fact]
    public void Closing_the_pane_sends_ctrl_close_to_an_attached_child()
    {
        // guard: the attached helper must get CTRL_CLOSE from conhost, not be killed before it
        var marker = TempMarker();
        var pane = StartHelper("ctrlclose", new Dictionary<string, string>
        {
            [PaneProcessHelper.MarkerVariable] = marker,
        });

        Close(pane.Pty);

        Assert.Equal("ctrl-close", WaitForMarker(marker));
        Assert.True(pane.Helper.WaitForExit(TimeSpan.FromSeconds(15)), "the helper outlived the pane");
    }

    [Fact]
    public void Closing_the_pane_ends_a_child_that_attaches_to_its_console_after_the_close()
    {
        // the helper's CTRL_CLOSE handler starts a ping that attaches to the closed console;
        // conhost already sent its CTRL_CLOSE, so the ping would run its full 30 s
        var marker = TempMarker();
        var pane = StartHelper("ctrlclose-spawn", new Dictionary<string, string>
        {
            [PaneProcessHelper.MarkerVariable] = marker,
        });
        var closedAt = DateTime.Now;

        Close(pane.Pty);

        var line = WaitForMarker(marker);
        Assert.StartsWith("ctrl-close spawned=", line);
        var late = TryPinPing(int.Parse(line["ctrl-close spawned=".Length..]), closedAt);
        if (late is not null)
            Assert.True(late.WaitForExit(TimeSpan.FromSeconds(15)),
                "the child that attached after the close outlived the pane");
        Assert.True(pane.Conhost.WaitForExit(TimeSpan.FromSeconds(15)),
            "the pane's console host outlived the pane");
    }

    public static TheoryData<string> FailingQueries =>
        new() { "fails", "throws", "unexpected", "fails-for-child", "throws-for-child" };

    [Theory]
    [MemberData(nameof(FailingQueries))]
    public void A_console_host_query_that_fails_leaves_the_pane_processes_as_they_were(string failure)
    {
        // the query is not documented: when it does not work, closing behaves as it did before
        // the sweep existed, so the suspended child stays and nothing throws. The "-for-child"
        // variants answer for attached processes and fail only for the suspended child.
        static long? Attached(IntPtr process) =>
            ConPtyNative.QueryConsoleHost(process) is long value && (value & 3) == 1 ? value : null;
        Func<IntPtr, long?> query = failure switch
        {
            "fails" => _ => null,
            "throws" => _ => throw new InvalidOperationException("query broke"),
            "unexpected" => _ => 0x7FFF_FFFF_FFFF_FFF3,
            "fails-for-child" => Attached,
            _ => process => Attached(process) ?? throw new InvalidOperationException("query broke"),
        };
        var pane = StartHelper("suspended", consoleHostQuery: query);

        Close(pane.Pty);

        // the suspended child keeps conhost, so the sweep runs until its limit; by the time the
        // helper is gone it has had its rounds
        Assert.True(pane.Helper.WaitForExit(TimeSpan.FromSeconds(15)), "the helper outlived the pane");
        Thread.Sleep(1000);
        Assert.False(pane.Spawned!.HasExited, "the sweep ended a process it could not query");
    }

    [Fact]
    public void A_child_whose_query_failed_at_close_is_not_taken_for_a_late_attacher()
    {
        // the query fails once, for the attached helper, while the close records who is attached;
        // later queries work. The helper was attached all along, so it gets CTRL_CLOSE only.
        var marker = TempMarker();
        var helperPid = 0;
        var failed = 0;
        long? Query(IntPtr process) =>
            ConPtyNative.GetProcessId(process) == Volatile.Read(ref helperPid)
            && Interlocked.Exchange(ref failed, 1) == 0
                ? null
                : ConPtyNative.QueryConsoleHost(process);
        var pane = StartHelper("ctrlclose", new Dictionary<string, string>
        {
            [PaneProcessHelper.MarkerVariable] = marker,
        }, Query);
        Volatile.Write(ref helperPid, pane.Helper.Id);

        Close(pane.Pty);

        Assert.Equal("ctrl-close", WaitForMarker(marker));
        Assert.True(pane.Helper.WaitForExit(TimeSpan.FromSeconds(15)), "the helper outlived the pane");
        Assert.Equal(1, Volatile.Read(ref failed));
        Assert.NotEqual(1, pane.Helper.ExitCode);
    }

    [Fact]
    public void A_program_in_the_pane_can_break_away_from_the_pane_job()
    {
        // the pane's job only lists processes; a program that asks to leave it (as `start` can)
        // may. When the host itself is in a job, the program leaves only the pane's job.
        var pane = StartHelper("breakaway");
        var text = WaitForOutput("BREAKAWAY ");

        Assert.NotNull(pane.Pty.PaneJobLimitFlags);
        Assert.NotEqual(0u, pane.Pty.PaneJobLimitFlags!.Value & JobObjectLimitBreakawayOk);
        Assert.Contains("BREAKAWAY ok", text);
        Close(pane.Pty);
    }

    private const uint JobObjectLimitBreakawayOk = 0x800;

    [Theory]
    [InlineData("detached")]
    [InlineData("newconsole")]
    [InlineData("gui")]
    [InlineData("freeconsole")]
    public async Task Closing_the_pane_leaves_a_child_that_does_not_use_its_console(string mode)
    {
        // DETACHED_PROCESS, CREATE_NEW_CONSOLE, GUI and FreeConsole children are like `start` and
        // `nohup`: they are not the pane's to end
        var pane = StartHelper(mode);

        Close(pane.Pty);

        Assert.True(pane.Conhost.WaitForExit(TimeSpan.FromSeconds(15)),
            "the pane's console host outlived the pane");
        await pane.Pty.DescendantSweep.WaitAsync(TimeSpan.FromSeconds(35));
        Thread.Sleep(500);
        Assert.False(pane.Spawned!.HasExited, $"the {mode} child ended with the pane");
    }

    private string TempMarker()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"agterm-pane-marker-{Guid.NewGuid():N}.txt");
        _markers.Add(marker);
        return marker;
    }

    private static string WaitForMarker(string path)
    {
        var deadline = Environment.TickCount64 + (long)Budget.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            try
            {
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path);
                    if (text.Length > 0) return text;
                }
            }
            catch (IOException)
            {
                // the helper is still writing it
            }
            Thread.Sleep(25);
        }
        Assert.Fail($"the marker {path} was never written");
        return "";
    }

    /// <summary>The ping the helper started, or null if that pid is already gone (or now belongs
    /// to another process, which means the ping is gone too).</summary>
    private Process? TryPinPing(int pid, DateTime notBefore)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
            _ = process.Handle;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        try
        {
            if (!process.ProcessName.Equals("PING", StringComparison.OrdinalIgnoreCase)
                || process.StartTime < notBefore.AddSeconds(-1))
            {
                process.Dispose();
                return null;
            }
        }
        catch (InvalidOperationException)
        {
            process.Dispose();
            return null;
        }
        _started.Add(process);
        return process;
    }
}
