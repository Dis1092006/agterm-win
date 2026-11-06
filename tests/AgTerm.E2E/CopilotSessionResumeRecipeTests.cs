using System.Diagnostics;
using System.Text.Json.Nodes;
using AgTerm.ControlServer;
using Xunit;

namespace AgTerm.E2E;

/// <summary>
/// cookbook/copilot-session-resume against Windows PowerShell 5.1. The hook runs with the built
/// agtermctl.exe against the headless host, so a pin is read back from the real tree. The installer
/// writes into a temp directory, never into the user's own ~/.copilot.
/// </summary>
public class CopilotSessionResumeRecipeTests : ControlApiTestCase
{
    private const string SessionId = "db3c0c96-4ade-4d25-9fb5-fc6a48d37dae";
    private const string OtherSessionId = "eab3742b-03b3-4bbf-bf1b-9762158e6303";

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
        return Path.Combine(dir!, "cookbook", "copilot-session-resume");
    }

    private static string HookScript => Path.Combine(RecipeDirectory(), "copilot-resume-hook.ps1");

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
        foreach (var name in info.Environment.Keys
                     .Where(k => k.StartsWith("AGTERM", StringComparison.OrdinalIgnoreCase) ||
                                 k.Equals("COPILOT_HOME", StringComparison.OrdinalIgnoreCase)).ToList())
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

    private string NewSession() => SendOk("""{"cmd":"session.new","args":{"name":"copilot"}}""")["id"]!.GetValue<string>();

    private (int Exit, string Out, string Error) RunHook(
        string session, string eventName, string payload, IDictionary<string, string?>? extra = null)
    {
        var environment = new Dictionary<string, string?>
        {
            ["AGTERMCTL"] = AgtermctlEndToEndTests.LocateAgtermctlExe(),
            ["AGTERM_CONTROL_SOCKET"] = Host.PipeName,
            ["AGTERM_SESSION_ID"] = session,
        };
        foreach (var (name, value) in extra ?? new Dictionary<string, string?>())
            environment[name] = value;
        return RunPowerShell(HookScript, [eventName], payload, environment);
    }

    private JsonObject Node(string session) =>
        Tree()["tree"]!["workspaces"]!.AsArray()
            .SelectMany(w => w!["sessions"]!.AsArray())
            .Single(s => s!["id"]!.GetValue<string>() == session)!.AsObject();

    private string? RestoreCommand(string session) => Node(session)["restoreCommand"]?.GetValue<string>();

    private void Pin(string session, string command, string pane = "left") =>
        SendOk($$$"""{"cmd":"session.restore","target":"{{{session}}}","args":{"mode":"set","command":"{{{command}}}","pane":"{{{pane}}}"}}""");

    // the payloads as Copilot CLI 1.0.105 sends them (measured); no field names the event
    private static string Start(string id, string source = "new") =>
        $$"""{"sessionId":"{{id}}","timestamp":1793929905022,"cwd":"C:\\work","source":"{{source}}","initialPrompt":"hi"}""";

    private static string End(string id, string reason) =>
        $$"""{"sessionId":"{{id}}","timestamp":1793929912008,"cwd":"C:\\work","reason":"{{reason}}"}""";

    [Theory]
    [InlineData("new")]
    [InlineData("resume")]
    public void Session_start_pins_the_live_session(string source)
    {
        var session = NewSession();

        var run = RunHook(session, "sessionStart", Start(SessionId, source));

        Assert.True(run.Exit == 0, run.Error);
        Assert.Equal($"copilot --resume={SessionId}", RestoreCommand(session));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uuid; rm -rf ~")]
    public void Session_start_without_a_uuid_pins_nothing(string id)
    {
        // the pin is shell code typed on every launch, so only a UUID-shaped id goes in it
        var session = NewSession();

        Assert.Equal(0, RunHook(session, "sessionStart", Start(id)).Exit);
        Assert.Null(RestoreCommand(session));
    }

    [Fact]
    public void A_subagent_pins_nothing()
    {
        var session = NewSession();
        var payload = $$"""{"sessionId":"{{SessionId}}","timestamp":1,"cwd":"C:\\work","source":"new","agentId":"a1"}""";

        Assert.Equal(0, RunHook(session, "sessionStart", payload).Exit);
        Assert.Null(RestoreCommand(session));
    }

    [Theory]
    [InlineData("user_exit")] // /exit, /clear, Ctrl+C twice, Ctrl+D twice, the pane closing (measured)
    [InlineData("complete")]  // a copilot -p run
    public void Leaving_copilot_clears_its_own_pin(string reason)
    {
        var session = NewSession();
        RunHook(session, "sessionStart", Start(SessionId));

        var run = RunHook(session, "sessionEnd", End(SessionId, reason));

        Assert.True(run.Exit == 0, run.Error);
        Assert.Null(RestoreCommand(session));
    }

    [Fact]
    public void The_end_of_another_session_keeps_the_pin()
    {
        // after /clear the next prompt pins the new session; a late end of the old one must not clear it
        var session = NewSession();
        RunHook(session, "sessionStart", Start(SessionId));
        RunHook(session, "sessionStart", Start(OtherSessionId));

        var run = RunHook(session, "sessionEnd", End(SessionId, "user_exit"));

        Assert.True(run.Exit == 0, run.Error);
        Assert.Equal($"copilot --resume={OtherSessionId}", RestoreCommand(session));
    }

    [Fact]
    public void An_end_keeps_a_pin_set_by_someone_else()
    {
        var session = NewSession();
        Pin(session, "npm run dev");

        RunHook(session, "sessionEnd", End(SessionId, "user_exit"));

        Assert.Equal("npm run dev", RestoreCommand(session));
    }

    [Theory]
    [InlineData("error")]
    [InlineData("abort")]
    [InlineData("timeout")]
    public void Any_other_end_keeps_the_pin(string reason)
    {
        var session = NewSession();
        RunHook(session, "sessionStart", Start(SessionId));

        RunHook(session, "sessionEnd", End(SessionId, reason));

        Assert.Equal($"copilot --resume={SessionId}", RestoreCommand(session));
    }

    [Fact]
    public void The_split_pane_pins_and_clears_its_own_restore_command()
    {
        var session = NewSession();
        var model = Host.Store.SessionWithId(Guid.Parse(session))!;
        model.Surface = new FakeSurface { PaneToken = "main-" + Guid.NewGuid().ToString("N") };
        SendOk($$$"""{"cmd":"session.split","target":"{{{session}}}","args":{"mode":"on"}}""");
        var rightToken = "right-" + Guid.NewGuid().ToString("N");
        model.SplitSurface = new FakeSurface { PaneToken = rightToken };
        Pin(session, "copilot --resume=" + SessionId);
        var pane = new Dictionary<string, string?> { ["AGTERM_PANE"] = "right", ["AGTERM_PANE_ID"] = rightToken };

        RunHook(session, "sessionStart", Start(SessionId), pane);
        Assert.Equal($"copilot --resume={SessionId}", Node(session)["splitRestoreCommand"]?.GetValue<string>());

        var run = RunHook(session, "sessionEnd", End(SessionId, "user_exit"), pane);

        Assert.True(run.Exit == 0, run.Error);
        var node = Node(session);
        Assert.Null(node["splitRestoreCommand"]);
        Assert.Equal($"copilot --resume={SessionId}", node["restoreCommand"]?.GetValue<string>());
    }

    [Fact]
    public void Outside_agterm_the_hook_does_nothing()
    {
        var run = RunPowerShell(HookScript, ["sessionStart"], Start(SessionId), new Dictionary<string, string?>
        {
            ["AGTERMCTL"] = Path.Combine(Scratch, "missing-agtermctl.exe"),
        });

        Assert.Equal(0, run.Exit);
        Assert.Equal("", run.Error);
    }

    [Fact]
    public void Another_event_does_nothing()
    {
        var session = NewSession();

        var run = RunHook(session, "agentStop", Start(SessionId));

        Assert.Equal(0, run.Exit);
        Assert.Null(RestoreCommand(session));
    }

    // --- the installer -------------------------------------------------------------------

    private string CopilotHome => Path.Combine(Scratch, "copilot");

    private string HooksFile => Path.Combine(CopilotHome, "hooks", "copilot-session-resume.json");

    private string InstallDirectory => Path.Combine(Scratch, "install");

    private (int Exit, string Out, string Error) Install(params string[] extra) =>
        RunPowerShell(InstallScript, ["-InstallDirectory", InstallDirectory, .. extra], "",
            new Dictionary<string, string?> { ["COPILOT_HOME"] = CopilotHome });

    [Fact]
    public void Install_copies_the_hook_and_writes_its_own_hooks_file()
    {
        var run = Install();

        Assert.True(run.Exit == 0, run.Error);
        var hook = Path.Combine(InstallDirectory, "copilot-resume-hook.ps1");
        Assert.True(File.Exists(hook));
        var file = JsonNode.Parse(File.ReadAllText(HooksFile))!.AsObject();
        Assert.Equal(1, file["version"]!.GetValue<int>());
        var hooks = file["hooks"]!.AsObject();
        Assert.Equal(["sessionStart", "sessionEnd"], hooks.Select(p => p.Key));
        foreach (var (eventName, entries) in hooks)
        {
            var handler = Assert.Single(entries!.AsArray())!;
            Assert.Equal("command", handler["type"]!.GetValue<string>());
            Assert.Equal("powershell.exe", handler["exec"]!.GetValue<string>());
            var args = handler["args"]!.AsArray().Select(a => a!.GetValue<string>()).ToArray();
            Assert.Equal([hook, eventName], args[^2..]);
        }
    }

    [Fact]
    public void Install_writes_under_the_home_directory_without_copilot_home()
    {
        var home = Path.Combine(Scratch, "home");
        Directory.CreateDirectory(home);

        var run = RunPowerShell(InstallScript, ["-InstallDirectory", InstallDirectory], "",
            new Dictionary<string, string?> { ["USERPROFILE"] = home, ["HOME"] = home });

        Assert.True(run.Exit == 0, run.Error);
        Assert.True(File.Exists(Path.Combine(home, ".copilot", "hooks", "copilot-session-resume.json")));
    }

    [Fact]
    public void A_second_install_changes_nothing()
    {
        Assert.Equal(0, Install().Exit);
        var first = File.ReadAllText(HooksFile);
        var written = File.GetLastWriteTimeUtc(HooksFile);

        Assert.Equal(0, Install().Exit);

        Assert.Equal(first, File.ReadAllText(HooksFile));
        Assert.Equal(written, File.GetLastWriteTimeUtc(HooksFile));
    }

    [Fact]
    public void Uninstall_removes_only_this_recipe()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HooksFile)!);
        var other = Path.Combine(Path.GetDirectoryName(HooksFile)!, "agterm-win-status.json");
        File.WriteAllText(other, """{ "version": 1, "hooks": {} }""");
        Assert.Equal(0, Install().Exit);

        var run = Install("-Uninstall");

        Assert.True(run.Exit == 0, run.Error);
        Assert.False(File.Exists(HooksFile));
        Assert.False(File.Exists(Path.Combine(InstallDirectory, "copilot-resume-hook.ps1")));
        Assert.True(File.Exists(other));
    }
}
