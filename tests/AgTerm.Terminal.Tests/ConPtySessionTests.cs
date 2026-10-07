using System.Text;

namespace AgTerm.Terminal.Tests;

/// <summary>
/// Integration tests against a REAL pseudoconsole: each test spawns an actual child process under
/// CreatePseudoConsole and reads its live VT output. cmd.exe is the driver (always present; pwsh
/// may not be installed on a runner).
/// </summary>
public class ConPtySessionTests : IDisposable
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly StringBuilder _output = new();
    private ConPtySession? _pty;

    private ConPtySession Start(params string[] argv) => StartIn(null, argv);

    private ConPtySession StartIn(string? cwd, params string[] argv) =>
        StartWith(new ConPtyStartInfo { Argv = argv, Cwd = cwd });

    private ConPtySession StartWith(ConPtyStartInfo info)
    {
        _pty = ConPtySession.Start(info with
        {
            Cols = 120,
            Rows = 30,
            OnOutput = chunk =>
            {
                lock (_output) _output.Append(Encoding.UTF8.GetString(chunk));
            },
        });
        return _pty;
    }

    private string Snapshot()
    {
        lock (_output) return _output.ToString();
    }

    private string WaitForOutput(string contains, int timeoutMs = 15000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var text = Snapshot();
            if (text.Contains(contains)) return text;
            Thread.Sleep(25);
        }
        Assert.Fail($"output never contained \"{contains}\"; got:\n{Snapshot()}");
        return ""; // unreachable — Assert.Fail throws
    }

    public void Dispose() => _pty?.Dispose();

    [Fact]
    public async Task Spawns_command_and_reads_its_output()
    {
        var pty = Start(Cmd, "/c", "echo hello-conpty");
        WaitForOutput("hello-conpty");
        Assert.Equal(0, await pty.Exited.WaitAsync(TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public async Task Exit_code_propagates()
    {
        var pty = Start(Cmd, "/c", "exit 3");
        Assert.Equal(3, await pty.Exited.WaitAsync(TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public async Task Child_starts_in_the_requested_cwd()
    {
        var cwd = Path.GetTempPath().TrimEnd('\\');
        var pty = StartIn(cwd, Cmd, "/c", "cd");
        await pty.Exited.WaitAsync(TimeSpan.FromSeconds(15));
        // cmd's `cd` prints the working directory; ignore case (8.3/short names aside, temp is stable)
        Assert.Contains(cwd, Snapshot(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteText_types_into_a_live_shell()
    {
        var pty = Start(Cmd);
        WaitForOutput(">"); // the cmd prompt
        pty.WriteText("echo typed-marker-%RANDOM:~0,0%ok\r");
        WaitForOutput("typed-marker-ok"); // the echoed command AND its output both carry it
        pty.WriteText("exit\r");
        Assert.Equal(0, await pty.Exited.WaitAsync(TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void Initial_size_is_the_requested_one()
    {
        // mode con reports the console geometry conhost created for the pty
        Start(Cmd, "/c", "mode con");
        var text = WaitForOutput("Columns:");
        Assert.Contains("120", text);
        Assert.Contains("30", text);
    }

    [Fact]
    public void Resize_is_visible_to_the_child()
    {
        var pty = Start(Cmd);
        WaitForOutput(">");
        pty.Resize(cols: 97, rows: 41);
        pty.WriteText("mode con\r");
        var text = WaitForOutput("Lines:");
        Assert.Contains("97", text);
        Assert.Contains("41", text);
    }

    [Fact]
    public async Task Extra_environment_reaches_the_child_on_top_of_the_inherited_one()
    {
        var pty = StartWith(new ConPtyStartInfo
        {
            // PATH must still be inherited (cmd itself resolves through it), AGTERM_PANE_ID added
            Argv = new[] { Cmd, "/c", "echo token=%AGTERM_PANE_ID% comspec=%COMSPEC%" },
            ExtraEnvironment = new Dictionary<string, string> { ["AGTERM_PANE_ID"] = "pane-abc123" },
        });
        await pty.Exited.WaitAsync(TimeSpan.FromSeconds(15));
        WaitForOutput("token=pane-abc123");
        WaitForOutput("comspec=C:\\"); // an inherited variable survived
    }

    [Fact]
    public async Task Removed_environment_variables_are_absent_from_the_child()
    {
        // an inherited variable that names the WRONG terminal has to go, not be overwritten with
        // a placeholder: Claude Code branches on TERM_PROGRAM being set at all
        Environment.SetEnvironmentVariable("AGTERM_TEST_INHERITED", "leaks");
        try
        {
            var pty = StartWith(new ConPtyStartInfo
            {
                Argv = new[] { Cmd, "/c", "echo probe=[%AGTERM_TEST_INHERITED%]" },
                RemoveEnvironment = new[] { "AGTERM_TEST_INHERITED" },
            });
            await pty.Exited.WaitAsync(TimeSpan.FromSeconds(15));
            // cmd leaves an unset variable's reference verbatim
            WaitForOutput("probe=[%AGTERM_TEST_INHERITED%]");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGTERM_TEST_INHERITED", null);
        }
    }

    [Fact]
    public async Task Removal_loses_to_an_explicit_extra_of_the_same_name()
    {
        Environment.SetEnvironmentVariable("AGTERM_TEST_INHERITED", "old");
        try
        {
            var pty = StartWith(new ConPtyStartInfo
            {
                Argv = new[] { Cmd, "/c", "echo probe=%AGTERM_TEST_INHERITED%" },
                ExtraEnvironment = new Dictionary<string, string> { ["AGTERM_TEST_INHERITED"] = "new" },
                RemoveEnvironment = new[] { "AGTERM_TEST_INHERITED" },
            });
            await pty.Exited.WaitAsync(TimeSpan.FromSeconds(15));
            WaitForOutput("probe=new");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGTERM_TEST_INHERITED", null);
        }
    }

    [Fact]
    public async Task Dispose_kills_a_live_shell()
    {
        var pty = Start(Cmd);
        WaitForOutput(">");
        pty.Dispose();
        // the child must be gone; a killed conpty child reports a nonzero exit
        await pty.Exited.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task OutputCompleted_completes_only_after_last_OnOutput_callback_returns()
    {
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var callbackGate = new ManualResetEventSlim();

        var info = new ConPtyStartInfo
        {
            Argv = new[] { Cmd, "/c", "echo output-completion-marker" },
            Cols = 120,
            Rows = 30,
            OnOutput = chunk =>
            {
                if (chunk.Length == 0) return;
                callbackStarted.TrySetResult();
                callbackGate.Wait();
            },
        };

        using var pty = ConPtySession.Start(info);
        try
        {
            await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await pty.Exited.WaitAsync(TimeSpan.FromSeconds(15));

            Assert.False(pty.OutputCompleted.IsCompleted);
        }
        finally
        {
            callbackGate.Set();
        }

        await pty.OutputCompleted.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
