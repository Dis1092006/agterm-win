using System.Diagnostics;
using System.Text.Json;
using AgTerm.Core.Install;

namespace AgTerm.Core.Tests.Install;

public sealed class CopilotLifecycleTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), $"agterm-copilot-{Guid.NewGuid():N}");
    private string Capture => Path.Join(_root, "capture.txt");
    private string Scripts => Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "..", "..", "..", "..",
        "src", "AgTerm.Xterm", "AgentStatus"));

    public CopilotLifecycleTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Join(_root, AgentHooksInstaller.WrapperName),
            "@echo off\r\necho %*>> \"%AGTERM_CAPTURE%\"\r\nexit /b 0\r\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Hook(string name, string session = "parent", long timestamp = 1, string? reason = null,
        bool? recoverable = null, string? agentId = null, string pane = "token-1", bool inside = true,
        string? rawInput = null, string? parentAgentId = null)
    {
        var source = Path.Join(Scripts, "agterm-copilot-lifecycle.ps1");
        Assert.True(File.Exists(source), "The Copilot lifecycle handler is missing");
        File.Copy(source, Path.Join(_root, Path.GetFileName(source)), overwrite: true);
        var input = JsonSerializer.Serialize(new
        {
            sessionId = session, timestamp, reason, recoverable, agentId, parentAgentId,
            notification_type = "permission_prompt", prompt = "PRIVATE PROMPT",
            error = new { message = "PRIVATE ERROR", stack = "PRIVATE STACK" },
            transcriptPath = "PRIVATE TRANSCRIPT"
        });
        var start = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", Path.Join(_root, Path.GetFileName(source)), name, "agterm-win" })
            start.ArgumentList.Add(arg);
        start.Environment["AGTERM_CAPTURE"] = Capture;
        start.Environment["AGTERM_SESSION_ID"] = inside ? "ABC" : "";
        start.Environment["AGTERM_SOCKET"] = "pipe";
        start.Environment["AGTERM_PANE_ID"] = pane;
        using var child = Process.Start(start)!;
        child.StandardInput.Write(rawInput ?? input);
        child.StandardInput.Close();
        var output = child.StandardOutput.ReadToEnd();
        var error = child.StandardError.ReadToEnd();
        Assert.True(child.WaitForExit(15000), "Copilot hook did not finish");
        Assert.Equal(0, child.ExitCode);
        Assert.Equal("", output);
        Assert.Equal("", error);
    }

    private string[] Statuses => File.Exists(Capture)
        ? File.ReadAllLines(Capture).Select(line => line.Split(' ')[0]).ToArray()
        : [];

    [Fact]
    public void SuccessfulPromptKeepsTheCompletedIndicationOnSessionEnd()
    {
        Hook("sessionStart");
        Hook("userPromptSubmitted", timestamp: 2);
        Hook("agentStop", timestamp: 3);
        Hook("sessionEnd", timestamp: 4, reason: "complete");
        Assert.Equal(new[] { "active", "completed" }, Statuses);
        Assert.Contains("--auto-reset", File.ReadAllText(Capture));
    }

    [Fact]
    public void FatalErrorClearsActiveButRecoverableErrorDoesNot()
    {
        Hook("sessionStart");
        Hook("userPromptSubmitted", timestamp: 2);
        Hook("errorOccurred", timestamp: 3, recoverable: true);
        Assert.Equal(new[] { "active" }, Statuses);
        Hook("errorOccurred", timestamp: 4, recoverable: false);
        Hook("sessionEnd", timestamp: 5, reason: "error");
        Assert.Equal(new[] { "active", "idle" }, Statuses);
    }

    [Theory]
    [InlineData("user_exit")]
    [InlineData("abort")]
    [InlineData("timeout")]
    [InlineData("error")]
    [InlineData("complete")]
    public void EndingAnUnfinishedPromptClearsItsStatus(string reason)
    {
        Hook("sessionStart");
        Hook("userPromptSubmitted", timestamp: 2);
        Hook("notification", timestamp: 3);
        Hook("sessionEnd", timestamp: 4, reason: reason);
        Assert.Equal(new[] { "active", "blocked", "idle" }, Statuses);
    }

    [Fact]
    public void DetachedOldSessionEndCannotEraseTheReplacementPrompt()
    {
        Hook("sessionStart", "old", 1);
        Hook("userPromptSubmitted", "old", 2);
        Hook("sessionStart", "new", 4);
        Hook("userPromptSubmitted", "new", 5);
        Hook("sessionEnd", "old", 3, reason: "user_exit");
        Hook("errorOccurred", "old", 6, recoverable: false);
        Assert.Equal(new[] { "active", "idle", "active" }, Statuses);
    }

    [Fact]
    public void ReplacementSessionClearsAnUnfinishedStatusEvenBeforeItsFirstPrompt()
    {
        Hook("sessionStart", "old", 1);
        Hook("userPromptSubmitted", "old", 2);
        Hook("notification", "old", 3);
        Hook("sessionStart", "new", 5);
        Hook("sessionEnd", "old", 4, reason: "user_exit");
        Assert.Equal(new[] { "active", "blocked", "idle" }, Statuses);
    }

    [Fact]
    public void ReplacementSessionDoesNotEraseACompletedIndication()
    {
        Hook("userPromptSubmitted", "old", 1);
        Hook("agentStop", "old", 2);
        Hook("sessionStart", "new", 4);
        Hook("sessionEnd", "old", 3, reason: "user_exit");
        Assert.Equal(new[] { "active", "completed" }, Statuses);
    }

    [Fact]
    public void DelayedNotificationsAndStartsCannotOverwriteNewerState()
    {
        Hook("userPromptSubmitted", timestamp: 5);
        Hook("agentStop", timestamp: 7);
        Hook("notification", timestamp: 6);
        Hook("sessionStart", "old", 1);
        Hook("sessionEnd", "old", 2, reason: "user_exit");
        Assert.Equal(new[] { "active", "completed" }, Statuses);
    }

    [Fact]
    public void EventsAfterTerminationCannotBringBackTheOldStatus()
    {
        Hook("userPromptSubmitted");
        Hook("sessionEnd", timestamp: 2, reason: "user_exit");
        Hook("notification", timestamp: 3);
        Hook("postToolUse", timestamp: 4);
        Hook("agentStop", timestamp: 5);
        Assert.Equal(new[] { "active", "idle" }, Statuses);
    }

    [Fact]
    public void FatalErrorDoesNotPreventTheNextPromptInTheSameSession()
    {
        Hook("userPromptSubmitted");
        Hook("errorOccurred", timestamp: 2, recoverable: false);
        Hook("userPromptSubmitted", timestamp: 3);
        Hook("errorOccurred", timestamp: 2, recoverable: false);
        Assert.Equal(new[] { "active", "idle", "active" }, Statuses);
    }

    [Fact]
    public void SubagentLifecycleAndErrorsLeaveTheParentStatusAlone()
    {
        Hook("userPromptSubmitted");
        foreach (var name in new[] { "sessionStart", "userPromptSubmitted", "postToolUse", "agentStop",
            "notification", "errorOccurred", "sessionEnd" })
            Hook(name, timestamp: 2, reason: "error", recoverable: false, agentId: "child");
        Hook("errorOccurred", timestamp: 2, recoverable: false, parentAgentId: "ancestor");
        Assert.Equal(new[] { "active" }, Statuses);
    }

    [Fact]
    public void MalformedPayloadIsLoggedWithoutRecordingItsContents()
    {
        Hook("userPromptSubmitted", rawInput: "{PRIVATE PROMPT PRIVATE TRANSCRIPT PRIVATE CREDENTIAL");
        var log = File.ReadAllText(Path.Join(_root, "agent-status.log"));
        Assert.Contains("Copilot status hook failed", log);
        Assert.DoesNotContain("PRIVATE", log);
        Assert.Empty(Statuses);
    }

    [Fact]
    public void SurfaceStateIsIndependentAndStoresNoPayloadText()
    {
        Hook("userPromptSubmitted");
        Hook("userPromptSubmitted", "right", 2, pane: "token-2");
        Hook("sessionEnd", timestamp: 3, reason: "user_exit");
        Assert.Equal(new[] { "active", "active", "idle" }, Statuses);
        foreach (var file in Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories))
            Assert.DoesNotContain("PRIVATE", File.ReadAllText(file));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void HooksOutsideAgtermDoNothing()
    {
        Hook("userPromptSubmitted", inside: false);
        Assert.Empty(Statuses);
        Assert.Empty(Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories));
    }
}
