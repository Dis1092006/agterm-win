using System.CommandLine;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli;

// Port of agtermctlKit Session+Wait.swift (upstream fff13ef2, #765). A CLI command built on the
// event stream and tree, not a wire command. The Swift SessionWaitDependencies map onto
// CliEnvironment (Send / Sleep / Now); SessionWaitWatchdog maps onto CliEnvironment.StartWatchdog.
// The refused-connect retry of the Swift poll is not ported: the port's events poller does not
// retry either, and any failed read ends the wait.

/// <summary>The result of a wait: a status in the --until set, the session leaving the open tree, or
/// the deadline passing first.</summary>
internal readonly record struct SessionWaitOutcome(SessionWaitOutcome.OutcomeKind Kind, Guid Id, AgentStatus Status)
{
    public enum OutcomeKind { Matched, Closed, TimedOut }

    public static readonly SessionWaitOutcome Closed = new(OutcomeKind.Closed, default, default);

    public static readonly SessionWaitOutcome TimedOut = new(OutcomeKind.TimedOut, default, default);

    public static SessionWaitOutcome Matched(Guid id, AgentStatus status) => new(OutcomeKind.Matched, id, status);
}

/// <summary>Ends a wait at its deadline even while a pipe call is blocked, which the poll loop's own
/// deadline check cannot reach: the client has no receive timeout and the app may never answer. A
/// dedicated thread, so a starved thread pool cannot delay the handler past the deadline.</summary>
internal static class CliEnvironmentWatchdog
{
    public static IDisposable Start(double seconds, Action fire)
    {
        var cancelled = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            if (!cancelled.Wait(TimeSpan.FromSeconds(seconds))) fire();
        })
        {
            IsBackground = true,
            Name = "agtermctl.session-wait.watchdog",
        };
        thread.Start();
        return new Cancel(cancelled);
    }

    private sealed class Cancel : IDisposable
    {
        private readonly ManualResetEventSlim _cancelled;

        public Cancel(ManualResetEventSlim cancelled) => _cancelled = cancelled;

        public void Dispose() => _cancelled.Set();
    }
}

internal static class SessionWaitCommands
{
    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("wait", "Block until a session's agent status is one of the given values.");
        var until = new Option<string[]>("--until")
        {
            Description = "Status to wait for (idle, active, completed, blocked); repeat for several.",
            DefaultValueFactory = _ => Array.Empty<string>(),
        };
        var timeout = CliOptions.LenientDouble("--timeout",
            $"Seconds to wait before exiting {WaitCommand.TimeoutExitCode}; omit to wait indefinitely.");
        var target = new TargetOptions.Binder();
        var options = new BasicOptions.Binder();
        cmd.Options.Add(until);
        cmd.Options.Add(timeout);
        target.AddTo(cmd);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new WaitCommand
        {
            Until = result.GetValue(until) ?? Array.Empty<string>(),
            Timeout = result.GetValue(timeout),
            Target = target.Read(result),
            Basic = options.Read(result),
        });
        return cmd;
    }

    internal sealed class WaitCommand : IRequestCommand
    {
        public const int TimeoutExitCode = 124;
        public const int ClosedExitCode = 2;
        public static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(0.25);

        private static readonly ControlEventKind[] StreamKinds = { ControlEventKind.Status, ControlEventKind.SessionClosed };

        public IReadOnlyList<string> Until { get; init; } = Array.Empty<string>();
        public double? Timeout { get; init; }
        public required TargetOptions Target { get; init; }
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        private HashSet<AgentStatus> Statuses =>
            Until.Select(AgentStatuses.FromWire).OfType<AgentStatus>().ToHashSet();

        public void Validate()
        {
            if (Until.Count == 0)
                throw new CliValidationException("provide at least one --until status");
            foreach (var raw in Until)
            {
                if (AgentStatuses.FromWire(raw) is null)
                    throw new CliValidationException($"invalid status: {raw} (expected idle, active, completed or blocked)");
            }
            if (Timeout is { } timeout && !(double.IsFinite(timeout) && timeout > 0))
                throw new CliValidationException("--timeout must be a positive number of seconds");
        }

        /// <summary>The events.read request the wait opens with; the later reads carry the cursor.</summary>
        public ControlRequest MakeRequest() => NewStream().MakeRequest();

        public int Execute(CliEnvironment env)
        {
            using var watchdog = Timeout is { } seconds
                ? env.StartWatchdog(seconds, OnWatchdogFired(env.Error, Environment.Exit))
                : null;
            var outcome = Wait(env);
            switch (outcome.Kind)
            {
                case SessionWaitOutcome.OutcomeKind.Matched:
                    env.Out.WriteLine(Output(outcome.Id, outcome.Status));
                    return 0;
                case SessionWaitOutcome.OutcomeKind.Closed:
                    env.Error.WriteLine("session closed");
                    return ClosedExitCode;
                default:
                    env.Error.WriteLine("timed out");
                    return TimeoutExitCode;
            }
        }

        public static Action OnWatchdogFired(TextWriter error, Action<int> exit) => () =>
        {
            error.WriteLine("timed out");
            exit(TimeoutExitCode);
        };

        private static EventStreamState NewStream() =>
            new(cursor: null, kinds: new HashSet<ControlEventKind>(StreamKinds), limit: null);

        /// <summary>Anchor the event cursor, then read the current status, then replay events from the
        /// anchor, so a status set between the two reads is still seen.</summary>
        public SessionWaitOutcome Wait(CliEnvironment env)
        {
            var pipeName = Options.PipeName(env.GetEnv);
            var statuses = Statuses;
            var deadline = Timeout is { } timeout ? env.Now() + timeout : (double?)null;
            var stream = NewStream();
            stream.Consume(env.Send(pipeName, stream.MakeRequest()).Response);
            var (id, current) = ResolveSession(env, pipeName);
            if (statuses.Contains(current)) return SessionWaitOutcome.Matched(id, current);

            void Pause(TimeSpan delay)
            {
                if (deadline is not { } limit)
                {
                    env.Sleep(delay);
                    return;
                }
                var remaining = Math.Max(0, limit - env.Now());
                env.Sleep(delay.TotalSeconds <= remaining ? delay : TimeSpan.FromSeconds(remaining));
            }

            while (true)
            {
                if (deadline is { } limit && env.Now() >= limit) return SessionWaitOutcome.TimedOut;
                ControlResponse response;
                try
                {
                    response = env.Send(pipeName, stream.MakeRequest()).Response;
                }
                catch (Exception error) when (stream.RetryDelay(error, env.Now()) is { } delay)
                {
                    Pause(TimeSpan.FromSeconds(delay));
                    continue;
                }
                var events = stream.Consume(response);
                foreach (var @event in events)
                {
                    if (!Guid.TryParse(@event.Session, out var eventSession) || eventSession != id) continue;
                    if (@event.Kind == ControlEventKind.SessionClosed) return SessionWaitOutcome.Closed;
                    if (@event.Payload.Status is { } raw && AgentStatuses.FromWire(raw) is { } status
                        && statuses.Contains(status))
                    {
                        return SessionWaitOutcome.Matched(id, status);
                    }
                }
                if (events.Length == 0) Pause(PollDelay);
            }
        }

        /// <summary>Resolve --target the way the server does, across every open window's tree: "active"
        /// is the frontmost window's selected session, an id or unique prefix matches in any open
        /// window. An absent status reads as idle.</summary>
        private (Guid Id, AgentStatus Status) ResolveSession(CliEnvironment env, string pipeName)
        {
            var list = env.Send(pipeName, new ControlRequest { Cmd = WireCommand.WindowList }).Response;
            if (!list.Ok || list.Result?.Windows is not { } windows)
                throw new CliValidationException(list.Error ?? "window.list response missing windows");
            var sessions = new List<ControlSessionNode>();
            Guid? active = null;
            foreach (var window in windows.Where(window => window.Open))
            {
                var reply = env.Send(pipeName, new ControlRequest
                {
                    Cmd = WireCommand.Tree,
                    Args = new ControlArgs { Window = window.Id },
                }).Response;
                if (!reply.Ok || reply.Result?.Tree is not { } tree)
                    throw new CliValidationException(reply.Error ?? "tree response missing tree");
                var nodes = tree.Workspaces.SelectMany(workspace => workspace.Sessions).ToArray();
                sessions.AddRange(nodes);
                if (window.Active)
                    active = nodes.FirstOrDefault(node => node.Active) is { } node && Guid.TryParse(node.Id, out var id) ? id : null;
            }
            var candidates = sessions.Select(node => Guid.TryParse(node.Id, out var id) ? id : (Guid?)null)
                .OfType<Guid>().ToArray();
            var resolution = ControlResolve.Resolve(Target.Target, candidates, active);
            if (resolution.Kind != TargetResolution.Outcome.Resolved
                || sessions.FirstOrDefault(node => Guid.TryParse(node.Id, out var id) && id == resolution.Id) is not { } match)
            {
                throw new CliValidationException(ControlResolve.ErrorMessage("session", Target.Target, resolution));
            }
            var status = match.Status is { } raw ? AgentStatuses.FromWire(raw) ?? AgentStatus.Idle : AgentStatus.Idle;
            return (resolution.Id, status);
        }

        public string Output(Guid id, AgentStatus status)
        {
            var wire = status.ToString().ToLowerInvariant();
            if (!Basic.Json) return wire;
            return $"{{\"id\":\"{id.ToString("D").ToUpperInvariant()}\",\"status\":\"{wire}\"}}";
        }
    }
}
