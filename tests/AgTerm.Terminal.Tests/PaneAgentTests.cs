using AgTerm.Core.Model;

namespace AgTerm.Terminal.Tests;

public sealed class PaneAgentTests
{
    private const long PaneConhost = 4000L | 1;

    [Theory]
    [InlineData(@"C:\Users\alice\.local\bin\claude.exe", SessionAgent.Claude)]
    [InlineData(@"C:\Program Files\Claude\CLAUDE.EXE", SessionAgent.Claude)]
    [InlineData(@"C:\Users\alice\AppData\Local\Microsoft\WinGet\Links\copilot.exe", SessionAgent.Copilot)]
    [InlineData(@"C:\npm\node_modules\@openai\codex-win32-x64\bin\codex.exe", SessionAgent.Codex)]
    public void Known_agent_executables_are_classified_by_file_name(string imagePath, SessionAgent expected) =>
        Assert.Equal(expected, PaneAgent.ClassifyImage(imagePath));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe")]
    [InlineData(@"C:\Program Files\nodejs\node.exe")]
    [InlineData(@"C:\npm\codex-command-runner.exe")]
    [InlineData(@"C:\npm\codex-voice-host.exe")]
    [InlineData(@"C:\npm\codex-code-mode-host.exe")]
    public void Non_agent_and_helper_executables_are_ignored(string? imagePath) =>
        Assert.Null(PaneAgent.ClassifyImage(imagePath));

    [Fact]
    public void Detached_and_foreign_console_processes_are_ignored_when_pane_console_is_known()
    {
        var candidates = new[]
        {
            new PaneAgent.Candidate(Pid: 100, CreationTime: 10, ConsoleHostValue: 1L, SessionAgent.Codex),
            new PaneAgent.Candidate(Pid: 101, CreationTime: 20, ConsoleHostValue: 9000L | 1, SessionAgent.Claude),
            new PaneAgent.Candidate(Pid: 102, CreationTime: 30, ConsoleHostValue: 5000L | 2, SessionAgent.Copilot),
        };

        Assert.Null(PaneAgent.Pick(candidates, PaneConhost));
    }

    [Fact]
    public void Earliest_attached_agent_wins_over_nested_subprocesses()
    {
        var candidates = new[]
        {
            new PaneAgent.Candidate(Pid: 250, CreationTime: 500, ConsoleHostValue: PaneConhost, SessionAgent.Codex),
            new PaneAgent.Candidate(Pid: 120, CreationTime: 100, ConsoleHostValue: PaneConhost, SessionAgent.Claude),
            new PaneAgent.Candidate(Pid: 300, CreationTime: 600, ConsoleHostValue: PaneConhost, SessionAgent.Copilot),
        };

        var picked = PaneAgent.Pick(candidates, PaneConhost);
        Assert.NotNull(picked);
        Assert.Equal(120, picked.Value.Pid);
        Assert.Equal(SessionAgent.Claude, picked.Value.Agent);
    }
}
