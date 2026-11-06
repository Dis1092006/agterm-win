using System.Text.Json;
using System.Diagnostics;
using AgTerm.Core.Install;

namespace AgTerm.Core.Tests.Install;

public sealed class AgentHooksInstallerTests : IDisposable
{
    private readonly string _home = Path.Join(Path.GetTempPath(), $"agterm-hooks-{Guid.NewGuid():N}");

    public AgentHooksInstallerTests()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(Path.Join(_home, "agtermctl.exe"), "");
    }
    public void Dispose() => Directory.Delete(_home, recursive: true);

    [Fact]
    public void MergeAddsFiveClaudeEventsAndPreservesUserHooks()
    {
        const string existing = """{"theme":"dark","hooks":{"Stop":[{"hooks":[{"type":"command","command":"my-hook"}]}]}}""";
        var result = AgentHooksInstaller.MergeClaude(existing, @"C:\a b\agterm-agent-status.cmd");
        Assert.True(result.Changed);
        using var json = JsonDocument.Parse(result.Contents);
        Assert.Equal("dark", json.RootElement.GetProperty("theme").GetString());
        var hooks = json.RootElement.GetProperty("hooks");
        Assert.Equal("my-hook", hooks.GetProperty("Stop")[0].GetProperty("hooks")[0].GetProperty("command").GetString());
        Assert.Equal(2, hooks.GetProperty("Stop").GetArrayLength());
        Assert.Equal("permission_prompt", hooks.GetProperty("Notification")[0].GetProperty("matcher").GetString());
        Assert.Contains("blocked", hooks.GetProperty("PermissionRequest")[0].GetProperty("hooks")[0]
            .GetProperty("args").EnumerateArray().Select(value => value.GetString()));
        foreach (var eventName in new[] { "UserPromptSubmit", "PostToolUse", "Stop", "Notification", "PermissionRequest" })
        {
            var handler = hooks.GetProperty(eventName)[hooks.GetProperty(eventName).GetArrayLength() - 1]
                .GetProperty("hooks")[0];
            Assert.Equal("cmd.exe", handler.GetProperty("command").GetString());
            Assert.Contains(@"C:\a b\agterm-agent-status.cmd",
                handler.GetProperty("args").EnumerateArray().Select(arg => arg.GetString()));
        }
        Assert.False(AgentHooksInstaller.MergeClaude(result.Contents, @"C:\a b\agterm-agent-status.cmd").Changed);
    }

    [Fact]
    public void MergeUpdatesOwnedPathWithoutDuplicatingHooks()
    {
        var first = AgentHooksInstaller.MergeClaude(null, @"C:\old\agterm-agent-status.cmd");
        var updated = AgentHooksInstaller.MergeClaude(first.Contents, @"C:\new\agterm-agent-status.cmd");
        Assert.True(updated.Changed);
        Assert.DoesNotContain(@"C:\old\", updated.Contents, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(updated.Contents);
        Assert.Equal(1, json.RootElement.GetProperty("hooks").GetProperty("Stop").GetArrayLength());
    }

    [Fact]
    public void MergeDoesNotDuplicateAmbiguousExistingWrapper()
    {
        var original = AgentHooksInstaller.MergeClaude(null, @"C:\old\agterm-agent-status.cmd").Contents;
        var altered = original.Replace("\"agterm-win\"", "\"unknown\"", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() =>
            AgentHooksInstaller.MergeClaude(altered, @"C:\new\agterm-agent-status.cmd"));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("""{"hooks":42}""")]
    [InlineData("""{"hooks":null}""")]
    [InlineData("""{"hooks":{"Stop":"wrong"}}""")]
    [InlineData("""{"hooks":{"Stop":null}}""")]
    [InlineData("""{"hooks":{"Stop":[null]}}""")]
    [InlineData("""{"hooks":{"Stop":[{"hooks":"wrong"}]}}""")]
    public void MergeRefusesInvalidExistingSettings(string existing) =>
        Assert.Throws<InvalidDataException>(() => AgentHooksInstaller.MergeClaude(existing, @"C:\hook.cmd"));

    [Fact]
    public void InstallMakesBackupAndPreservesForeignPiExtension()
    {
        Directory.CreateDirectory(Path.Join(_home, ".claude"));
        Directory.CreateDirectory(Path.Join(_home, ".pi", "agent", "extensions"));
        var settings = Path.Join(_home, ".claude", "settings.json");
        File.WriteAllText(settings, """{"theme":"dark"}""");
        var pi = Path.Join(_home, ".pi", "agent", "extensions", "agterm-status.ts");
        File.WriteAllText(pi, "user extension");

        var outcome = Install(_home, "rem agterm-win-status-hook\n",
            "// agterm-win-pi-status-extension\n", Path.Join(_home, "agtermctl.exe"));

        Assert.True(outcome.IsWarning);
        Assert.Contains("Pi", outcome.Message);
        Assert.Equal("user extension", File.ReadAllText(pi));
        Assert.Equal("""{"theme":"dark"}""", File.ReadAllText(settings + ".bak"));
        using var json = JsonDocument.Parse(File.ReadAllText(settings));
        Assert.Equal("dark", json.RootElement.GetProperty("theme").GetString());
    }

    [Fact]
    public void RerunIsIdempotentAndMalformedSettingsStayUntouched()
    {
        var tool = Path.Join(_home, "agtermctl.exe");
        var first = Install(_home, "rem agterm-win-status-hook\n",
            "// agterm-win-pi-status-extension\n", tool);
        Assert.False(first.IsWarning);
        var settings = Path.Join(_home, ".claude", "settings.json");
        var bytes = File.ReadAllBytes(settings);
        var second = Install(_home, "rem agterm-win-status-hook\n",
            "// agterm-win-pi-status-extension\n", tool);
        Assert.False(second.IsWarning);
        Assert.Equal(bytes, File.ReadAllBytes(settings));
        Assert.False(File.Exists(settings + ".bak"));
        File.WriteAllText(settings, "{broken");
        var invalid = Install(_home, "rem agterm-win-status-hook\n",
            "// agterm-win-pi-status-extension\n", tool);
        Assert.True(invalid.IsWarning);
        Assert.Equal("{broken", File.ReadAllText(settings));
    }

    [Fact]
    public void ExistingSettingsWithInvalidUtf8AreNeverRewritten()
    {
        Directory.CreateDirectory(Path.Join(_home, ".claude"));
        var settings = Path.Join(_home, ".claude", "settings.json");
        var bytes = new byte[] { (byte)'{', (byte)'"', (byte)'x', (byte)'"', (byte)':',
            (byte)'"', 0xff, (byte)'"', (byte)'}' };
        File.WriteAllBytes(settings, bytes);

        var outcome = Install(_home, "rem agterm-win-status-hook\n",
            "// agterm-win-pi-status-extension\n", Path.Join(_home, "agtermctl.exe"));

        Assert.True(outcome.IsWarning);
        Assert.Equal(bytes, File.ReadAllBytes(settings));
        Assert.False(File.Exists(settings + ".bak"));
    }

    [Fact]
    public void PiInstallsOnlyInExistingAgentDirectoryAndRefreshesManagedExtension()
    {
        Directory.CreateDirectory(Path.Join(_home, ".pi", "agent"));
        var tool = Path.Join(_home, "agtermctl.exe");
        var first = Install(_home, "rem agterm-win-status-hook\n",
            "// agterm-win-pi-status-extension\nconst wrapper = __AGTERM_STATUS_WRAPPER__;\npi.exec(\"cmd.exe\");\n", tool);
        var pi = Path.Join(_home, ".pi", "agent", "extensions", AgentHooksInstaller.PiName);
        Assert.False(first.IsWarning);
        Assert.Contains("agterm-agent-status.cmd", File.ReadAllText(pi), StringComparison.Ordinal);
        Assert.Contains("pi.exec(\"cmd.exe\"", File.ReadAllText(pi), StringComparison.Ordinal);
        var second = Install(_home, "rem agterm-win-status-hook\n",
            "// agterm-win-pi-status-extension\nconst wrapper = __AGTERM_STATUS_WRAPPER__;\npi.exec(\"cmd.exe\");\n", tool);
        Assert.False(second.IsWarning);
        Assert.Contains("agterm-agent-status.cmd", File.ReadAllText(pi), StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingForeignStatusScriptPreventsClaimingCompleteInstall()
    {
        var directory = Path.Join(_home, ".config", "agterm", "agent-status");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Join(directory, AgentHooksInstaller.WrapperName), "user script");

        var outcome = Install(_home, "rem agterm-win-status-hook\n",
            "// agterm-win-pi-status-extension\n", Path.Join(_home, "agtermctl.exe"));

        Assert.Equal("Install Failed", outcome.Title);
        Assert.Contains("not an agterm-win file", outcome.Message);
        Assert.DoesNotContain("was not installed", outcome.Message);
        Assert.False(Directory.Exists(Path.Join(_home, ".claude")));
    }

    [Fact]
    public void ChangedSettingsDoNotOverwritePriorBackup()
    {
        var tool = Path.Join(_home, "agtermctl.exe");
        var cmd = "rem agterm-win-status-hook\n";
        var pi = "// agterm-win-pi-status-extension\n";
        Directory.CreateDirectory(Path.Join(_home, ".claude"));
        var settings = Path.Join(_home, ".claude", "settings.json");
        File.WriteAllText(settings, """{"theme":"dark"}""");
        File.WriteAllText(settings + ".bak", "older backup");
        Install(_home, cmd, pi, tool);
        Assert.Equal("older backup", File.ReadAllText(settings + ".bak"));
        Assert.True(File.Exists(settings + ".bak.1"));
        Assert.Equal("""{"theme":"dark"}""", File.ReadAllText(settings + ".bak.1"));
    }

    [Fact]
    public void CmdWrapperReportsPaneSpecificStatusAndIsSilentOutsideAgterm()
    {
        var template = File.ReadAllText(Path.GetFullPath(
            Path.Join(AppContext.BaseDirectory, "..", "..", "..", "..",
                "src", "AgTerm.Xterm", "AgentStatus", AgentHooksInstaller.WrapperName)));
        var tool = Path.Join(_home, "fake tool.cmd");
        var capture = Path.Join(_home, "capture.txt");
        File.WriteAllText(tool, "@echo off\r\necho %*>> \"%AGTERM_CAPTURE%\"\r\nexit /b 9\r\n");
        Install(_home, template, "// agterm-win-pi-status-extension\n", tool);
        var wrapper = Path.Join(_home, ".config", "agterm", "agent-status", AgentHooksInstaller.WrapperName);

        var outside = InvokeStatus(wrapper, capture, inside: false);
        Assert.Equal((0, "", ""), outside);
        Assert.False(File.Exists(capture));

        var inside = InvokeStatus(wrapper, capture, inside: true);
        Assert.Equal((0, "", ""), inside);
        var line = File.ReadAllText(capture);
        Assert.Contains("session status \"active\" --target \"ABC\"", line);
        Assert.Contains("--socket \"pipe name\"", line);
        Assert.Contains("--pane \"right\"", line);
        Assert.Contains("--pane-id \"token-1\"", line);
        Assert.Contains("--blink", line);
    }

    private static (int Code, string Output, string Error) InvokeStatus(string wrapper, string capture, bool inside,
        string? temporary = null)
    {
        var process = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "/d", "/v:on", "/c", "call", wrapper,
            "active", "--blink", "agterm-win" })
            process.ArgumentList.Add(arg);
        process.Environment["AGTERM_CAPTURE"] = capture;
        process.Environment["AGTERM_SESSION_ID"] = inside ? "ABC" : "";
        process.Environment["AGTERM_SOCKET"] = "pipe name";
        process.Environment["AGTERM_PANE"] = "right";
        process.Environment["AGTERM_PANE_ID"] = "token-1";
        process.Environment.Remove("AGTERMCTL");
        if (temporary is not null) process.Environment["TEMP"] = temporary;
        using var child = Process.Start(process)!;
        var output = child.StandardOutput.ReadToEnd();
        var error = child.StandardError.ReadToEnd();
        child.WaitForExit();
        return (child.ExitCode, output, error);
    }

    [Theory]
    [InlineData("UserPromptSubmit", "active", "--blink")]
    [InlineData("PostToolUse", "active", "--blink")]
    [InlineData("Stop", "completed", "--auto-reset")]
    [InlineData("PermissionRequest", "blocked", "")]
    [InlineData("Notification", "blocked", "")]
    public void EveryClaudeHookForwardsOnlyItsStatusArguments(string eventName, string state, string flag)
    {
        var template = File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "..", "..", "..", "..",
            "src", "AgTerm.Xterm", "AgentStatus", AgentHooksInstaller.WrapperName)));
        var tool = Path.Join(_home, "fake tool.cmd");
        var capture = Path.Join(_home, "capture.txt");
        File.WriteAllText(tool, "@echo off\r\necho %*>> \"%AGTERM_CAPTURE%\"\r\nexit /b 9\r\n");
        Install(_home, template,
            "// agterm-win-pi-status-extension\n", tool);
        var settings = File.ReadAllText(Path.Join(_home, ".claude", "settings.json"));
        using var json = JsonDocument.Parse(settings);
        var handler = json.RootElement.GetProperty("hooks").GetProperty(eventName)[0].GetProperty("hooks")[0];
        var process = new ProcessStartInfo(handler.GetProperty("command").GetString()!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in handler.GetProperty("args").EnumerateArray())
            process.ArgumentList.Add(arg.GetString()!);
        process.Environment["AGTERM_CAPTURE"] = capture;
        process.Environment["AGTERM_SESSION_ID"] = "ABC";
        process.Environment["AGTERM_SOCKET"] = "pipe name";
        process.Environment["AGTERM_PANE"] = "right";
        process.Environment["AGTERM_PANE_ID"] = "token-1";
        using var child = Process.Start(process)!;
        Assert.Equal("", child.StandardOutput.ReadToEnd());
        Assert.Equal("", child.StandardError.ReadToEnd());
        child.WaitForExit();
        Assert.Equal(0, child.ExitCode);

        var expected = $"session status \"{state}\" --target \"ABC\" --socket \"pipe name\" --pane \"right\" --pane-id \"token-1\"";
        if (flag.Length > 0) expected += " " + flag;
        Assert.Equal(expected, File.ReadAllText(capture).Trim());
    }

    private const string WrapperTemplate = "rem agterm-win-status-hook\n";
    private const string PiTemplate = "// agterm-win-pi-status-extension\n";

    private static InstallOutcome Install(string home, string wrapper, string pi, string tool, string? copilotHome = null) =>
        AgentHooksInstaller.Run(home, wrapper, pi, tool, File.ReadAllText(Path.GetFullPath(
            Path.Join(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "AgTerm.Xterm", "AgentStatus",
                AgentHooksInstaller.CopilotLifecycleName))), copilotHome);

    private string CopilotHooksFile => Path.Join(_home, ".copilot", "hooks", AgentHooksInstaller.CopilotHooksName);

    [Fact]
    public void CopilotHooksHandleErrorsAndSessionLifecycle()
    {
        using var json = JsonDocument.Parse(AgentHooksInstaller.BuildCopilot(
            @"C:\a b\agterm-agent-status.cmd"));
        var hooks = json.RootElement.GetProperty("hooks");
        foreach (var eventName in new[] { "sessionStart", "errorOccurred", "sessionEnd" })
        {
            Assert.True(hooks.TryGetProperty(eventName, out var entries), $"Missing {eventName} hook");
            var handler = entries[0];
            Assert.Equal("powershell.exe", handler.GetProperty("exec").GetString());
            var args = handler.GetProperty("args").EnumerateArray().Select(arg => arg.GetString()).ToArray();
            Assert.Contains(@"C:\a b\agterm-copilot-lifecycle.ps1", args);
            Assert.Contains(eventName, args);
        }
    }

    [Fact]
    public void FailedStatusDeliveryIsLoggedWithoutInterruptingTheAgent()
    {
        var template = File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "..", "..", "..", "..",
            "src", "AgTerm.Xterm", "AgentStatus", AgentHooksInstaller.WrapperName)));
        var tool = Path.Join(_home, "fake tool.cmd");
        File.WriteAllText(tool, "@echo off\r\necho control pipe unavailable 1>&2\r\nexit /b 9\r\n");
        Install(_home, template, PiTemplate, tool);
        var directory = Path.Join(_home, ".config", "agterm", "agent-status");

        Assert.Equal((0, "", ""), InvokeStatus(Path.Join(directory, AgentHooksInstaller.WrapperName),
            Path.Join(_home, "capture.txt"), inside: true));

        var log = Path.Join(directory, "agent-status.log");
        Assert.True(File.Exists(log), "Failed status delivery produced no diagnostic log");
        Assert.Contains("control pipe unavailable", File.ReadAllText(log));
        Assert.Contains("exit code 9", File.ReadAllText(log));
    }

    [Fact]
    public void AnUnwritableDiagnosticLogDoesNotPreventStatusDelivery()
    {
        var template = File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "..", "..", "..", "..",
            "src", "AgTerm.Xterm", "AgentStatus", AgentHooksInstaller.WrapperName)));
        var tool = Path.Join(_home, "fake tool.cmd");
        var capture = Path.Join(_home, "capture.txt");
        File.WriteAllText(tool, "@echo off\r\necho %*>> \"%AGTERM_CAPTURE%\"\r\necho pipe unavailable 1>&2\r\nexit /b 9\r\n");
        Install(_home, template, PiTemplate, tool);
        var directory = Path.Join(_home, ".config", "agterm", "agent-status");
        Directory.CreateDirectory(Path.Join(directory, "agent-status.log"));

        var result = InvokeStatus(Path.Join(directory, AgentHooksInstaller.WrapperName), capture, inside: true);

        Assert.Equal(0, result.Code);
        Assert.True(File.Exists(capture));
        Assert.Contains("Could not write agent-status.log", result.Error);
    }

    [Fact]
    public void MissingStatusToolIsLoggedAndTheAgentStillContinues()
    {
        var template = File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "..", "..", "..", "..",
            "src", "AgTerm.Xterm", "AgentStatus", AgentHooksInstaller.WrapperName)));
        var tool = Path.Join(_home, "missing tool.cmd");
        File.WriteAllText(tool, "@echo off\r\n");
        Install(_home, template, PiTemplate, tool);
        File.Delete(tool);
        var directory = Path.Join(_home, ".config", "agterm", "agent-status");

        Assert.Equal((0, "", ""), InvokeStatus(Path.Join(directory, AgentHooksInstaller.WrapperName),
            Path.Join(_home, "capture.txt"), inside: true));
        var log = File.ReadAllText(Path.Join(directory, "agent-status.log"));
        Assert.Contains("exit code", log);
        Assert.Contains("not recognized", log);
    }

    [Fact]
    public void SuccessfulStatusDeliveryDoesNotCreateADiagnosticLog()
    {
        var template = File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "..", "..", "..", "..",
            "src", "AgTerm.Xterm", "AgentStatus", AgentHooksInstaller.WrapperName)));
        var tool = Path.Join(_home, "fake tool.cmd");
        File.WriteAllText(tool, "@echo off\r\necho ignored output\r\necho ignored stderr 1>&2\r\nexit /b 0\r\n");
        Install(_home, template, PiTemplate, tool);
        var directory = Path.Join(_home, ".config", "agterm", "agent-status");

        Assert.Equal((0, "", ""), InvokeStatus(Path.Join(directory, AgentHooksInstaller.WrapperName),
            Path.Join(_home, "capture.txt"), inside: false));
        Assert.Equal((0, "", ""), InvokeStatus(Path.Join(directory, AgentHooksInstaller.WrapperName),
            Path.Join(_home, "capture.txt"), inside: true));
        Assert.False(File.Exists(Path.Join(directory, "agent-status.log")));
        Directory.CreateDirectory(Path.Join(directory, "agent-status.log"));
        Assert.Equal((0, "", ""), InvokeStatus(Path.Join(directory, AgentHooksInstaller.WrapperName),
            Path.Join(_home, "capture.txt"), inside: true));
    }

    [Fact]
    public void AnUnwritableTemporaryDirectoryDoesNotPreventStatusDelivery()
    {
        var template = File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "..", "..", "..", "..",
            "src", "AgTerm.Xterm", "AgentStatus", AgentHooksInstaller.WrapperName)));
        var tool = Path.Join(_home, "fake tool.cmd");
        var capture = Path.Join(_home, "capture.txt");
        File.WriteAllText(tool, "@echo off\r\necho %*>> \"%AGTERM_CAPTURE%\"\r\nexit /b 0\r\n");
        Install(_home, template, PiTemplate, tool);
        var directory = Path.Join(_home, ".config", "agterm", "agent-status");
        var result = InvokeStatus(Path.Join(directory, AgentHooksInstaller.WrapperName), capture, inside: true,
            temporary: Path.Join(_home, "missing temp"));
        Assert.Equal(0, result.Code);
        Assert.True(File.Exists(capture));
        Assert.Contains("Could not capture agent-status stderr", result.Error);
    }

    [Fact]
    public void CopilotHooksGoIntoAnOwnedFileWhenCopilotIsInstalled()
    {
        Directory.CreateDirectory(Path.Join(_home, ".copilot"));

        var outcome = Install(_home, WrapperTemplate, PiTemplate, Path.Join(_home, "agtermctl.exe"));

        Assert.False(outcome.IsWarning);
        Assert.Contains($"Copilot CLI: {CopilotHooksFile}", outcome.Message);
        using var json = JsonDocument.Parse(File.ReadAllText(CopilotHooksFile));
        Assert.Equal(1, json.RootElement.GetProperty("version").GetInt32());
        var hooks = json.RootElement.GetProperty("hooks");
        Assert.Equal(
            new[] { "sessionStart", "userPromptSubmitted", "postToolUse", "postToolUseFailure", "agentStop", "notification",
                "errorOccurred", "sessionEnd" },
            hooks.EnumerateObject().Select(property => property.Name));
        Assert.Equal(new[] { "permission_prompt", "elicitation_dialog" },
            hooks.GetProperty("notification").EnumerateArray().Select(entry => entry.GetProperty("matcher").GetString()));
        var wrapper = Path.Join(_home, ".config", "agterm", "agent-status", AgentHooksInstaller.WrapperName);
        foreach (var property in hooks.EnumerateObject())
            foreach (var handler in property.Value.EnumerateArray())
            {
                Assert.Equal("command", handler.GetProperty("type").GetString());
                var args = handler.GetProperty("args").EnumerateArray().Select(arg => arg.GetString()).ToArray();
                if (property.Name is "postToolUse" or "postToolUseFailure")
                {
                    Assert.Equal("cmd.exe", handler.GetProperty("exec").GetString());
                    Assert.Equal(new[] { "/d", "/v:on", "/c", "call", wrapper, "active", "--blink", "agterm-win" }, args);
                }
                else
                {
                    Assert.Equal("powershell.exe", handler.GetProperty("exec").GetString());
                    Assert.Contains(Path.Join(Path.GetDirectoryName(wrapper), AgentHooksInstaller.CopilotLifecycleName), args);
                }
            }
    }

    [Fact]
    public void CopilotToolHooksFromTheLifecycleOnlyLayoutUpgradeWithABackup()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CopilotHooksFile)!);
        const string previous = """
            {"version":1,"hooks":{"postToolUse":[{"type":"command","exec":"powershell.exe","args":["-NoLogo","-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File","C:\\old\\agterm-copilot-lifecycle.ps1","postToolUse","agterm-win"],"timeoutSec":10}]}}
            """;
        File.WriteAllText(CopilotHooksFile, previous);
        var outcome = Install(_home, WrapperTemplate, PiTemplate, Path.Join(_home, "agtermctl.exe"));
        Assert.False(outcome.IsWarning);
        Assert.Equal(previous, File.ReadAllText(CopilotHooksFile + ".bak"));
        using var json = JsonDocument.Parse(File.ReadAllText(CopilotHooksFile));
        Assert.Equal("cmd.exe", json.RootElement.GetProperty("hooks").GetProperty("postToolUse")[0].GetProperty("exec").GetString());
    }

    [Fact]
    public void CopilotHooksAreSkippedWithoutWarningWhenCopilotIsMissing()
    {
        var outcome = Install(_home, WrapperTemplate, PiTemplate, Path.Join(_home, "agtermctl.exe"));

        Assert.False(outcome.IsWarning);
        Assert.Contains("Copilot CLI: not installed (no .copilot directory)", outcome.Message);
        Assert.False(Directory.Exists(Path.Join(_home, ".copilot")));
    }

    [Fact]
    public void CopilotRerunIsIdempotentAndAChangedOwnedFileIsRefreshedWithBackup()
    {
        Directory.CreateDirectory(Path.Join(_home, ".copilot"));
        var tool = Path.Join(_home, "agtermctl.exe");
        Install(_home, WrapperTemplate, PiTemplate, tool);
        var bytes = File.ReadAllBytes(CopilotHooksFile);

        var second = Install(_home, WrapperTemplate, PiTemplate, tool);
        Assert.False(second.IsWarning);
        Assert.Equal(bytes, File.ReadAllBytes(CopilotHooksFile));
        Assert.False(File.Exists(CopilotHooksFile + ".bak"));

        var edited = File.ReadAllText(CopilotHooksFile).Replace("\"timeoutSec\": 10", "\"timeoutSec\": 99", StringComparison.Ordinal);
        File.WriteAllText(CopilotHooksFile, edited);
        var third = Install(_home, WrapperTemplate, PiTemplate, tool);
        Assert.False(third.IsWarning);
        Assert.Equal(bytes, File.ReadAllBytes(CopilotHooksFile));
        Assert.Equal(edited, File.ReadAllText(CopilotHooksFile + ".bak"));
    }

    [Fact]
    public void LegacyCopilotHooksUpgradeToTheLifecycleHandlerWithABackup()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CopilotHooksFile)!);
        const string legacy = """
            {"version":1,"hooks":{"agentStop":[{"type":"command","exec":"cmd.exe","args":["/d","/v:on","/c","call","C:\\old\\agterm-agent-status.cmd","completed","--auto-reset","agterm-win"]}]}}
            """;
        File.WriteAllText(CopilotHooksFile, legacy);
        var outcome = Install(_home, WrapperTemplate, PiTemplate, Path.Join(_home, "agtermctl.exe"));
        Assert.False(outcome.IsWarning);
        Assert.Equal(legacy, File.ReadAllText(CopilotHooksFile + ".bak"));
        Assert.Contains("errorOccurred", File.ReadAllText(CopilotHooksFile));
    }

    [Fact]
    public void CopilotHomeOverrideIsUsedAndForeignHooksStayUntouched()
    {
        var custom = Path.Join(_home, "custom copilot");
        Directory.CreateDirectory(Path.Join(custom, "hooks"));
        Directory.CreateDirectory(Path.Join(_home, ".copilot"));
        var file = Path.Join(custom, "hooks", AgentHooksInstaller.CopilotHooksName);
        File.WriteAllText(file, "foreign");
        var outcome = Install(_home, WrapperTemplate, PiTemplate, Path.Join(_home, "agtermctl.exe"), custom);
        Assert.True(outcome.IsWarning);
        Assert.Equal("foreign", File.ReadAllText(file));
        Assert.False(File.Exists(CopilotHooksFile));
    }

    [Fact]
    public void ForeignLifecycleScriptIsNotOverwrittenOrReferencedByNewHooks()
    {
        var scripts = Path.Join(_home, ".config", "agterm", "agent-status");
        Directory.CreateDirectory(scripts);
        var file = Path.Join(scripts, AgentHooksInstaller.CopilotLifecycleName);
        File.WriteAllText(file, "foreign");
        var outcome = Install(_home, WrapperTemplate, PiTemplate, Path.Join(_home, "agtermctl.exe"));
        Assert.True(outcome.IsWarning);
        Assert.Equal("foreign", File.ReadAllText(file));
        Assert.False(File.Exists(Path.Join(scripts, AgentHooksInstaller.WrapperName)));
        Assert.False(Directory.Exists(Path.Join(_home, ".claude")));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("""{"version":1,"hooks":{"agentStop":[{"type":"command","bash":"./mine.sh"}]}}""")]
    [InlineData("""{"version":1,"hooks":{"agentStop":[{"type":"command","exec":"cmd.exe","args":["/d","/v:on","/c","call","C:\\x\\agterm-agent-status.cmd","completed","mine"]}]}}""")]
    [InlineData("""{"version":1,"hooks":{"agentStop":"wrong"}}""")]
    public void ForeignOrInvalidCopilotHooksFileStaysUntouched(string existing)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CopilotHooksFile)!);
        File.WriteAllText(CopilotHooksFile, existing);

        var outcome = Install(_home, WrapperTemplate, PiTemplate, Path.Join(_home, "agtermctl.exe"));

        Assert.True(outcome.IsWarning);
        Assert.Contains("Copilot CLI:", outcome.Message);
        Assert.Equal(existing, File.ReadAllText(CopilotHooksFile));
        Assert.False(File.Exists(CopilotHooksFile + ".bak"));
    }

    [Theory]
    [InlineData("userPromptSubmitted", 0, "active", "--blink")]
    [InlineData("postToolUse", 0, "active", "--blink")]
    [InlineData("postToolUseFailure", 0, "active", "--blink")]
    [InlineData("agentStop", 0, "completed", "--auto-reset")]
    [InlineData("notification", 0, "blocked", "")]
    [InlineData("notification", 1, "blocked", "")]
    public void EveryCopilotHookForwardsOnlyItsStatusArguments(string eventName, int index, string state, string flag)
    {
        Directory.CreateDirectory(Path.Join(_home, ".copilot"));
        var template = File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "..", "..", "..", "..",
            "src", "AgTerm.Xterm", "AgentStatus", AgentHooksInstaller.WrapperName)));
        var tool = Path.Join(_home, "fake tool.cmd");
        var capture = Path.Join(_home, "capture.txt");
        File.WriteAllText(tool, "@echo off\r\necho %*>> \"%AGTERM_CAPTURE%\"\r\nexit /b 9\r\n");
        Install(_home, template, PiTemplate, tool);
        using var json = JsonDocument.Parse(File.ReadAllText(CopilotHooksFile));
        var handler = json.RootElement.GetProperty("hooks").GetProperty(eventName)[index];
        var process = new ProcessStartInfo(handler.GetProperty("exec").GetString()!)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in handler.GetProperty("args").EnumerateArray())
            process.ArgumentList.Add(arg.GetString()!);
        process.Environment["AGTERM_CAPTURE"] = capture;
        process.Environment["AGTERM_SESSION_ID"] = "ABC";
        process.Environment["AGTERM_SOCKET"] = "pipe name";
        process.Environment["AGTERM_PANE"] = "right";
        process.Environment["AGTERM_PANE_ID"] = "token-1";
        process.Environment.Remove("AGTERMCTL");
        using var child = Process.Start(process)!;
        child.StandardInput.Write("""{"sessionId":"parent","timestamp":1,"notification_type":"permission_prompt"}""");
        child.StandardInput.Close();
        Assert.Equal("", child.StandardOutput.ReadToEnd());
        Assert.Equal("", child.StandardError.ReadToEnd());
        child.WaitForExit();
        Assert.Equal(0, child.ExitCode);

        var expected = $"session status \"{state}\" --target \"ABC\" --socket \"pipe name\" --pane \"right\" --pane-id \"token-1\"";
        if (flag.Length > 0) expected += " " + flag;
        Assert.Equal(expected, File.ReadAllText(capture).Trim());
    }

    [Theory]
    [InlineData("a b", false)]
    [InlineData("a&b", true)]
    [InlineData("a%b", true)]
    [InlineData("a!b", true)]
    public void CmdInstallerAcceptsSpacesAndRefusesUnsafePaths(string folder, bool shouldFail)
    {
        var home = Path.Join(_home, folder);
        Directory.CreateDirectory(home);
        var template = File.ReadAllText(Path.GetFullPath(
            Path.Join(AppContext.BaseDirectory, "..", "..", "..", "..",
                "src", "AgTerm.Xterm", "AgentStatus", AgentHooksInstaller.WrapperName)));
        var tool = Path.Join(home, "fake tool.cmd");
        var capture = Path.Join(home, "capture.txt");
        File.WriteAllText(tool, "@echo off\r\necho %*>> \"%AGTERM_CAPTURE%\"\r\n");
        var outcome = Install(home, template,
            "// agterm-win-pi-status-extension\n", tool);
        if (shouldFail)
        {
            Assert.True(outcome.IsWarning);
            Assert.Contains("cannot be quoted safely", outcome.Message);
            Assert.False(Directory.Exists(Path.Join(home, ".claude")));
            return;
        }
        Assert.False(outcome.IsWarning);
        var wrapper = Path.Join(home, ".config", "agterm", "agent-status", AgentHooksInstaller.WrapperName);
        var result = InvokeStatus(wrapper, capture, inside: true);
        Assert.True(result.Code == 0, result.Error);
        Assert.Equal(("", ""), (result.Output, result.Error));
        Assert.Contains("session status \"active\"", File.ReadAllText(capture));
    }
}
