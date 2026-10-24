using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace AgTerm.E2E;

/// <summary>
/// cookbook/claude-session-resume against Windows PowerShell 5.1, the shell every Windows machine
/// has. The hook runs with the built agtermctl.exe against the headless host, so a pin is read
/// back from the real tree. The installer runs on a settings file in a temp directory, never on
/// the user's own ~/.claude/settings.json.
/// </summary>
public class ClaudeSessionResumeRecipeTests : ControlApiTestCase
{
    private const string ConversationId = "17d54f9e-a89e-4a74-9f07-1c9f22f88259";

    private static readonly string WindowsPowerShell =
        Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    // under the fixture's state directory, which its Dispose deletes
    private string Scratch => Path.Combine(StateDir, "recipe");

    private static string RecipeDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AgTerm.slnx")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return Path.Combine(dir!, "cookbook", "claude-session-resume");
    }

    private static string HookScript => Path.Combine(RecipeDirectory(), "claude-resume-hook.ps1");

    private static string InstallScript => Path.Combine(RecipeDirectory(), "install.ps1");

    private (int Exit, string Out, string Error) RunPowerShell(
        string script, string[] arguments, string stdin, IDictionary<string, string?> environment)
    {
        var info = new ProcessStartInfo(WindowsPowerShell)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }.Concat(arguments))
            info.ArgumentList.Add(argument);
        foreach (var name in info.Environment.Keys.Where(k => k.StartsWith("AGTERM", StringComparison.OrdinalIgnoreCase)).ToList())
            info.Environment.Remove(name);
        foreach (var (name, value) in environment)
            info.Environment[name] = value;

        using var process = Process.Start(info)!;
        process.StandardInput.Write(stdin);
        process.StandardInput.Close();
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "powershell did not exit");
        return (process.ExitCode, stdout, stderr.Result);
    }

    // --- the hook ------------------------------------------------------------------------

    private string NewSession() => SendOk("""{"cmd":"session.new","args":{"name":"claude"}}""")["id"]!.GetValue<string>();

    private (int Exit, string Out, string Error) RunHook(string session, string payload) =>
        RunPowerShell(HookScript, [], payload, new Dictionary<string, string?>
        {
            ["AGTERMCTL"] = AgtermctlEndToEndTests.LocateAgtermctlExe(),
            ["AGTERM_CONTROL_SOCKET"] = Host.PipeName,
            ["AGTERM_SESSION_ID"] = session,
        });

    private string? RestoreCommand(string session) =>
        Tree()["tree"]!["workspaces"]!.AsArray()
            .SelectMany(w => w!["sessions"]!.AsArray())
            .Single(s => s!["id"]!.GetValue<string>() == session)!["restoreCommand"]?.GetValue<string>();

    private static string Start(string id) =>
        $$"""{"session_id":"{{id}}","hook_event_name":"SessionStart","source":"startup"}""";

    private static string End(string reason) =>
        $$"""{"session_id":"{{ConversationId}}","hook_event_name":"SessionEnd","reason":"{{reason}}"}""";

    [Fact]
    public void Session_start_pins_the_live_conversation()
    {
        var session = NewSession();

        var run = RunHook(session, Start(ConversationId));

        Assert.True(run.Exit == 0, run.Error);
        Assert.Equal($"claude --resume {ConversationId}", RestoreCommand(session));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uuid; rm -rf ~")]
    public void Session_start_without_a_uuid_pins_nothing(string id)
    {
        // the pin is shell code typed on every launch, so only a UUID-shaped id goes in it
        var session = NewSession();

        Assert.Equal(0, RunHook(session, Start(id)).Exit);
        Assert.Null(RestoreCommand(session));
    }

    [Theory]
    [InlineData("prompt_input_exit")] // /exit, Ctrl+D
    [InlineData("logout")]
    public void Leaving_claude_clears_the_pin(string reason)
    {
        var session = NewSession();
        RunHook(session, Start(ConversationId));

        var run = RunHook(session, End(reason));

        Assert.True(run.Exit == 0, run.Error);
        Assert.Null(RestoreCommand(session));
    }

    [Theory]
    [InlineData("other")]  // the pane closing, which an app quit does to every pane (measured)
    [InlineData("clear")]  // a SessionStart with the new id follows
    [InlineData("resume")]
    public void Any_other_end_keeps_the_pin(string reason)
    {
        var session = NewSession();
        RunHook(session, Start(ConversationId));

        RunHook(session, End(reason));

        Assert.Equal($"claude --resume {ConversationId}", RestoreCommand(session));
    }

    [Fact]
    public void Outside_agterm_the_hook_does_nothing()
    {
        var run = RunPowerShell(HookScript, [], Start(ConversationId), new Dictionary<string, string?>
        {
            ["AGTERMCTL"] = Path.Combine(Scratch, "missing-agtermctl.exe"),
        });

        Assert.Equal(0, run.Exit);
        Assert.Equal("", run.Error);
    }

    // --- the installer -------------------------------------------------------------------

    private string SettingsPath => Path.Combine(Scratch, "claude", "settings.json");

    private string InstallDirectory => Path.Combine(Scratch, "install");

    private (int Exit, string Out, string Error) Install(params string[] extra) =>
        RunPowerShell(InstallScript, ["-SettingsPath", SettingsPath, "-InstallDirectory", InstallDirectory, .. extra], "",
            new Dictionary<string, string?>());

    private JsonObject Settings() => JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();

    private static string[] HandlerArgs(JsonObject settings, string eventName) =>
        settings["hooks"]![eventName]!.AsArray()
            .SelectMany(group => group!["hooks"]!.AsArray())
            .Select(handler => string.Join(" ", handler!["args"]?.AsArray().Select(a => a!.GetValue<string>()) ?? []))
            .ToArray();

    [Fact]
    public void Install_copies_the_hook_and_registers_both_events()
    {
        var run = Install();

        Assert.True(run.Exit == 0, run.Error);
        var hook = Path.Combine(InstallDirectory, "claude-resume-hook.ps1");
        Assert.True(File.Exists(hook));
        var settings = Settings();
        foreach (var eventName in new[] { "SessionStart", "SessionEnd" })
        {
            var group = Assert.Single(settings["hooks"]![eventName]!.AsArray())!;
            var handler = Assert.Single(group["hooks"]!.AsArray())!;
            Assert.Equal("powershell.exe", handler["command"]!.GetValue<string>());
            Assert.Equal(hook, handler["args"]!.AsArray().Select(a => a!.GetValue<string>()).Last());
        }
    }

    [Fact]
    public void Install_keeps_every_other_setting_and_hook()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """
            {
              "permissions": { "deny": ["Bash(git push *)"] },
              "hooks": {
                "SessionStart": [{ "hooks": [{ "type": "command", "command": "other-start.cmd" }] }],
                "Stop": [{ "hooks": [{ "type": "command", "command": "cmd.exe", "args": ["/c", "status.cmd", "completed"] }] }]
              }
            }
            """);

        Assert.Equal(0, Install().Exit);

        var settings = Settings();
        Assert.Equal("Bash(git push *)", settings["permissions"]!["deny"]![0]!.GetValue<string>());
        Assert.Equal("cmd.exe", settings["hooks"]!["Stop"]![0]!["hooks"]![0]!["command"]!.GetValue<string>());
        var start = settings["hooks"]!["SessionStart"]!.AsArray();
        Assert.Equal(2, start.Count);
        Assert.Equal("other-start.cmd", start[0]!["hooks"]![0]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void Install_keeps_strings_with_escapes_and_json_punctuation_intact()
    {
        // the installer re-indents the file itself, so a brace, comma or quote inside a string
        // must stay part of the string
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        const string tricky = "a \"{x: [1, 2]}\" C:\\dir\\";
        File.WriteAllText(SettingsPath, new JsonObject { ["note"] = tricky, ["empty"] = new JsonArray(), ["obj"] = new JsonObject() }.ToJsonString());

        Assert.Equal(0, Install().Exit);

        var settings = Settings();
        Assert.Equal(tricky, settings["note"]!.GetValue<string>());
        Assert.Empty(settings["empty"]!.AsArray());
        Assert.Empty(settings["obj"]!.AsObject());
    }

    [Fact]
    public void Install_keeps_the_place_of_an_event_already_in_the_file()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """
            { "hooks": { "SessionStart": [{ "hooks": [{ "type": "command", "command": "a.cmd" }] }],
                         "Stop": [{ "hooks": [{ "type": "command", "command": "b.cmd" }] }] } }
            """);

        Assert.Equal(0, Install().Exit);

        Assert.Equal(["SessionStart", "Stop", "SessionEnd"], Settings()["hooks"]!.AsObject().Select(p => p.Key));
    }

    [Fact]
    public void Install_writes_two_space_indented_json()
    {
        Assert.Equal(0, Install().Exit);

        var lines = File.ReadAllLines(SettingsPath);
        Assert.Equal("{", lines[0]);
        Assert.Equal("  \"hooks\": {", lines[1]);
        Assert.Equal("    \"SessionStart\": [", lines[2]);
    }

    [Fact]
    public void Install_backs_up_a_settings_file_it_changes()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        const string original = """{ "theme": "dark" }""";
        File.WriteAllText(SettingsPath, original);

        Assert.Equal(0, Install().Exit);

        var backup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "settings.json.*.bak"));
        Assert.Equal(original, File.ReadAllText(backup));
    }

    [Fact]
    public void A_second_install_changes_nothing()
    {
        Assert.Equal(0, Install().Exit);
        var first = File.ReadAllText(SettingsPath);
        var backups = Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "*.bak").Length;

        Assert.Equal(0, Install().Exit);

        Assert.Equal(first, File.ReadAllText(SettingsPath));
        Assert.Equal(backups, Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "*.bak").Length);
    }

    [Fact]
    public void Install_replaces_the_earlier_pin_only_hook()
    {
        // the first version of this recipe pinned from pin-claude-resume.ps1 and never cleared
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """
            { "hooks": { "SessionStart": [{ "hooks": [{ "type": "command", "command": "powershell.exe",
              "args": ["-NoProfile", "-File", "C:\\Users\\me\\.config\\agterm\\claude-restore\\pin-claude-resume.ps1"], "timeout": 15 }] }] } }
            """);

        Assert.Equal(0, Install().Exit);

        var args = Assert.Single(HandlerArgs(Settings(), "SessionStart"));
        Assert.EndsWith("claude-resume-hook.ps1", args);
    }

    [Fact]
    public void Uninstall_removes_only_this_recipe()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """
            { "hooks": { "SessionStart": [{ "hooks": [{ "type": "command", "command": "other-start.cmd" }] }] } }
            """);
        Assert.Equal(0, Install().Exit);

        var run = Install("-Uninstall");

        Assert.True(run.Exit == 0, run.Error);
        var settings = Settings();
        Assert.Equal("other-start.cmd", Assert.Single(settings["hooks"]!["SessionStart"]!.AsArray())!["hooks"]![0]!["command"]!.GetValue<string>());
        Assert.False(settings["hooks"]!.AsObject().ContainsKey("SessionEnd"));
        Assert.False(File.Exists(Path.Combine(InstallDirectory, "claude-resume-hook.ps1")));
    }

    [Fact]
    public void Install_refuses_a_settings_file_that_is_not_json_and_leaves_it_alone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ not json");

        var run = Install();

        Assert.NotEqual(0, run.Exit);
        Assert.Equal("{ not json", File.ReadAllText(SettingsPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "*.bak"));
    }

    [Fact]
    public void Install_keeps_non_ascii_text_in_the_settings_file()
    {
        // Windows PowerShell 5.1 reads a BOM-less file as ANSI and writes UTF-16 by default
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{ "language": "русский" }""", new UTF8Encoding(false));

        Assert.Equal(0, Install().Exit);

        var bytes = File.ReadAllBytes(SettingsPath);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "BOM written");
        Assert.Equal("русский", Settings()["language"]!.GetValue<string>());
    }
}
