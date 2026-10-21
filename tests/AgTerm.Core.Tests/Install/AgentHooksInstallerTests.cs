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

        var outcome = AgentHooksInstaller.Run(_home, "rem agterm-win-status-hook\n",
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
        var first = AgentHooksInstaller.Run(_home, "rem agterm-win-status-hook\n",
            "// agterm-win-pi-status-extension\n", tool);
        Assert.False(first.IsWarning);
        var settings = Path.Join(_home, ".claude", "settings.json");
        var bytes = File.ReadAllBytes(settings);
        var second = AgentHooksInstaller.Run(_home, "rem agterm-win-status-hook\n",
            "// agterm-win-pi-status-extension\n", tool);
        Assert.False(second.IsWarning);
        Assert.Equal(bytes, File.ReadAllBytes(settings));
        Assert.False(File.Exists(settings + ".bak"));
        File.WriteAllText(settings, "{broken");
        var invalid = AgentHooksInstaller.Run(_home, "rem agterm-win-status-hook\n",
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

        var outcome = AgentHooksInstaller.Run(_home, "rem agterm-win-status-hook\n",
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
        var first = AgentHooksInstaller.Run(_home, "rem agterm-win-status-hook\n",
            "// agterm-win-pi-status-extension\nconst wrapper = __AGTERM_STATUS_WRAPPER__;\npi.exec(\"cmd.exe\");\n", tool);
        var pi = Path.Join(_home, ".pi", "agent", "extensions", AgentHooksInstaller.PiName);
        Assert.False(first.IsWarning);
        Assert.Contains("agterm-agent-status.cmd", File.ReadAllText(pi), StringComparison.Ordinal);
        Assert.Contains("pi.exec(\"cmd.exe\"", File.ReadAllText(pi), StringComparison.Ordinal);
        var second = AgentHooksInstaller.Run(_home, "rem agterm-win-status-hook\n",
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

        var outcome = AgentHooksInstaller.Run(_home, "rem agterm-win-status-hook\n",
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
        AgentHooksInstaller.Run(_home, cmd, pi, tool);
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
        AgentHooksInstaller.Run(_home, template, "// agterm-win-pi-status-extension\n", tool);
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

    private static (int Code, string Output, string Error) InvokeStatus(string wrapper, string capture, bool inside)
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
        AgentHooksInstaller.Run(_home, template,
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
        var outcome = AgentHooksInstaller.Run(home, template,
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
