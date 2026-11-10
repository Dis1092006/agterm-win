using System.CommandLine;
using AgTerm.ControlServer;
using AgTerm.Core.Agents;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli;

// Windows-only (parity/windows-only.json). Upstream resumes agents with per-agent zsh recipes; the
// port runs one handler from each agent's own hooks, so the resume-agent-sessions setting can
// install them without a script. It only composes session.restore and tree: no wire change.
internal static class AgentHookCommands
{
    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var group = new System.CommandLine.Command("agent-hook", "Handlers an agent CLI runs from its own hooks.");
        var resume = new System.CommandLine.Command("resume",
            "Pin the pane's restore command to the agent session that started, and clear it when you leave the agent.\n\n"
            + "An agent CLI runs this from its session start and session end hooks, with the hook payload on standard "
            + "input. It reads its pane from AGTERM_SESSION_ID, AGTERM_PANE_ID and AGTERM_PANE, and does nothing "
            + "outside agterm-win. End clears the pin only when it still names this agent session. It always exits 0, "
            + "so a failure never stops the agent, and reports a failure on standard error.");
        var agent = new Argument<string>("agent")
        {
            Description = "The agent CLI that runs the hook: " + string.Join(", ", AgentResumeProfile.All.Select(profile => profile.Name)) + ".",
        };
        agent.AcceptOnlyFromAmong(AgentResumeProfile.All.Select(profile => profile.Name).ToArray());
        var hookEvent = new Argument<string>("event")
        {
            Description = "start (the session started or changed id) or end (the session ended).",
        };
        hookEvent.AcceptOnlyFromAmong("start", "end");
        resume.Arguments.Add(agent);
        resume.Arguments.Add(hookEvent);
        bindings.RegisterLocal(resume, result => new Resume
        {
            Agent = AgentResumeProfile.Find(result.GetValue(agent)!)!,
            Event = result.GetValue(hookEvent) == "start" ? AgentHookEvent.Start : AgentHookEvent.End,
        });
        group.Subcommands.Add(resume);
        return group;
    }

    internal sealed class Resume : ILocalCommand
    {
        public required AgentResumeProfile Agent { get; init; }

        public required AgentHookEvent Event { get; init; }

        public int Execute(CliEnvironment env)
        {
            if (env.GetEnv(SurfaceEnvironment.SessionId) is not { Length: > 0 } session) return 0;
            if (Agent.IgnoredWhenSet is { } marker && env.GetEnv(marker) is { Length: > 0 }) return 0;
            var decision = AgentResumeHook.Decide(Agent, Event, env.In.ReadToEnd());
            if (decision.Verb == AgentResumeVerb.Ignore) return 0;

            var pipe = new BasicOptions { Socket = NonEmpty(env.GetEnv(SurfaceEnvironment.Socket)) }.PipeName(env.GetEnv);
            var pane = NonEmpty(env.GetEnv(SurfaceEnvironment.Pane));
            var paneId = NonEmpty(env.GetEnv(SurfaceEnvironment.PaneId));
            try
            {
                if (decision.Verb == AgentResumeVerb.ClearIfPinned)
                {
                    var current = PaneRestoreCommand(env, pipe, session, paneId, pane);
                    if (!current.Found || (current.RestoreCommand != decision.Command &&
                        (decision.LegacyCommand is null || current.RestoreCommand != decision.LegacyCommand))) return 0;
                }
                var response = env.Send(pipe, new ControlRequest
                {
                    Cmd = WireCommand.SessionRestore,
                    Target = session,
                    Args = new ControlArgs
                    {
                        Mode = decision.Verb == AgentResumeVerb.Pin ? "set" : "clear",
                        Command = decision.Verb == AgentResumeVerb.Pin ? decision.Command : null,
                        Pane = pane,
                        PaneId = paneId,
                    },
                }).Response;
                if (!response.Ok) env.Error.WriteLine("error: " + response.Error);
            }
            catch (PipeClientException e)
            {
                env.Error.WriteLine("error: " + e.Message);
            }
            return 0;
        }

        // tree answers for one window: the hook's own window first, then the frontmost one, for a
        // window id that no longer resolves.
        private static AgentResumePane PaneRestoreCommand(CliEnvironment env, string pipe, string session, string? paneId, string? pane)
        {
            var windows = new List<string?>();
            if (NonEmpty(env.GetEnv(SurfaceEnvironment.WindowId)) is { } window) windows.Add(window);
            windows.Add(null);
            foreach (var candidate in windows)
            {
                var response = env.Send(pipe, new ControlRequest
                {
                    Cmd = WireCommand.Tree,
                    Args = candidate is null ? null : new ControlArgs { Window = candidate },
                }).Response;
                if (!response.Ok || response.Result?.Tree is not { } tree) continue;
                var found = AgentResumeHook.PaneRestoreCommand(tree, session, paneId, pane);
                if (found.Found) return found;
            }
            return new AgentResumePane(false, null);
        }

        private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    }
}
