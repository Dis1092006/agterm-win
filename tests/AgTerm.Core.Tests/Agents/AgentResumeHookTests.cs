using AgTerm.Core.Agents;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Agents;

public class AgentResumeHookTests
{
    private const string Id = "db3c0c96-4ade-4d25-9fb5-fc6a48d37dae";

    private static AgentResumeProfile Agent(string name) => AgentResumeProfile.Find(name)!;

    // payloads as each CLI sends them (Claude Code recipe, Copilot CLI 1.0.105 and Codex 0.163.0 probes)
    private static string ClaudeStart(string id) =>
        $$"""{"session_id":"{{id}}","transcript_path":"C:\\t.jsonl","cwd":"C:\\work","hook_event_name":"SessionStart","source":"startup"}""";

    private static string ClaudeEnd(string reason) =>
        $$"""{"session_id":"{{Id}}","cwd":"C:\\work","hook_event_name":"SessionEnd","reason":"{{reason}}"}""";

    private static string CopilotStart(string id) =>
        $$"""{"sessionId":"{{id}}","timestamp":1793929905022,"cwd":"C:\\work","source":"new","initialPrompt":"hi"}""";

    private static string CopilotEnd(string reason) =>
        $$"""{"sessionId":"{{Id}}","timestamp":1793929912008,"cwd":"C:\\work","reason":"{{reason}}"}""";

    private static string CodexStart(string id) =>
        $$"""{"session_id":"{{id}}","transcript_path":"C:\\r.jsonl","cwd":"C:\\work","hook_event_name":"SessionStart","model":"gpt-5","permission_mode":"default","source":"startup"}""";

    private static string CodexEnd(string reason) =>
        $$"""{"session_id":"{{Id}}","cwd":"C:\\work","hook_event_name":"SessionEnd","reason":"{{reason}}"}""";

    [Fact]
    public void The_table_names_claude_copilot_and_codex()
    {
        Assert.Equal(["claude", "copilot", "codex"], AgentResumeProfile.All.Select(agent => agent.Name));
        Assert.Equal(["Claude Code", "Copilot CLI", "Codex"], AgentResumeProfile.All.Select(agent => agent.DisplayName));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("Claude")]
    [InlineData("nope")]
    public void Find_matches_the_exact_name_only(string name)
    {
        Assert.Equal(name == "claude", AgentResumeProfile.Find(name) is not null);
    }

    [Fact]
    public void Each_agent_pins_its_own_resume_command()
    {
        Assert.Equal(AgentResumeDecision.Pin("claude --resume " + Id),
            AgentResumeHook.Decide(Agent("claude"), AgentHookEvent.Start, ClaudeStart(Id)));
        Assert.Equal(AgentResumeDecision.Pin("copilot --resume=" + Id),
            AgentResumeHook.Decide(Agent("copilot"), AgentHookEvent.Start, CopilotStart(Id)));
        Assert.Equal(AgentResumeDecision.Pin("codex resume " + Id),
            AgentResumeHook.Decide(Agent("codex"), AgentHookEvent.Start, CodexStart(Id)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uuid; rm -rf ~")]
    [InlineData("db3c0c96-4ade-4d25-9fb5-fc6a48d37dae; calc")]
    public void An_id_that_is_not_a_uuid_pins_nothing(string id)
    {
        // the pin is shell code typed on every launch, so only a UUID-shaped id goes in it
        Assert.Equal(AgentResumeDecision.Ignore, AgentResumeHook.Decide(Agent("claude"), AgentHookEvent.Start, ClaudeStart(id)));
        Assert.Equal(AgentResumeDecision.Ignore, AgentResumeHook.Decide(Agent("copilot"), AgentHookEvent.Start, CopilotStart(id)));
        Assert.Equal(AgentResumeDecision.Ignore, AgentResumeHook.Decide(Agent("codex"), AgentHookEvent.Start, CodexStart(id)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"session_id":42}""")]
    [InlineData("""{"sessionId":"db3c0c96-4ade-4d25-9fb5-fc6a48d37dae"}""")]
    public void A_payload_without_the_agents_id_field_is_ignored(string payload)
    {
        Assert.Equal(AgentResumeDecision.Ignore, AgentResumeHook.Decide(Agent("claude"), AgentHookEvent.Start, payload));
    }

    [Theory]
    [InlineData("agentId")]
    [InlineData("parentAgentId")]
    public void A_copilot_subagent_is_ignored(string field)
    {
        // a subagent runs inside its parent's session and pane
        var payload = $$"""{"sessionId":"{{Id}}","source":"new","{{field}}":"a1","reason":"user_exit"}""";

        Assert.Equal(AgentResumeDecision.Ignore, AgentResumeHook.Decide(Agent("copilot"), AgentHookEvent.Start, payload));
        Assert.Equal(AgentResumeDecision.Ignore, AgentResumeHook.Decide(Agent("copilot"), AgentHookEvent.End, payload));
    }

    [Theory]
    [InlineData("prompt_input_exit", true)] // /exit, Ctrl+D
    [InlineData("logout", true)]
    [InlineData("other", false)]            // the pane closing, which an app quit does to every pane
    [InlineData("clear", false)]
    [InlineData("resume", false)]
    public void Claude_clears_only_when_the_user_leaves_it(string reason, bool clears)
    {
        Assert.Equal(clears ? AgentResumeDecision.ClearIfPinned("claude --resume " + Id) : AgentResumeDecision.Ignore,
            AgentResumeHook.Decide(Agent("claude"), AgentHookEvent.End, ClaudeEnd(reason)));
    }

    [Theory]
    [InlineData("user_exit", true)] // /exit, /clear, Ctrl+C, Ctrl+D, the pane closing
    [InlineData("complete", true)]  // copilot -p
    [InlineData("error", false)]
    [InlineData("abort", false)]
    [InlineData("timeout", false)]
    public void Copilot_clears_on_user_exit_and_complete(string reason, bool clears)
    {
        Assert.Equal(clears ? AgentResumeDecision.ClearIfPinned("copilot --resume=" + Id) : AgentResumeDecision.Ignore,
            AgentResumeHook.Decide(Agent("copilot"), AgentHookEvent.End, CopilotEnd(reason)));
    }

    [Theory]
    [InlineData("other")] // the only reason Codex sends
    [InlineData("anything")]
    public void Codex_clears_on_any_end(string reason)
    {
        Assert.Equal(AgentResumeDecision.ClearIfPinned("codex resume " + Id),
            AgentResumeHook.Decide(Agent("codex"), AgentHookEvent.End, CodexEnd(reason)));
    }

    [Fact]
    public void An_end_without_a_uuid_clears_nothing()
    {
        Assert.Equal(AgentResumeDecision.Ignore, AgentResumeHook.Decide(Agent("codex"), AgentHookEvent.End,
            """{"session_id":"x","reason":"other"}"""));
    }

    // --- the pane's current pin ---------------------------------------------------------

    private const string Session = "aaaabbbb-cccc-dddd-eeee-ffff00001111";

    private static ControlTree Tree(string? restore, string? split, params ControlSurfaceNode[] surfaces) => new()
    {
        Workspaces =
        [
            new ControlWorkspaceNode
            {
                Id = "w1", Name = "Work",
                Sessions =
                [
                    new ControlSessionNode { Id = "other", Name = "other", Cwd = "C:\\", RestoreCommand = "nope" },
                    new ControlSessionNode
                    {
                        Id = Session, Name = "agent", Cwd = "C:\\", RestoreCommand = restore, SplitRestoreCommand = split,
                        Surfaces = surfaces,
                    },
                ],
            },
        ],
    };

    private static ControlSurfaceNode Surface(string kind, string token) => new() { Id = "s-" + kind, Kind = kind, PaneId = token };

    [Fact]
    public void The_left_pane_reads_the_restore_command()
    {
        Assert.Equal(new AgentResumePane(true, "a"), AgentResumeHook.PaneRestoreCommand(Tree("a", "b"), Session, paneId: null, pane: null));
        Assert.Equal(new AgentResumePane(true, "a"), AgentResumeHook.PaneRestoreCommand(Tree("a", "b"), Session, paneId: null, pane: "left"));
    }

    [Fact]
    public void The_right_pane_reads_the_split_restore_command()
    {
        Assert.Equal(new AgentResumePane(true, "b"), AgentResumeHook.PaneRestoreCommand(Tree("a", "b"), Session, paneId: null, pane: "right"));
    }

    [Fact]
    public void The_pane_token_wins_over_the_pane_name()
    {
        // a promoted-then-re-split pane keeps its token but its AGTERM_PANE is stale
        var tree = Tree("a", "b", Surface("left", "t-left"), Surface("right", "t-right"));

        Assert.Equal(new AgentResumePane(true, "b"), AgentResumeHook.PaneRestoreCommand(tree, Session, paneId: "t-right", pane: "left"));
        Assert.Equal(new AgentResumePane(true, "a"), AgentResumeHook.PaneRestoreCommand(tree, Session, paneId: "t-left", pane: "right"));
        Assert.Equal(new AgentResumePane(true, "b"), AgentResumeHook.PaneRestoreCommand(tree, Session, paneId: "gone", pane: "right"));
    }

    [Fact]
    public void The_scratch_pane_has_no_restore_command()
    {
        var tree = Tree("a", "b", Surface("scratch", "t-scratch"));

        Assert.Equal(new AgentResumePane(true, null), AgentResumeHook.PaneRestoreCommand(tree, Session, paneId: "t-scratch", pane: null));
        Assert.Equal(new AgentResumePane(true, null), AgentResumeHook.PaneRestoreCommand(tree, Session, paneId: null, pane: "scratch"));
    }

    [Fact]
    public void The_session_id_matches_in_any_case()
    {
        // the shell gets AGTERM_SESSION_ID in upper case
        Assert.Equal(new AgentResumePane(true, "a"), AgentResumeHook.PaneRestoreCommand(Tree("a", "b"), Session.ToUpperInvariant(), paneId: null, pane: null));
    }

    [Fact]
    public void A_session_not_in_the_tree_is_not_found()
    {
        Assert.Equal(new AgentResumePane(false, null), AgentResumeHook.PaneRestoreCommand(Tree("a", "b"), "missing", paneId: null, pane: null));
    }
}
