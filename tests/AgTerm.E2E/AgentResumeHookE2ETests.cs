using System.Diagnostics;
using System.Text.Json.Nodes;
using AgTerm.Cli;
using AgTerm.ControlServer;

namespace AgTerm.E2E;

/// <summary>
/// agtermctl agent-hook resume against the headless host over the real pipe, so a pin is read back
/// from the real tree. Most cases run the command in process; one runs the built agtermctl.exe to
/// cover the payload on real standard input.
/// </summary>
public class AgentResumeHookE2ETests : ControlApiTestCase
{
    private const string SessionId = "db3c0c96-4ade-4d25-9fb5-fc6a48d37dae";
    private const string OtherSessionId = "eab3742b-03b3-4bbf-bf1b-9762158e6303";

    private string NewSession() => SendOk("""{"cmd":"session.new","args":{"name":"agent"}}""")["id"]!.GetValue<string>();

    private (int Exit, string Error) Hook(string agent, string hookEvent, string session, string payload,
        IDictionary<string, string?>? extra = null)
    {
        var environment = new Dictionary<string, string?>
        {
            ["AGTERM_CONTROL_SOCKET"] = Host.PipeName,
            ["AGTERM_SESSION_ID"] = session.ToUpperInvariant(),
        };
        foreach (var (name, value) in extra ?? new Dictionary<string, string?>())
            environment[name] = value;
        var error = new StringWriter();
        var exit = Agtermctl.Run(["agent-hook", "resume", agent, hookEvent], new CliEnvironment
        {
            GetEnv = name => environment.TryGetValue(name, out var value) ? value : null,
            Out = TextWriter.Null,
            Error = error,
            In = new StringReader(payload),
        });
        return (exit, error.ToString());
    }

    private JsonObject Node(string session) =>
        Tree()["tree"]!["workspaces"]!.AsArray()
            .SelectMany(w => w!["sessions"]!.AsArray())
            .Single(s => s!["id"]!.GetValue<string>() == session)!.AsObject();

    private string? RestoreCommand(string session) => Node(session)["restoreCommand"]?.GetValue<string>();

    private void Pin(string session, string command) =>
        SendOk($$$"""{"cmd":"session.restore","target":"{{{session}}}","args":{"mode":"set","command":"{{{command}}}"}}""");

    private static string ClaudeStart(string id) => $$"""{"session_id":"{{id}}","hook_event_name":"SessionStart","source":"startup"}""";

    private static string ClaudeEnd(string id, string reason) => $$"""{"session_id":"{{id}}","hook_event_name":"SessionEnd","reason":"{{reason}}"}""";

    private static string CopilotStart(string id) => $$"""{"sessionId":"{{id}}","timestamp":1,"cwd":"C:\\work","source":"new","initialPrompt":"hi"}""";

    private static string CopilotEnd(string id, string reason) => $$"""{"sessionId":"{{id}}","timestamp":2,"cwd":"C:\\work","reason":"{{reason}}"}""";

    private static string CodexStart(string id) => $$"""{"session_id":"{{id}}","cwd":"C:\\work","hook_event_name":"SessionStart","source":"startup"}""";

    private static string CodexEnd(string id) => $$"""{"session_id":"{{id}}","cwd":"C:\\work","hook_event_name":"SessionEnd","reason":"other"}""";

    [Theory]
    [InlineData("claude", "claude --resume ")]
    [InlineData("copilot", "copilot --resume=")]
    [InlineData("codex", "codex --no-daemon resume ")]
    public void Start_pins_and_end_clears_the_agent_session(string agent, string prefix)
    {
        var session = NewSession();
        var (start, end) = agent switch
        {
            "claude" => (ClaudeStart(SessionId), ClaudeEnd(SessionId, "prompt_input_exit")),
            "copilot" => (CopilotStart(SessionId), CopilotEnd(SessionId, "user_exit")),
            _ => (CodexStart(SessionId), CodexEnd(SessionId)),
        };

        var pinned = Hook(agent, "start", session, start);
        Assert.Equal((0, ""), pinned);
        Assert.Equal(prefix + SessionId, RestoreCommand(session));

        var cleared = Hook(agent, "end", session, end);
        Assert.Equal((0, ""), cleared);
        Assert.Null(RestoreCommand(session));
    }

    [Fact]
    public void The_end_of_another_session_keeps_the_pin()
    {
        // after /clear the new session pins itself; a late end of the old one must not clear it
        var session = NewSession();
        Hook("copilot", "start", session, CopilotStart(SessionId));
        Hook("copilot", "start", session, CopilotStart(OtherSessionId));

        Assert.Equal(0, Hook("copilot", "end", session, CopilotEnd(SessionId, "user_exit")).Exit);

        Assert.Equal("copilot --resume=" + OtherSessionId, RestoreCommand(session));
    }

    [Fact]
    public void An_end_keeps_a_pin_set_by_someone_else()
    {
        var session = NewSession();
        Pin(session, "npm run dev");

        Hook("codex", "end", session, CodexEnd(SessionId));

        Assert.Equal("npm run dev", RestoreCommand(session));
    }

    [Fact]
    public void A_claude_pane_closing_keeps_the_pin()
    {
        // an app quit closes every pane, which ends Claude with reason "other"
        var session = NewSession();
        Hook("claude", "start", session, ClaudeStart(SessionId));

        Hook("claude", "end", session, ClaudeEnd(SessionId, "other"));

        Assert.Equal("claude --resume " + SessionId, RestoreCommand(session));
    }

    [Fact]
    public void A_stale_window_id_falls_back_to_the_frontmost_window()
    {
        var session = NewSession();
        Hook("codex", "start", session, CodexStart(SessionId));

        var run = Hook("codex", "end", session, CodexEnd(SessionId),
            new Dictionary<string, string?> { ["AGTERM_WINDOW_ID"] = Guid.NewGuid().ToString("D").ToUpperInvariant() });

        Assert.Equal((0, ""), run);
        Assert.Null(RestoreCommand(session));
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
        Pin(session, "claude --resume " + SessionId);
        var pane = new Dictionary<string, string?> { ["AGTERM_PANE"] = "right", ["AGTERM_PANE_ID"] = rightToken };

        Hook("claude", "start", session, ClaudeStart(SessionId), pane);
        Assert.Equal("claude --resume " + SessionId, Node(session)["splitRestoreCommand"]?.GetValue<string>());

        Assert.Equal((0, ""), Hook("claude", "end", session, ClaudeEnd(SessionId, "logout"), pane));

        var node = Node(session);
        Assert.Null(node["splitRestoreCommand"]);
        Assert.Equal("claude --resume " + SessionId, node["restoreCommand"]?.GetValue<string>());
    }

    [Fact]
    public void An_unknown_session_exits_zero_and_reports_on_stderr()
    {
        var run = Hook("claude", "start", Guid.NewGuid().ToString("D"), ClaudeStart(SessionId));

        Assert.Equal(0, run.Exit);
        Assert.StartsWith("error: ", run.Error);
    }

    [Fact]
    public async Task The_built_exe_reads_the_payload_from_standard_input()
    {
        var exe = AgtermctlEndToEndTests.LocateAgtermctlExe();
        Assert.True(File.Exists(exe), $"agtermctl.exe not built at {exe} — run dotnet build first");
        var session = NewSession();
        var info = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "agent-hook", "resume", "codex", "start" })
            info.ArgumentList.Add(argument);
        foreach (var name in info.Environment.Keys.Where(k => k.StartsWith("AGTERM", StringComparison.OrdinalIgnoreCase)).ToList())
            info.Environment.Remove(name);
        info.Environment["AGTERM_CONTROL_SOCKET"] = Host.PipeName;
        info.Environment["AGTERM_SESSION_ID"] = session.ToUpperInvariant();

        using var process = Process.Start(info)!;
        await process.StandardInput.WriteAsync(CodexStart(SessionId));
        process.StandardInput.Close();
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);

        Assert.Equal((0, "", ""), (process.ExitCode, stdout, await stderr));
        Assert.Equal("codex --no-daemon resume " + SessionId, RestoreCommand(session));
    }
}
