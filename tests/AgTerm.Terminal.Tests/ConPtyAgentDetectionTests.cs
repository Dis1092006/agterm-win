using System.Diagnostics;
using AgTerm.Core.Model;

namespace AgTerm.Terminal.Tests;

public sealed class ConPtyAgentDetectionTests : IDisposable
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(15);

    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-agent-pty-").FullName;
    private readonly List<ConPtySession> _sessions = [];

    public ConPtyAgentDetectionTests()
    {
        var ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
        File.Copy(ping, Binary("claude.exe"));
        File.Copy(ping, Binary("copilot.exe"));
        File.Copy(ping, Binary("codex.exe"));
    }

    private string Binary(string name) => Path.Combine(_dir, name);

    public void Dispose()
    {
        foreach (var session in _sessions)
        {
            session.Dispose();
        }
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private ConPtySession StartShell()
    {
        var session = ConPtySession.Start(new ConPtyStartInfo
        {
            Argv = [Cmd],
            CommandLine = $"\"{Cmd}\" /Q /D",
            Cwd = _dir,
        });
        _sessions.Add(session);
        return session;
    }

    private static void WaitUntil(Func<bool> predicate, string description)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < Budget)
        {
            if (predicate()) return;
            Thread.Sleep(20);
        }
        Assert.Fail($"Timed out waiting for: {description}");
    }

    [Fact]
    public void Direct_command_agent_is_detected_at_launch_and_cleared_on_exit()
    {
        var session = ConPtySession.Start(new ConPtyStartInfo
        {
            Argv = [Binary("copilot.exe"), "-n", "30", "127.0.0.1"],
            Cwd = _dir,
        });
        _sessions.Add(session);

        WaitUntil(() => session.CurrentAgent == SessionAgent.Copilot, "Copilot detected on direct launch");

        session.Kill();
        WaitUntil(() => session.CurrentAgent is null, "Copilot cleared after kill");
    }

    [Fact]
    public void Shell_pane_detects_agent_launch_and_clears_when_agent_exits_or_is_killed()
    {
        var events = new List<SessionAgent?>();
        var gate = new Lock();
        var session = StartShell();
        session.AgentChanged += agent =>
        {
            lock (gate) events.Add(agent);
        };

        Assert.Null(session.CurrentAgent);

        // Short-lived agent exits cleanly on its own without further user input.
        session.WriteText($"\"{Binary("codex.exe")}\" -n 2 127.0.0.1\r\n");
        WaitUntil(() => session.CurrentAgent == SessionAgent.Codex, "Codex detected in shell");
        WaitUntil(() => session.CurrentAgent is null, "Codex cleared after clean exit");

        // Long-lived agent killed externally clears immediately via kernel process wait.
        session.WriteText($"\"{Binary("claude.exe")}\" -n 30 127.0.0.1\r\n");
        WaitUntil(() => session.CurrentAgent == SessionAgent.Claude, "Claude detected in shell");

        var claudeProc = Process.GetProcessesByName("claude")
            .FirstOrDefault(p =>
            {
                try { return string.Equals(p.MainModule?.FileName, Binary("claude.exe"), StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            });
        Assert.NotNull(claudeProc);
        using (claudeProc)
        {
            claudeProc.Kill();
        }

        WaitUntil(() => session.CurrentAgent is null, "Claude cleared after external kill");

        lock (gate)
        {
            Assert.Contains(SessionAgent.Codex, events);
            Assert.Contains(SessionAgent.Claude, events);
            Assert.Null(events[^1]);
        }
    }
}
