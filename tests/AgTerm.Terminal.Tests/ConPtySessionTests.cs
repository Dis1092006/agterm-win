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

    /// <summary>How long a spawned child is given. These are correctness tests, not performance
    /// ones, and the whole solution runs its test assemblies in parallel: spawning a real process
    /// and draining a pseudoconsole can take far longer than it does with the suite to itself, so
    /// the budget is generous on purpose. A deadlocked test still fails, it just takes a minute.
    /// </summary>
    private static readonly TimeSpan ChildBudget = TimeSpan.FromSeconds(60);

    private string WaitForOutput(string contains, int timeoutMs = 60000)
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
        Assert.Equal(0, await pty.Exited.WaitAsync(ChildBudget));
    }

    [Fact]
    public void A_fresh_pseudoconsole_clears_the_screen()
    {
        Start(Cmd, "/c", "echo fresh-marker");
        Assert.Contains("\x1b[2J", WaitForOutput("fresh-marker"));
    }

    [Fact]
    public async Task InheritCursor_asks_for_the_cursor_and_keeps_the_screen()
    {
        var pty = StartWith(new ConPtyStartInfo
        {
            Argv = [Cmd, "/c", "echo inherit-marker"],
            InheritCursor = true,
        });
        WaitForOutput("\x1b[6n");
        pty.WriteText("\x1b[5;1R"); // the terminal's cursor-position report: row 5 is below restored text

        var text = WaitForOutput("inherit-marker");
        Assert.DoesNotContain("\x1b[2J", text);
        Assert.Equal(0, await pty.Exited.WaitAsync(ChildBudget));
    }

    [Fact]
    public async Task Dispose_after_a_write_to_a_closed_console_does_not_throw()
    {
        // The page can answer a pty query after the child is gone, as xterm does for ConPTY's
        // cursor position request. The failed write must leave nothing behind for Dispose to
        // flush into the closed pipe (CI: IOException "The pipe is being closed" from Dispose).
        var pty = Start(Cmd, "/c", "exit 0");
        await pty.Exited.WaitAsync(ChildBudget);
        await pty.OutputCompleted.WaitAsync(ChildBudget);

        pty.WriteText("\x1b[1;1R");

        _pty = null;
        pty.Dispose();
    }

    [Fact]
    public async Task Exit_code_propagates()
    {
        var pty = Start(Cmd, "/c", "exit 3");
        Assert.Equal(3, await pty.Exited.WaitAsync(ChildBudget));
    }

    [Fact]
    public async Task CommandLine_reaches_cmd_unchanged_with_quotes_and_a_quoted_path_with_spaces()
    {
        var dir = Directory.CreateTempSubdirectory("agterm cmdline ");
        try
        {
            var script = Path.Combine(dir.FullName, "say hi.cmd");
            File.WriteAllText(script, "@echo ARGS=[%~1] [%~2]\r\n@exit /b 7\r\n");
            var command = $"echo \"quoted text\" & \"{script}\" \"two words\" x";
            var pty = StartWith(new ConPtyStartInfo
            {
                Argv = WindowsCommandLine.CmdShellArgv(Cmd, command),
                CommandLine = WindowsCommandLine.ForCmdShell(Cmd, command),
            });

            Assert.Equal(7, await pty.Exited.WaitAsync(ChildBudget));
            await pty.OutputCompleted.WaitAsync(ChildBudget);
            var text = Snapshot();
            Assert.Contains("\"quoted text\"", text);
            Assert.DoesNotContain("\\\"", text);
            Assert.Contains("ARGS=[two words] [x]", text);
        }
        finally
        {
            try { dir.Delete(recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Child_starts_in_the_requested_cwd()
    {
        var cwd = Path.GetTempPath().TrimEnd('\\');
        var pty = StartIn(cwd, Cmd, "/c", "cd");
        await pty.Exited.WaitAsync(ChildBudget);
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
        Assert.Equal(0, await pty.Exited.WaitAsync(ChildBudget));
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
        await pty.Exited.WaitAsync(ChildBudget);
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
            await pty.Exited.WaitAsync(ChildBudget);
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
            await pty.Exited.WaitAsync(ChildBudget);
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
        await pty.Exited.WaitAsync(ChildBudget);
    }

    [Fact]
    public async Task Kill_ends_a_live_shell_and_the_session_still_drains_and_disposes()
    {
        var pty = Start(Cmd);
        WaitForOutput(">");
        pty.Kill();
        Assert.Equal(1, await pty.Exited.WaitAsync(ChildBudget));
        await pty.OutputCompleted.WaitAsync(ChildBudget);
        pty.Kill();
        pty.Dispose();
        pty.Kill();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_ends_a_grandchild_attached_to_the_console(bool killFirst)
    {
        // the grandchild keeps the cwd open while it lives: a cwd that can be deleted means it is gone
        var dir = Directory.CreateTempSubdirectory("agterm grandchild ");
        try
        {
            var command = "echo grandchild-started& ping -n 31 127.0.0.1 > nul";
            var pty = StartWith(new ConPtyStartInfo
            {
                Argv = WindowsCommandLine.CmdShellArgv(Cmd, command),
                CommandLine = WindowsCommandLine.ForCmdShell(Cmd, command),
                Cwd = dir.FullName,
            });
            WaitForOutput("grandchild-started");
            Thread.Sleep(500);

            if (killFirst)
            {
                pty.Kill();
                await pty.Exited.WaitAsync(ChildBudget);
            }
            pty.Dispose();

            await pty.Exited.WaitAsync(TimeSpan.FromSeconds(15));
            var drained = await Task.WhenAny(pty.OutputCompleted, Task.Delay(TimeSpan.FromSeconds(15)));
            Assert.True(drained == pty.OutputCompleted, "the output reader did not reach EOF after Dispose");
            var deadline = Environment.TickCount64 + 15000;
            while (true)
            {
                try
                {
                    dir.Delete(recursive: true);
                    break;
                }
                catch (IOException) when (Environment.TickCount64 < deadline)
                {
                    Thread.Sleep(100);
                }
            }
        }
        finally
        {
            try { dir.Delete(recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Dispose_kills_the_child_and_releases_a_write_stuck_behind_undrained_output()
    {
        // The reader hands output to a consumer that stops taking it (the UI thread is busy). The
        // output pipe fills, conhost stops reading input, and the next write blocks. Dispose on
        // another thread must still kill the child, and the stuck write must return.
        var readerBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var readerGate = new ManualResetEventSlim();
        var info = new ConPtyStartInfo
        {
            Argv = new[] { Cmd, "/c", "for /l %i in (0,0,1) do @echo flood-flood-flood-flood-flood-flood-flood-flood-flood-flood" },
            Cols = 120,
            Rows = 30,
            OnOutput = _ =>
            {
                readerBlocked.TrySetResult();
                readerGate.Wait();
            },
        };
        var pty = ConPtySession.Start(info);
        Thread? writer = null;
        var stopWriting = 0;
        long writeStartedAt = 0;
        try
        {
            await readerBlocked.Task.WaitAsync(ChildBudget);

            var chunk = Enumerable.Repeat((byte)'a', 4096).ToArray();
            writer = new Thread(() =>
            {
                while (Volatile.Read(ref stopWriting) == 0)
                {
                    Volatile.Write(ref writeStartedAt, Environment.TickCount64);
                    pty.Write(chunk);
                    Volatile.Write(ref writeStartedAt, 0);
                }
            })
            { IsBackground = true, Name = "stuck-writer" };
            writer.Start();

            var deadline = Environment.TickCount64 + (long)ChildBudget.TotalMilliseconds;
            while (true)
            {
                var startedAt = Volatile.Read(ref writeStartedAt);
                if (startedAt != 0 && Environment.TickCount64 - startedAt > 1000) break;
                Assert.True(Environment.TickCount64 < deadline, "no write got stuck behind the undrained output");
                Thread.Sleep(50);
            }

            Volatile.Write(ref stopWriting, 1);
            var disposer = new Thread(pty.Dispose) { IsBackground = true, Name = "disposer" };
            disposer.Start();

            var exited = await Task.WhenAny(pty.Exited, Task.Delay(TimeSpan.FromSeconds(15)));
            Assert.True(exited == pty.Exited,
                $"child still alive 15 s after Dispose; disposer alive={disposer.IsAlive}, writer alive={writer.IsAlive}");
            Assert.True(writer.Join(TimeSpan.FromSeconds(15)), "the stuck write did not return after Dispose");
            Assert.True(disposer.Join(TimeSpan.FromSeconds(15)), "Dispose did not return");
        }
        finally
        {
            Volatile.Write(ref stopWriting, 1);
            readerGate.Set();
            pty.Dispose();
            writer?.Join(TimeSpan.FromSeconds(15));
        }
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
            await callbackStarted.Task.WaitAsync(ChildBudget);
            await pty.Exited.WaitAsync(ChildBudget);

            Assert.False(pty.OutputCompleted.IsCompleted);
        }
        finally
        {
            callbackGate.Set();
        }

        await pty.OutputCompleted.WaitAsync(ChildBudget);
    }
}
