using System.CommandLine;
using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli;

// Port of agtermctlKit MessageCommands.swift (upstream b0839692, #847): `agtermctl msg`, messages
// addressed to a pane, read and acknowledged by that pane. The Swift MessageReadDependencies map
// onto CliEnvironment (Send / Sleep / Now) and SessionWaitWatchdog onto CliEnvironment.StartWatchdog.

/// <summary>What one msg reply prints and whether the command fails.</summary>
internal sealed record MessageOutput(string Stdout = "", string Stderr = "", bool Failed = false);

/// <summary>The reply a read ended on, or the --timeout deadline passing first.</summary>
internal readonly record struct MessageReadOutcome(ControlReply? Reply, bool TimedOut)
{
    public static readonly MessageReadOutcome Expired = new(null, true);
}

/// <summary>The pane a msg command acts as: --as, else the pane's own environment.</summary>
internal static class MessageIdentity
{
    public const string OptionDescription =
        "Act as this pane, SESSION:PANE-ID (defaults to AGTERM_SESSION_ID:AGTERM_PANE_ID).";

    /// <summary>Null only when neither variable is present, which makes a send anonymous. Any other
    /// combination short of two non-empty values is an error, so a broken environment never sends as
    /// nobody.</summary>
    public static string? Resolve(string? identity, Func<string, string?> env)
    {
        if (identity is not null) return identity;
        var session = env("AGTERM_SESSION_ID");
        var pane = env("AGTERM_PANE_ID");
        if (session is null && pane is null) return null;
        if (string.IsNullOrEmpty(session) || string.IsNullOrEmpty(pane))
        {
            throw new CliValidationException("incomplete pane identity: AGTERM_SESSION_ID and AGTERM_PANE_ID must both be set; "
                + "pass --as SESSION:PANE-ID");
        }
        return $"{session}:{pane}";
    }

    public static Option<string?> NewOption() => new("--as") { Description = OptionDescription };
}

internal static class MessageCommands
{
    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var msg = new System.CommandLine.Command("msg", "Send, read and acknowledge messages between panes.");
        msg.Subcommands.Add(BuildSend(bindings));
        msg.Subcommands.Add(BuildRead(bindings));
        msg.Subcommands.Add(BuildAck(bindings));
        return msg;
    }

    /// <summary>Writes one reply's output; the exit code is 1 when the output says the command failed.</summary>
    private static int Emit(CliEnvironment env, MessageOutput output)
    {
        if (output.Stdout.Length > 0) env.Out.Write(output.Stdout);
        if (output.Stderr.Length > 0) env.Error.Write(output.Stderr);
        return output.Failed ? 1 : 0;
    }

    /// <summary>The output of a reply whose ok is false.</summary>
    private static MessageOutput Refused(ControlReply reply, bool json) => json
        ? new MessageOutput(Stdout: reply.Line + "\n", Failed: true)
        : new MessageOutput(Stderr: $"error: {reply.Response.Error ?? "unknown error"}\n", Failed: true);

    internal sealed record Send : IRequestCommand
    {
        public string? Text { get; init; }
        public bool Stdin { get; init; }
        public IReadOnlyList<string> Recipients { get; init; } = Array.Empty<string>();
        public bool All { get; init; }
        public string? Workspace { get; init; }
        public string? Identity { get; init; }
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        /// <summary>Stdin source for --stdin; injectable for tests.</summary>
        public Func<Stream> StdinStream { get; init; } = Console.OpenStandardInput;

        public void Validate()
        {
            if ((Text is not null) == Stdin)
                throw new CliValidationException("provide the body as TEXT or --stdin, exactly one");
        }

        public ControlRequest MakeRequest() => MakeRequest(Body(), Environment.GetEnvironmentVariable);

        public ControlRequest MakeRequest(string body, Func<string, string?> env) => new()
        {
            Cmd = WireCommand.MsgSend,
            Args = new ControlArgs
            {
                Body = body,
                Recipients = Recipients.Count == 0 ? null : Recipients.ToArray(),
                All = All ? true : null,
                Workspace = Workspace,
                Identity = MessageIdentity.Resolve(Identity, env),
            },
        };

        public int Execute(CliEnvironment env)
        {
            var request = MakeRequest(Body(), env.GetEnv);
            return Emit(env, Output(env.Send(Options.PipeName(env.GetEnv), request), Basic.Json));
        }

        private string Body() => Text ?? TypedStdin.Decode(TypedStdin.ReadAll(StdinStream()));

        public static MessageOutput Output(ControlReply reply, bool json)
        {
            var response = reply.Response;
            if (!response.Ok) return Refused(reply, json);
            var rejected = response.Result?.Rejected ?? Array.Empty<MessageRejection>();
            if (json) return new MessageOutput(Stdout: reply.Line + "\n", Failed: rejected.Count > 0);
            return new MessageOutput(
                Stdout: (response.Result?.MessageId ?? "") + "\n",
                Stderr: string.Concat(rejected.Select(r => $"rejected {r.Address}: {r.Reason.ToWire()}\n")),
                Failed: rejected.Count > 0);
        }
    }

    private static System.CommandLine.Command BuildSend(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("send",
            "Send a message to one or more panes. A recipient is SESSION:PANE, where PANE is a pane id or "
            + "left, right or scratch. --all reaches every live pane in every open window except the sender. "
            + "Prints the message id. Exit 1 when any recipient was rejected; the copies that were delivered "
            + "stay queued.");
        var text = new Argument<string?>("text")
        {
            Description = "Message body; use --stdin to read it instead.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var stdin = new Option<bool>("--stdin") { Description = "Read the body from stdin." };
        var to = new Option<string[]>("--to")
        {
            Description = "Recipient SESSION:PANE; repeat for several.",
            DefaultValueFactory = _ => Array.Empty<string>(),
        };
        var all = new Option<bool>("--all") { Description = "Send to every live pane except the sender." };
        var workspace = new Option<string?>("--workspace") { Description = "With --all, only the panes of this workspace." };
        var identity = MessageIdentity.NewOption();
        var options = new BasicOptions.Binder();
        cmd.Arguments.Add(text);
        cmd.Options.Add(stdin);
        cmd.Options.Add(to);
        cmd.Options.Add(all);
        cmd.Options.Add(workspace);
        cmd.Options.Add(identity);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Send
        {
            Text = result.GetValue(text),
            Stdin = result.GetValue(stdin),
            Recipients = result.GetValue(to) ?? Array.Empty<string>(),
            All = result.GetValue(all),
            Workspace = result.GetValue(workspace),
            Identity = result.GetValue(identity),
            Basic = options.Read(result),
        });
        return cmd;
    }

    internal sealed record Read : IRequestCommand
    {
        public const int TimeoutExitCode = 124;
        public static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(0.25);

        public bool Ack { get; init; }
        public bool Wait { get; init; }
        public double? Timeout { get; init; }
        public string? Identity { get; init; }
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public void Validate()
        {
            if (Timeout is not { } timeout) return;
            if (!Wait) throw new CliValidationException("--timeout needs --wait");
            if (!(double.IsFinite(timeout) && timeout > 0))
                throw new CliValidationException("--timeout must be a positive number of seconds");
        }

        public ControlRequest MakeRequest() => MakeRequest(Environment.GetEnvironmentVariable);

        public ControlRequest MakeRequest(Func<string, string?> env) => new()
        {
            Cmd = WireCommand.MsgRead,
            Args = new ControlArgs { Identity = MessageIdentity.Resolve(Identity, env), Ack = Ack ? true : null },
        };

        public int Execute(CliEnvironment env)
        {
            var request = MakeRequest(env.GetEnv);
            using var watchdog = Timeout is { } seconds
                ? env.StartWatchdog(seconds, SessionWaitCommands.WaitCommand.OnWatchdogFired(env.Error, Environment.Exit))
                : null;
            var outcome = ReadMail(request, env);
            if (outcome.Reply is { } reply) return Emit(env, Output(reply));
            env.Error.Write("timed out\n");
            return TimeoutExitCode;
        }

        /// <summary>Sends the read itself on every poll, --ack included: an empty read removes nothing,
        /// and a separate probe would leave a gap in which another reader could drain the mailbox.</summary>
        public MessageReadOutcome ReadMail(ControlRequest request, CliEnvironment env)
        {
            var pipeName = Options.PipeName(env.GetEnv);
            double? deadline = Timeout is { } timeout ? env.Now() + timeout : null;
            while (true)
            {
                var reply = env.Send(pipeName, request);
                var result = reply.Response.Result;
                var keepWaiting = Wait && reply.Response.Ok && (result?.Messages is not { Count: > 0 })
                    && (!Ack || AckConfirmed(result));
                if (!keepWaiting) return new MessageReadOutcome(reply, false);
                if (deadline is not { } limit)
                {
                    env.Sleep(PollDelay);
                    continue;
                }
                var left = limit - env.Now();
                if (left <= 0) return MessageReadOutcome.Expired;
                env.Sleep(left < PollDelay.TotalSeconds ? TimeSpan.FromSeconds(left) : PollDelay);
            }
        }

        public MessageOutput Output(ControlReply reply)
        {
            var response = reply.Response;
            if (!response.Ok) return Refused(reply, Basic.Json);
            var result = response.Result;
            if (Ack && !AckConfirmed(result))
            {
                return new MessageOutput(
                    Stderr: "error: the app did not confirm which messages it removed; nothing was printed\n", Failed: true);
            }
            if (Basic.Json) return new MessageOutput(Stdout: reply.Line + "\n");
            var remaining = result?.Remaining ?? 0;
            return new MessageOutput(
                Stdout: string.Concat((result?.Messages ?? Array.Empty<BusMessage>()).Select(Render)),
                Stderr: remaining > 0 ? $"{remaining} more pending\n" : "");
        }

        /// <summary>Holds when acked names exactly the returned messages, so an app that ignored the flag
        /// cannot pass for one that removed them.</summary>
        public static bool AckConfirmed(ControlResult? result)
        {
            if (result?.Acked is not { } acked) return false;
            var ids = (result.Messages ?? Array.Empty<BusMessage>()).Select(m => Upper(m.Id));
            return acked.SequenceEqual(ids);
        }

        public static string Render(BusMessage message)
        {
            var body = message.Body.EndsWith('\n') ? message.Body : message.Body + "\n";
            return $"{Upper(message.Id)} from {message.From ?? "outside agterm"}\n{body}";
        }

        private static string Upper(Guid id) => id.ToString("D").ToUpperInvariant();
    }

    private static System.CommandLine.Command BuildRead(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("read",
            "Read the oldest messages in this pane's mailbox. Prints each message as a line \"ID from SENDER\" "
            + "followed by its body. Reading does not remove anything; acknowledge with msg ack, or pass --ack "
            + "to remove the printed batch in the same step. --wait repeats the read until a message arrives; "
            + $"--timeout ends it with exit {Read.TimeoutExitCode}.");
        var ack = new Option<bool>("--ack") { Description = "Remove the messages this read returns." };
        var wait = new Option<bool>("--wait") { Description = "Wait until a message arrives." };
        var timeout = CliOptions.LenientDouble("--timeout", $"With --wait, seconds before exiting {Read.TimeoutExitCode}.");
        var identity = MessageIdentity.NewOption();
        var options = new BasicOptions.Binder();
        cmd.Options.Add(ack);
        cmd.Options.Add(wait);
        cmd.Options.Add(timeout);
        cmd.Options.Add(identity);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Read
        {
            Ack = result.GetValue(ack),
            Wait = result.GetValue(wait),
            Timeout = result.GetValue(timeout),
            Identity = result.GetValue(identity),
            Basic = options.Read(result),
        });
        return cmd;
    }

    internal sealed record Ack : IRequestCommand
    {
        public IReadOnlyList<string> Ids { get; init; } = Array.Empty<string>();
        public string? Identity { get; init; }
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => MakeRequest(Environment.GetEnvironmentVariable);

        public ControlRequest MakeRequest(Func<string, string?> env) => new()
        {
            Cmd = WireCommand.MsgAck,
            Args = new ControlArgs { Identity = MessageIdentity.Resolve(Identity, env), Ids = Ids.ToArray() },
        };

        public int Execute(CliEnvironment env)
        {
            var request = MakeRequest(env.GetEnv);
            return Emit(env, Output(env.Send(Options.PipeName(env.GetEnv), request), Basic.Json));
        }

        public static MessageOutput Output(ControlReply reply, bool json)
        {
            var response = reply.Response;
            if (!response.Ok) return Refused(reply, json);
            if (json) return new MessageOutput(Stdout: reply.Line + "\n");
            return new MessageOutput(Stdout: $"acked {response.Result?.Acked?.Count ?? 0}\n");
        }
    }

    private static System.CommandLine.Command BuildAck(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("ack",
            "Remove messages from this pane's mailbox by id. Unknown or already removed ids are not an error. "
            + "Prints how many were removed.");
        var ids = new Argument<string[]>("ids")
        {
            Description = "Message ids from msg read.",
            Arity = ArgumentArity.OneOrMore,
        };
        var identity = MessageIdentity.NewOption();
        var options = new BasicOptions.Binder();
        cmd.Arguments.Add(ids);
        cmd.Options.Add(identity);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Ack
        {
            Ids = result.GetValue(ids) ?? Array.Empty<string>(),
            Identity = result.GetValue(identity),
            Basic = options.Read(result),
        });
        return cmd;
    }
}
