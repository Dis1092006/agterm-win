using System.Diagnostics;
using System.Text.Json;
using AgTerm.Core.Install;

namespace AgTerm.Core.Tests.Install;

public sealed class AgentResumeInstallerTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), $"agterm-resume-{Guid.NewGuid():N}");
    private readonly string _home;
    private readonly string _tool;

    public AgentResumeInstallerTests()
    {
        _home = Path.Join(_root, "home");
        Directory.CreateDirectory(_home);
        _tool = Path.Join(_root, "agtermctl.exe");
        File.WriteAllText(_tool, "");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Template => File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory,
        "..", "..", "..", "..", "src", "AgTerm.Xterm", "AgentStatus", AgentResumeInstaller.WrapperName)));

    private string Wrapper => Path.Join(_home, ".config", "agterm", "agent-resume", AgentResumeInstaller.WrapperName);
    private string ClaudeSettings => Path.Join(_home, ".claude", "settings.json");
    private string CopilotHooks => Path.Join(_home, ".copilot", "hooks", AgentResumeInstaller.CopilotHooksName);
    private string CodexHooks => Path.Join(_home, ".codex", "hooks.json");
    private string CodexConfig => Path.Join(_home, ".codex", "config.toml");

    private const string CodexTrust = "Codex asks you to trust the new hooks the next time it starts. "
        + "If Codex's background server is running, stop it once with codex app-server daemon stop.";

    private const string CodexUserOn = "daemon_auto_start = true in Codex config.toml keeps Codex sessions in its "
        + "background server, so they are not resumed.";

    private AgentResumeReport Apply(bool enabled, string? template = null, string? tool = null) =>
        AgentResumeInstaller.Apply(new AgentResumePaths(_home), enabled, tool ?? _tool, template ?? Template);

    private void CreateAgents()
    {
        Directory.CreateDirectory(Path.Join(_home, ".claude"));
        Directory.CreateDirectory(Path.Join(_home, ".copilot"));
        Directory.CreateDirectory(Path.Join(_home, ".codex"));
    }

    private static string[] Args(JsonElement handler) =>
        handler.GetProperty("args").EnumerateArray().Select(arg => arg.GetString()!).ToArray();

    private static AgentResumeState StateOf(AgentResumeReport report, string name) =>
        report.Agents.Single(agent => agent.DisplayName == name).State;

    [Fact]
    public void TurningOnInstallsTheWrapperAndAHookPairForEveryAgentFound()
    {
        CreateAgents();

        var report = Apply(enabled: true);

        Assert.Null(report.Error);
        Assert.Equal(new[] { "Claude Code", "Copilot CLI", "Codex" }, report.Agents.Select(agent => agent.DisplayName));
        Assert.All(report.Agents, agent => Assert.Equal(AgentResumeState.On, agent.State));
        var wrapper = File.ReadAllText(Wrapper);
        Assert.Contains(AgentResumeInstaller.WrapperMarker, wrapper);
        Assert.Contains(_tool, wrapper);
        Assert.DoesNotContain("__AGTERMCTL_PATH__", wrapper);

        using (var claude = JsonDocument.Parse(File.ReadAllText(ClaudeSettings)))
        {
            var hooks = claude.RootElement.GetProperty("hooks");
            foreach (var (name, verb) in new[] { ("SessionStart", "start"), ("SessionEnd", "end") })
            {
                var group = Assert.Single(hooks.GetProperty(name).EnumerateArray());
                Assert.False(group.TryGetProperty("matcher", out _));
                var handler = Assert.Single(group.GetProperty("hooks").EnumerateArray());
                Assert.Equal("command", handler.GetProperty("type").GetString());
                Assert.Equal("cmd.exe", handler.GetProperty("command").GetString());
                Assert.Equal(new[] { "/d", "/c", "call", Wrapper, "claude", verb }, Args(handler));
                Assert.Equal(10, handler.GetProperty("timeout").GetInt32());
            }
        }

        using (var copilot = JsonDocument.Parse(File.ReadAllText(CopilotHooks)))
        {
            Assert.Equal(1, copilot.RootElement.GetProperty("version").GetInt32());
            var hooks = copilot.RootElement.GetProperty("hooks");
            Assert.Equal(new[] { "sessionStart", "sessionEnd" }, hooks.EnumerateObject().Select(property => property.Name));
            foreach (var (name, verb) in new[] { ("sessionStart", "start"), ("sessionEnd", "end") })
            {
                var handler = Assert.Single(hooks.GetProperty(name).EnumerateArray());
                Assert.Equal("command", handler.GetProperty("type").GetString());
                Assert.Equal("cmd.exe", handler.GetProperty("exec").GetString());
                Assert.Equal(new[] { "/d", "/c", "call", Wrapper, "copilot", verb }, Args(handler));
                Assert.Equal(10, handler.GetProperty("timeoutSec").GetInt32());
            }
        }

        using (var codex = JsonDocument.Parse(File.ReadAllText(CodexHooks)))
        {
            var hooks = codex.RootElement.GetProperty("hooks");
            foreach (var (name, verb) in new[] { ("SessionStart", "start"), ("SessionEnd", "end") })
            {
                var group = Assert.Single(hooks.GetProperty(name).EnumerateArray());
                var handler = Assert.Single(group.GetProperty("hooks").EnumerateArray());
                Assert.Equal("command", handler.GetProperty("type").GetString());
                // Codex quotes the command for cmd.exe /c with backslash escapes, so it carries no quotes.
                Assert.Equal($"{Wrapper} codex {verb}", handler.GetProperty("command").GetString());
                Assert.Equal(10, handler.GetProperty("timeout").GetInt32());
            }
        }
    }

    [Fact]
    public void ASecondRunChangesNothing()
    {
        CreateAgents();
        Apply(enabled: true);
        var files = new[] { Wrapper, ClaudeSettings, CopilotHooks, CodexHooks };
        var before = files.Select(File.ReadAllBytes).ToArray();
        var times = files.Select(File.GetLastWriteTimeUtc).ToArray();

        var report = Apply(enabled: true);

        Assert.All(report.Agents, agent => Assert.Equal(AgentResumeState.On, agent.State));
        Assert.Equal(before, files.Select(File.ReadAllBytes).ToArray());
        Assert.Equal(times, files.Select(File.GetLastWriteTimeUtc).ToArray());
        Assert.Empty(Directory.GetFiles(_home, "*.bak*", SearchOption.AllDirectories));
    }

    [Fact]
    public void OtherSettingsAndHooksAreKeptAndAChangedFileIsBackedUp()
    {
        CreateAgents();
        const string claude = """{"theme":"dark","hooks":{"SessionStart":[{"matcher":"startup","hooks":[{"type":"command","command":"my-hook"}]}],"Stop":[{"hooks":[{"type":"command","command":"other"}]}]}}""";
        const string codex = """{"hooks":{"SessionStart":[{"hooks":[{"type":"command","command":"mine"}]}]},"extra":1}""";
        File.WriteAllText(ClaudeSettings, claude);
        File.WriteAllText(CodexHooks, codex);

        Apply(enabled: true);

        Assert.Equal(claude, File.ReadAllText(ClaudeSettings + ".bak"));
        Assert.Equal(codex, File.ReadAllText(CodexHooks + ".bak"));
        using var claudeJson = JsonDocument.Parse(File.ReadAllText(ClaudeSettings));
        Assert.Equal("dark", claudeJson.RootElement.GetProperty("theme").GetString());
        var start = claudeJson.RootElement.GetProperty("hooks").GetProperty("SessionStart");
        Assert.Equal(2, start.GetArrayLength());
        Assert.Equal("my-hook", start[0].GetProperty("hooks")[0].GetProperty("command").GetString());
        Assert.Equal("other", claudeJson.RootElement.GetProperty("hooks").GetProperty("Stop")[0]
            .GetProperty("hooks")[0].GetProperty("command").GetString());
        using var codexJson = JsonDocument.Parse(File.ReadAllText(CodexHooks));
        Assert.Equal(1, codexJson.RootElement.GetProperty("extra").GetInt32());
        var codexStart = codexJson.RootElement.GetProperty("hooks").GetProperty("SessionStart");
        Assert.Equal(2, codexStart.GetArrayLength());
        Assert.Equal("mine", codexStart[0].GetProperty("hooks")[0].GetProperty("command").GetString());
    }

    [Fact]
    public void AnAgentWithoutAConfigurationDirectoryIsNotFoundAndNothingIsCreatedForIt()
    {
        Directory.CreateDirectory(Path.Join(_home, ".claude"));

        var report = Apply(enabled: true);

        Assert.Null(report.Error);
        Assert.Equal(AgentResumeState.On, StateOf(report, "Claude Code"));
        Assert.Equal(AgentResumeState.NotFound, StateOf(report, "Copilot CLI"));
        Assert.Equal(AgentResumeState.NotFound, StateOf(report, "Codex"));
        Assert.False(Directory.Exists(Path.Join(_home, ".copilot")));
        Assert.False(Directory.Exists(Path.Join(_home, ".codex")));
    }

    [Fact]
    public void TurningOffRemovesOnlyOurEntriesAndFiles()
    {
        CreateAgents();
        File.WriteAllText(ClaudeSettings, """{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"my-hook"}]}]}}""");
        File.WriteAllText(CodexHooks, """{"hooks":{"SessionStart":[{"hooks":[{"type":"command","command":"mine"}]}]}}""");
        Directory.CreateDirectory(Path.Join(_home, ".copilot", "hooks"));
        File.WriteAllText(Path.Join(_home, ".copilot", "hooks", "mine.json"), "{}");
        Apply(enabled: true);

        var report = Apply(enabled: false);

        Assert.Null(report.Error);
        Assert.All(report.Agents, agent => Assert.Equal(AgentResumeState.Off, agent.State));
        using var claudeJson = JsonDocument.Parse(File.ReadAllText(ClaudeSettings));
        var hooks = claudeJson.RootElement.GetProperty("hooks");
        Assert.Equal(new[] { "Stop" }, hooks.EnumerateObject().Select(property => property.Name));
        Assert.False(File.Exists(CopilotHooks));
        Assert.True(File.Exists(Path.Join(_home, ".copilot", "hooks", "mine.json")));
        using var codexJson = JsonDocument.Parse(File.ReadAllText(CodexHooks));
        var codexHooks = codexJson.RootElement.GetProperty("hooks");
        Assert.Equal(new[] { "SessionStart" }, codexHooks.EnumerateObject().Select(property => property.Name));
        Assert.Equal("mine", codexHooks.GetProperty("SessionStart")[0].GetProperty("hooks")[0]
            .GetProperty("command").GetString());
        Assert.False(File.Exists(Wrapper));
    }

    [Fact]
    public void TurningOffAfterAFreshInstallLeavesNoHooksKeyBehind()
    {
        CreateAgents();
        File.WriteAllText(ClaudeSettings, """{"theme":"dark"}""");
        Apply(enabled: true);

        Apply(enabled: false);

        using var claude = JsonDocument.Parse(File.ReadAllText(ClaudeSettings));
        Assert.Equal(new[] { "theme" }, claude.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Wrapper)));
    }

    [Fact]
    public void TurningOffWithNothingInstalledTouchesNoFile()
    {
        CreateAgents();
        File.WriteAllText(ClaudeSettings, """{"theme":"dark"}""");

        var report = Apply(enabled: false);

        Assert.Null(report.Error);
        Assert.Equal("""{"theme":"dark"}""", File.ReadAllText(ClaudeSettings));
        Assert.False(File.Exists(CodexHooks));
        Assert.Empty(Directory.GetFiles(_home, "*.bak*", SearchOption.AllDirectories));
    }

    [Fact]
    public void TheClaudeRecipeIsReplaced()
    {
        CreateAgents();
        var restore = Path.Join(_home, ".config", "agterm", "claude-restore");
        Directory.CreateDirectory(restore);
        File.WriteAllText(Path.Join(restore, "claude-resume-hook.ps1"), "");
        File.WriteAllText(Path.Join(restore, "pin-claude-resume.ps1"), "");
        var hook = JsonSerializer.Serialize(Path.Join(restore, "claude-resume-hook.ps1"));
        var pin = JsonSerializer.Serialize(Path.Join(restore, "pin-claude-resume.ps1"));
        File.WriteAllText(ClaudeSettings, $$$"""
            {"hooks":{
              "SessionStart":[{"hooks":[{"type":"command","command":"powershell.exe","args":["-NoProfile","-File",{{{hook}}}],"timeout":10}]},
                              {"hooks":[{"type":"command","command":"keep"},{"type":"command","command":"powershell.exe","args":["-File",{{{pin}}}]}]}],
              "SessionEnd":[{"hooks":[{"type":"command","command":"powershell.exe","args":["-File",{{{hook}}}]}]}]}}
            """);

        var report = Apply(enabled: true);

        Assert.Equal(AgentResumeState.On, StateOf(report, "Claude Code"));
        using var json = JsonDocument.Parse(File.ReadAllText(ClaudeSettings));
        var hooks = json.RootElement.GetProperty("hooks");
        var start = hooks.GetProperty("SessionStart");
        Assert.Equal(2, start.GetArrayLength());
        Assert.Equal("keep", Assert.Single(start[0].GetProperty("hooks").EnumerateArray()).GetProperty("command").GetString());
        Assert.Equal("claude", Args(start[1].GetProperty("hooks")[0])[4]);
        Assert.Equal("end", Args(Assert.Single(hooks.GetProperty("SessionEnd").EnumerateArray()).GetProperty("hooks")[0])[5]);
        Assert.DoesNotContain(".ps1", File.ReadAllText(ClaudeSettings), StringComparison.Ordinal);
        Assert.False(Directory.Exists(restore));
    }

    [Fact]
    public void TheCopilotRecipeIsReplaced()
    {
        CreateAgents();
        var restore = Path.Join(_home, ".config", "agterm", "copilot-restore");
        Directory.CreateDirectory(restore);
        File.WriteAllText(Path.Join(restore, "copilot-resume-hook.ps1"), "");
        var recipe = Path.Join(_home, ".copilot", "hooks", "copilot-session-resume.json");
        Directory.CreateDirectory(Path.GetDirectoryName(recipe)!);
        File.WriteAllText(recipe, $$$"""
            {"version":1,"hooks":{"sessionStart":[{"type":"command","exec":"powershell.exe","args":["-File",{{{JsonSerializer.Serialize(Path.Join(restore, "copilot-resume-hook.ps1"))}}},"sessionStart"],"timeoutSec":10}]}}
            """);

        Apply(enabled: true);

        Assert.False(File.Exists(recipe));
        Assert.False(Directory.Exists(restore));
        Assert.True(File.Exists(CopilotHooks));
    }

    [Fact]
    public void AFileWithTheRecipeNameThatIsNotTheRecipeIsKept()
    {
        CreateAgents();
        var recipe = Path.Join(_home, ".copilot", "hooks", "copilot-session-resume.json");
        Directory.CreateDirectory(Path.GetDirectoryName(recipe)!);
        File.WriteAllText(recipe, """{"version":1,"hooks":{}}""");
        var restore = Path.Join(_home, ".config", "agterm", "claude-restore");
        Directory.CreateDirectory(restore);
        File.WriteAllText(Path.Join(restore, "notes.txt"), "mine");

        Apply(enabled: true);

        Assert.True(File.Exists(recipe));
        Assert.True(File.Exists(Path.Join(restore, "notes.txt")));
    }

    [Fact]
    public void TurningOffLeavesTheRecipesAlone()
    {
        CreateAgents();
        var restore = Path.Join(_home, ".config", "agterm", "copilot-restore");
        Directory.CreateDirectory(restore);
        File.WriteAllText(Path.Join(restore, "copilot-resume-hook.ps1"), "");

        Apply(enabled: false);

        Assert.True(File.Exists(Path.Join(restore, "copilot-resume-hook.ps1")));
    }

    [Fact]
    public void CodexFailsWhenTheWrapperPathHasASpace()
    {
        var home = Path.Join(_root, "home with space");
        foreach (var agent in new[] { ".claude", ".copilot", ".codex" }) Directory.CreateDirectory(Path.Join(home, agent));

        var report = AgentResumeInstaller.Apply(new AgentResumePaths(home), enabled: true, _tool, Template);

        Assert.Equal(AgentResumeState.On, StateOf(report, "Claude Code"));
        Assert.Equal(AgentResumeState.On, StateOf(report, "Copilot CLI"));
        var codex = report.Agents.Single(agent => agent.DisplayName == "Codex");
        Assert.Equal(AgentResumeState.Failed, codex.State);
        Assert.Contains("space", codex.Reason);
        Assert.False(File.Exists(Path.Join(home, ".codex", "hooks.json")));
    }

    [Fact]
    public void AForeignCopilotFileWithOurNameIsLeftAlone()
    {
        CreateAgents();
        Directory.CreateDirectory(Path.GetDirectoryName(CopilotHooks)!);
        File.WriteAllText(CopilotHooks, """{"version":1,"hooks":{"sessionStart":[{"type":"command","exec":"mine"}]}}""");

        var report = Apply(enabled: true);
        var off = Apply(enabled: false);

        Assert.Equal(AgentResumeState.Failed, StateOf(report, "Copilot CLI"));
        Assert.Equal(AgentResumeState.Failed, StateOf(off, "Copilot CLI"));
        Assert.Contains("mine", File.ReadAllText(CopilotHooks));
        Assert.Equal(AgentResumeState.On, StateOf(report, "Claude Code"));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("""{"hooks":42}""")]
    [InlineData("""{"hooks":{"SessionStart":"wrong"}}""")]
    [InlineData("""{"hooks":{"SessionStart":[null]}}""")]
    [InlineData("""{"hooks":{"SessionStart":[{"hooks":"wrong"}]}}""")]
    public void InvalidSettingsFailForThatAgentAndStayUntouched(string contents)
    {
        CreateAgents();
        File.WriteAllText(ClaudeSettings, contents);
        File.WriteAllText(CodexHooks, contents);

        var report = Apply(enabled: true);

        Assert.Equal(AgentResumeState.Failed, StateOf(report, "Claude Code"));
        Assert.Equal(AgentResumeState.Failed, StateOf(report, "Codex"));
        Assert.Equal(AgentResumeState.On, StateOf(report, "Copilot CLI"));
        Assert.Equal(contents, File.ReadAllText(ClaudeSettings));
        Assert.Equal(contents, File.ReadAllText(CodexHooks));
    }

    [Fact]
    public void AnAlteredCopyOfOurHookIsReportedInsteadOfDuplicated()
    {
        CreateAgents();
        Apply(enabled: true);
        var altered = File.ReadAllText(ClaudeSettings).Replace("\"claude\"", "\"other\"", StringComparison.Ordinal);
        File.WriteAllText(ClaudeSettings, altered);

        var report = Apply(enabled: true);

        Assert.Equal(AgentResumeState.Failed, StateOf(report, "Claude Code"));
        Assert.Equal(altered, File.ReadAllText(ClaudeSettings));
    }

    [Fact]
    public void TurningOffKeepsTheWrapperWhileAHookStillUsesIt()
    {
        CreateAgents();
        Apply(enabled: true);
        var contents = File.ReadAllText(ClaudeSettings).Replace("\"claude\"", "\"other\"", StringComparison.Ordinal);
        File.WriteAllText(ClaudeSettings, contents);

        var report = Apply(enabled: false);

        Assert.Equal(AgentResumeState.Failed, StateOf(report, "Claude Code"));
        Assert.True(File.Exists(Wrapper));
    }

    [Fact]
    public void ConfigurationDirectoriesFromTheEnvironmentAreUsed()
    {
        var claude = Path.Join(_root, "claude-config");
        var copilot = Path.Join(_root, "copilot-home");
        var codex = Path.Join(_root, "codex-home");
        foreach (var directory in new[] { claude, copilot, codex }) Directory.CreateDirectory(directory);

        var report = AgentResumeInstaller.Apply(new AgentResumePaths(_home, claude, copilot, codex), true, _tool, Template);

        Assert.All(report.Agents, agent => Assert.Equal(AgentResumeState.On, agent.State));
        Assert.True(File.Exists(Path.Join(claude, "settings.json")));
        Assert.True(File.Exists(Path.Join(copilot, "hooks", AgentResumeInstaller.CopilotHooksName)));
        Assert.True(File.Exists(Path.Join(codex, "hooks.json")));
        Assert.False(Directory.Exists(Path.Join(_home, ".claude")));
    }

    [Fact]
    public void FromEnvironmentReadsTheThreeVariablesAndIgnoresEmptyValues()
    {
        var paths = AgentResumePaths.FromEnvironment(_home, name => name switch
        {
            "CLAUDE_CONFIG_DIR" => @"C:\claude",
            "COPILOT_HOME" => "",
            "CODEX_HOME" => @"C:\codex",
            _ => null,
        });

        Assert.Equal(new AgentResumePaths(_home, @"C:\claude", null, @"C:\codex"), paths);
    }

    [Fact]
    public void AMissingToolOrTemplateFailsBeforeAnyFileIsWritten()
    {
        CreateAgents();

        var noTool = Apply(enabled: true, tool: Path.Join(_root, "missing.exe"));
        var noMarker = Apply(enabled: true, template: "@echo off\r\n");

        Assert.NotNull(noTool.Error);
        Assert.NotNull(noMarker.Error);
        Assert.False(File.Exists(Wrapper));
        Assert.False(File.Exists(ClaudeSettings));
    }

    [Theory]
    [InlineData("a%b")]
    [InlineData("a!b")]
    [InlineData("a^b")]
    [InlineData("a&b")]
    public void AHomePathCmdCannotQuoteFails(string name)
    {
        var home = Path.Join(_root, name);
        Directory.CreateDirectory(Path.Join(home, ".claude"));

        var report = AgentResumeInstaller.Apply(new AgentResumePaths(home), true, _tool, Template);

        Assert.NotNull(report.Error);
        Assert.False(File.Exists(Path.Join(home, ".claude", "settings.json")));
    }

    [Fact]
    public void AForeignFileAtTheWrapperPathFailsTheInstall()
    {
        CreateAgents();
        Directory.CreateDirectory(Path.GetDirectoryName(Wrapper)!);
        File.WriteAllText(Wrapper, "@echo off\r\nrem mine\r\n");

        var report = Apply(enabled: true);

        Assert.NotNull(report.Error);
        Assert.Equal("@echo off\r\nrem mine\r\n", File.ReadAllText(Wrapper));
        Assert.False(File.Exists(ClaudeSettings));
    }

    [Fact]
    public void DescribeGroupsTheAgentsByState()
    {
        var report = new AgentResumeReport([
            new("Claude Code", AgentResumeState.On),
            new("Copilot CLI", AgentResumeState.NotFound),
            new("Codex", AgentResumeState.On),
        ]);

        Assert.Equal("Claude Code, Codex: on. Copilot CLI: not found. " + CodexTrust, report.Describe());
    }

    [Fact]
    public void Describe_explains_a_user_daemon_setting()
    {
        var report = new AgentResumeReport([new("Codex", AgentResumeState.On, CodexUserOn)]);

        Assert.False(report.HasFailure);
        Assert.Equal("Codex: on. " + CodexUserOn + " " + CodexTrust, report.Describe());
    }

    // Codex 0.167 runs hooks in a shared app-server with another pane's AGTERM_*; the setting turns it off.

    [Fact]
    public void EnablingWritesTheCodexDaemonSetting()
    {
        CreateAgents();

        Apply(enabled: true);

        Assert.Equal("[features]\n" + CodexFeatureConfig.OwnedLine + "\n", File.ReadAllText(CodexConfig));
    }

    [Fact]
    public void EnablingKeepsTheUserCodexConfigAndBacksItUp()
    {
        CreateAgents();
        File.WriteAllText(CodexConfig, "model = \"o3\"\n");

        Apply(enabled: true);

        Assert.StartsWith("model = \"o3\"\n", File.ReadAllText(CodexConfig));
        Assert.Contains(CodexFeatureConfig.OwnedLine, File.ReadAllText(CodexConfig));
        Assert.Equal("model = \"o3\"\n", File.ReadAllText(CodexConfig + ".bak"));
    }

    [Fact]
    public void DisablingRemovesOnlyTheCodexDaemonLine()
    {
        CreateAgents();
        Apply(enabled: true);
        File.WriteAllText(CodexConfig, File.ReadAllText(CodexConfig) + "hooks = true\n");

        Apply(enabled: false);

        Assert.Equal("[features]\nhooks = true\n", File.ReadAllText(CodexConfig));
    }

    [Fact]
    public void A_user_daemon_setting_is_left_alone()
    {
        CreateAgents();
        const string mine = "[features]\ndaemon_auto_start = true\n";
        File.WriteAllText(CodexConfig, mine);

        var report = Apply(enabled: true);

        Assert.Equal(mine, File.ReadAllText(CodexConfig));
        var codex = report.Agents.Single(agent => agent.DisplayName == "Codex");
        Assert.Equal(AgentResumeState.On, codex.State);
        Assert.Equal(CodexUserOn, codex.Reason);
    }

    [Fact]
    public void An_inline_features_table_fails_Codex_only()
    {
        CreateAgents();
        const string inline = "features = { hooks = true }\n";
        File.WriteAllText(CodexConfig, inline);

        var report = Apply(enabled: true);

        var codex = report.Agents.Single(agent => agent.DisplayName == "Codex");
        Assert.Equal(AgentResumeState.Failed, codex.State);
        Assert.Equal("config.toml sets features inline, so agterm-win left it unchanged.", codex.Reason);
        Assert.True(File.Exists(CodexHooks));
        Assert.Equal(inline, File.ReadAllText(CodexConfig));
        Assert.Equal(AgentResumeState.On, StateOf(report, "Claude Code"));
        Assert.Equal(AgentResumeState.On, StateOf(report, "Copilot CLI"));
    }

    [Fact]
    public void DescribeShowsEachFailureWithItsReason()
    {
        var report = new AgentResumeReport([
            new("Claude Code", AgentResumeState.Failed, "settings.json is not valid JSON."),
            new("Copilot CLI", AgentResumeState.Off),
            new("Codex", AgentResumeState.Off),
        ]);

        Assert.True(report.HasFailure);
        Assert.Equal("Copilot CLI, Codex: off. Claude Code failed: settings.json is not valid JSON.", report.Describe());
        Assert.Equal("The resume hooks could not be installed: x", new AgentResumeReport([], "x").Describe());
        Assert.True(new AgentResumeReport([], "x").HasFailure);
    }

    // The wrapper as an agent runs it.

    private (int Code, string Output, string Error) InvokeWrapper(string payload, bool inside,
        string verb = "start", string? temporary = null)
    {
        var process = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "/d", "/c", "call", Wrapper, "claude", verb })
            process.ArgumentList.Add(arg);
        process.Environment["AGTERM_CAPTURE"] = Path.Join(_root, "capture.txt");
        process.Environment["AGTERM_SESSION_ID"] = inside ? "ABC" : "";
        process.Environment.Remove("AGTERMCTL");
        if (temporary is not null) process.Environment["TEMP"] = temporary;
        using var child = Process.Start(process)!;
        try
        {
            child.StandardInput.Write(payload);
            child.StandardInput.Close();
        }
        catch (IOException)
        {
            // the wrapper does not read stdin itself, so with a tool that does not read it either cmd
            // can exit before the write and the pipe is already closed
        }
        var output = child.StandardOutput.ReadToEnd();
        var error = child.StandardError.ReadToEnd();
        child.WaitForExit();
        return (child.ExitCode, output, error);
    }

    private string FakeTool(string body)
    {
        var tool = Path.Join(_root, "fake tool.cmd");
        File.WriteAllText(tool, "@echo off\r\n" + body);
        Directory.CreateDirectory(Path.Join(_home, ".claude"));
        Assert.Null(Apply(enabled: true, tool: tool).Error);
        return tool;
    }

    private string Log => Path.Join(Path.GetDirectoryName(Wrapper)!, "agent-resume.log");

    [Fact]
    public void TheWrapperPassesTheArgumentsAndThePayloadToTheTool()
    {
        FakeTool("echo %*>> \"%AGTERM_CAPTURE%\"\r\nmore >> \"%AGTERM_CAPTURE%\"\r\n");

        Assert.Equal((0, "", ""), InvokeWrapper("{\"session_id\":\"x\"}", inside: true, verb: "end"));

        var capture = File.ReadAllText(Path.Join(_root, "capture.txt"));
        Assert.Contains("agent-hook resume \"claude\" \"end\"", capture);
        Assert.Contains("{\"session_id\":\"x\"}", capture);
        Assert.False(File.Exists(Log));
    }

    [Fact]
    public void TheWrapperDoesNothingOutsideAgterm()
    {
        FakeTool("echo %*>> \"%AGTERM_CAPTURE%\"\r\n");

        Assert.Equal((0, "", ""), InvokeWrapper("{}", inside: false));

        Assert.False(File.Exists(Path.Join(_root, "capture.txt")));
    }

    [Fact]
    public void TheWrapperLogsWhatTheToolWritesToStandardError()
    {
        FakeTool("echo error: control pipe unavailable 1>&2\r\nexit /b 0\r\n");

        Assert.Equal((0, "", ""), InvokeWrapper("{}", inside: true));

        var log = File.ReadAllText(Log);
        Assert.Contains("claude start", log);
        Assert.Contains("error: control pipe unavailable", log);
    }

    [Fact]
    public void TheWrapperLogsAMissingTool()
    {
        var tool = FakeTool("");
        File.Delete(tool);

        Assert.Equal((0, "", ""), InvokeWrapper("{}", inside: true));

        Assert.Contains("not recognized", File.ReadAllText(Log));
    }

    [Fact]
    public void TheWrapperStillRunsTheToolWithoutAWritableTemporaryDirectory()
    {
        FakeTool("echo %*>> \"%AGTERM_CAPTURE%\"\r\n");

        var result = InvokeWrapper("{}", inside: true, temporary: Path.Join(_root, "missing temp"));

        Assert.Equal(0, result.Code);
        Assert.True(File.Exists(Path.Join(_root, "capture.txt")));
    }
}
