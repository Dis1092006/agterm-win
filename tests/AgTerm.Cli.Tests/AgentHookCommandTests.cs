using AgTerm.Cli;
using AgTerm.ControlServer;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests;

// agent-hook resume is Windows-only (parity/windows-only.json); the end-to-end cases against the
// headless host are in AgTerm.E2E AgentResumeHookE2ETests.
public sealed class AgentHookCommandTests
{
    private const string Id = "db3c0c96-4ade-4d25-9fb5-fc6a48d37dae";
    private const string Session = "AAAABBBB-CCCC-DDDD-EEEE-FFFF00001111";

    private static readonly Dictionary<string, string?> PaneEnvironment = new()
    {
        ["AGTERM_SESSION_ID"] = Session,
        ["AGTERM_WINDOW_ID"] = "WIN-1",
        ["AGTERM_SOCKET"] = @"C:\state\agterm.sock",
        ["AGTERM_PANE"] = "left",
        ["AGTERM_PANE_ID"] = "token-1",
    };

    private sealed record Run(int Exit, string Out, string Error, List<(string Pipe, ControlRequest Request)> Sent);

    private static Run Hook(string[] argv, string stdin, IDictionary<string, string?> environment,
        Func<ControlRequest, ControlResponse>? respond = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var sent = new List<(string, ControlRequest)>();
        var env = new CliEnvironment
        {
            GetEnv = name => environment.TryGetValue(name, out var value) ? value : null,
            Out = output,
            Error = error,
            In = new StringReader(stdin),
            Send = (pipe, request) =>
            {
                sent.Add((pipe, request));
                return respond?.Invoke(request) ?? new ControlResponse { Ok = true };
            },
        };
        var exit = Agtermctl.Run(["agent-hook", "resume", .. argv], env);
        return new Run(exit, output.ToString(), error.ToString(), sent);
    }

    private static string Start(string id) => $$"""{"session_id":"{{id}}","hook_event_name":"SessionStart","source":"startup"}""";

    private static string End(string reason) => $$"""{"session_id":"{{Id}}","hook_event_name":"SessionEnd","reason":"{{reason}}"}""";

    private static ControlResponse TreeWith(string? restoreCommand) => new()
    {
        Ok = true,
        Result = new ControlResult
        {
            Tree = new ControlTree
            {
                Workspaces =
                [
                    new ControlWorkspaceNode
                    {
                        Id = "w", Name = "w",
                        Sessions = [new ControlSessionNode { Id = Session.ToLowerInvariant(), Name = "s", Cwd = "C:\\", RestoreCommand = restoreCommand }],
                    },
                ],
            },
        },
    };

    [Fact]
    public void Start_pins_the_resume_command_on_the_hooks_pane()
    {
        var run = Hook(["claude", "start"], Start(Id), PaneEnvironment);

        Assert.Equal(0, run.Exit);
        Assert.Equal("", run.Out);
        Assert.Equal("", run.Error);
        var (pipe, request) = Assert.Single(run.Sent);
        Assert.Equal(ControlPipeName.Derive(@"C:\state\agterm.sock"), pipe);
        Assert.Equal(WireCommand.SessionRestore, request.Cmd);
        Assert.Equal(Session, request.Target);
        Assert.Equal("set", request.Args?.Mode);
        Assert.Equal("claude --resume " + Id, request.Args?.Command);
        Assert.Equal("left", request.Args?.Pane);
        Assert.Equal("token-1", request.Args?.PaneId);
        Assert.Null(request.Args?.Window);
    }

    [Fact]
    public void Without_a_socket_path_it_uses_the_control_socket_override()
    {
        var environment = new Dictionary<string, string?>
        {
            ["AGTERM_SESSION_ID"] = Session,
            ["AGTERM_CONTROL_SOCKET"] = "test-pipe",
        };

        var run = Hook(["codex", "start"], Start(Id), environment);

        var (pipe, request) = Assert.Single(run.Sent);
        Assert.Equal("test-pipe", pipe);
        Assert.Null(request.Args?.Pane);
        Assert.Null(request.Args?.PaneId);
    }

    [Fact]
    public void Outside_agterm_it_does_nothing()
    {
        var run = Hook(["claude", "start"], Start(Id), new Dictionary<string, string?>());

        Assert.Equal(0, run.Exit);
        Assert.Empty(run.Sent);
        Assert.Equal("", run.Error);
    }

    [Fact]
    public void A_payload_the_agent_table_ignores_sends_nothing()
    {
        var run = Hook(["claude", "start"], Start("not-a-uuid"), PaneEnvironment);

        Assert.Equal(0, run.Exit);
        Assert.Empty(run.Sent);
    }

    [Fact]
    public void End_clears_the_pin_when_it_is_still_this_session()
    {
        var run = Hook(["claude", "end"], End("prompt_input_exit"), PaneEnvironment,
            request => request.Cmd == WireCommand.Tree ? TreeWith("claude --resume " + Id) : new ControlResponse { Ok = true });

        Assert.Equal(0, run.Exit);
        Assert.Equal("", run.Error);
        Assert.Collection(run.Sent,
            tree =>
            {
                Assert.Equal(WireCommand.Tree, tree.Request.Cmd);
                Assert.Equal("WIN-1", tree.Request.Args?.Window);
            },
            clear =>
            {
                Assert.Equal(WireCommand.SessionRestore, clear.Request.Cmd);
                Assert.Equal(Session, clear.Request.Target);
                Assert.Equal("clear", clear.Request.Args?.Mode);
                Assert.Null(clear.Request.Args?.Command);
                Assert.Equal("left", clear.Request.Args?.Pane);
                Assert.Equal("token-1", clear.Request.Args?.PaneId);
            });
    }

    [Theory]
    [InlineData("claude --resume eab3742b-03b3-4bbf-bf1b-9762158e6303")] // a later session pinned itself
    [InlineData("npm run dev")]                                           // someone else's pin
    [InlineData(null)]
    public void End_keeps_a_pin_that_is_not_this_session(string? pin)
    {
        var run = Hook(["claude", "end"], End("prompt_input_exit"), PaneEnvironment,
            request => request.Cmd == WireCommand.Tree ? TreeWith(pin) : new ControlResponse { Ok = true });

        Assert.Equal(0, run.Exit);
        Assert.Equal(WireCommand.Tree, Assert.Single(run.Sent).Request.Cmd);
    }

    [Fact]
    public void An_end_reason_that_keeps_the_pin_reads_nothing()
    {
        var run = Hook(["claude", "end"], End("other"), PaneEnvironment);

        Assert.Empty(run.Sent);
    }

    [Fact]
    public void End_falls_back_to_the_frontmost_window()
    {
        var run = Hook(["codex", "end"], End("other"), PaneEnvironment, request => request.Cmd switch
        {
            WireCommand.Tree when request.Args?.Window is not null => new ControlResponse { Ok = false, Error = "no such window: WIN-1" },
            WireCommand.Tree => TreeWith("codex --no-daemon resume " + Id),
            _ => new ControlResponse { Ok = true },
        });

        Assert.Equal(0, run.Exit);
        Assert.Equal("", run.Error);
        Assert.Equal(3, run.Sent.Count);
        Assert.Null(run.Sent[1].Request.Args?.Window);
        Assert.Equal("clear", run.Sent[2].Request.Args?.Mode);
    }

    [Fact]
    public void Codex_hook_from_the_shared_app_server_sends_nothing()
    {
        // Codex 0.167 runs hooks in its shared app-server, which carries the AGTERM_* of another pane
        var environment = new Dictionary<string, string?>(PaneEnvironment) { ["CODEX_DAEMON_SHUTDOWN_SOCKET"] = "1" };

        var run = Hook(["codex", "start"], Start(Id), environment);

        Assert.Equal(0, run.Exit);
        Assert.Empty(run.Sent);
        Assert.Equal("", run.Error);
    }

    [Fact]
    public void Codex_end_clears_the_pin_from_before_no_daemon()
    {
        var run = Hook(["codex", "end"], End("other"), PaneEnvironment, request => request.Cmd == WireCommand.Tree
            ? TreeWith("codex resume " + Id)
            : new ControlResponse { Ok = true });

        Assert.Equal(WireCommand.SessionRestore, run.Sent[^1].Request.Cmd);
        Assert.Equal("clear", run.Sent[^1].Request.Args?.Mode);
    }

    [Fact]
    public void A_session_in_no_window_clears_nothing()
    {
        var run = Hook(["codex", "end"], End("other"), PaneEnvironment, request => request.Cmd == WireCommand.Tree
            ? new ControlResponse { Ok = true, Result = new ControlResult { Tree = new ControlTree { Workspaces = [] } } }
            : new ControlResponse { Ok = true });

        Assert.Equal(0, run.Exit);
        Assert.All(run.Sent, sent => Assert.Equal(WireCommand.Tree, sent.Request.Cmd));
        Assert.Equal(2, run.Sent.Count);
    }

    [Fact]
    public void A_refused_pin_exits_zero_and_reports_on_stderr()
    {
        // a hook must never fail the agent; stderr is for the wrapper's log
        var run = Hook(["claude", "start"], Start(Id), PaneEnvironment,
            _ => new ControlResponse { Ok = false, Error = "no such session: x" });

        Assert.Equal(0, run.Exit);
        Assert.Equal("", run.Out);
        Assert.Equal("error: no such session: x", run.Error.TrimEnd());
    }

    [Fact]
    public void A_transport_failure_exits_zero_and_reports_on_stderr()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var env = new CliEnvironment
        {
            GetEnv = name => PaneEnvironment.TryGetValue(name, out var value) ? value : null,
            Out = output,
            Error = error,
            In = new StringReader(Start(Id)),
            Send = (_, _) => throw new PipeClientException("connect(x) failed: timeout — is agterm running?"),
        };

        Assert.Equal(0, Agtermctl.Run(["agent-hook", "resume", "claude", "start"], env));
        Assert.Equal("", output.ToString());
        Assert.Equal("error: connect(x) failed: timeout — is agterm running?", error.ToString().TrimEnd());
    }

    [Theory]
    [InlineData("nope", "start")]
    [InlineData("claude", "stop")]
    [InlineData("Claude", "start")]
    public void An_unknown_agent_or_event_is_a_usage_error(string agent, string hookEvent)
    {
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRootAny(["agent-hook", "resume", agent, hookEvent]));
    }

    [Fact]
    public void Every_agent_in_the_table_is_accepted()
    {
        foreach (var agent in AgTerm.Core.Agents.AgentResumeProfile.All)
        {
            Assert.IsType<AgentHookCommands.Resume>(Agtermctl.ParseAsRootAny(["agent-hook", "resume", agent.Name, "end"]));
        }
    }
}
