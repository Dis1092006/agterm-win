using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli;

/// <summary>A terminal or pending outcome read from a poll: the bare result line, whether to keep
/// polling, and the exit code.</summary>
internal sealed record ModalOutcome(string Json, bool Pending, int ExitCode);

/// <summary>One family of upstream's ModalCommandRunner: its name in error lines, its poll and
/// cancel commands, and how to read its result from a reply.</summary>
internal sealed record ModalFamily(string Name, WireCommand ResultCommand, WireCommand CancelCommand,
                                   Func<ControlResult, ModalOutcome?> Read);

internal static class ModalFamilies
{
    public static readonly ModalFamily Pick = new("pick", WireCommand.PickResult, WireCommand.PickCancel,
        result => result.Pick is { } pick
            ? new ModalOutcome(ControlJson.Serialize(pick), pick.Result == ControlPickOutcome.Pending, PickRunner.ExitCode(pick.Result))
            : null);

    public static readonly ModalFamily Ask = new("ask", WireCommand.AskResult, WireCommand.AskCancel,
        result => result.Ask is { } ask
            ? new ModalOutcome(ControlJson.Serialize(ask), ask.Result == ControlAskOutcome.Pending, AskRunner.ExitCode(ask.Result))
            : null);
}

/// <summary>Port of upstream's ModalCommandRunner: open, then poll the result by id until a
/// terminal outcome. The server never blocks, so other clients keep working while a user decides.</summary>
internal static class ModalRunner
{
    /// <summary>pendingPolls is the 0-based count of pending polls so far (upstream's helper is 1-based).</summary>
    public static TimeSpan PollDelay(int pendingPolls) =>
        pendingPolls < 10 ? TimeSpan.FromSeconds(0.1) : TimeSpan.FromSeconds(0.5);

    public static int RunOpen(CliEnvironment env, IConnectionOptions options, ModalFamily family, ControlRequest open, bool noBlock)
    {
        var pipe = options.PipeName(env.GetEnv);
        var opened = env.Send(pipe, open);
        if (!opened.Ok)
        {
            ClientOutput.PrintResponse(env, opened, options.Json);
            return 1;
        }
        if (opened.Result?.Id is not { } id)
        {
            env.Error.WriteLine($"error: {family.Name}.open result missing id");
            return 1;
        }
        if (noBlock)
        {
            // only the id, even when ask.open also echoed a pane
            env.Out.WriteLine(ControlJson.Serialize(new ControlResult { Id = id }));
            return 0;
        }

        for (var pendingPolls = 0; ; pendingPolls++)
        {
            ControlResponse polled;
            try
            {
                // no window: the id is global, so a frontmost change cannot make the poll miss it
                polled = env.Send(pipe, new ControlRequest { Cmd = family.ResultCommand, Target = id });
            }
            catch (AgTerm.ControlServer.PipeClientException)
            {
                CancelBestEffort(env, pipe, family, id);
                throw;
            }
            if (!polled.Ok)
            {
                // the server no longer has the dialog, so there is nothing to cancel
                ClientOutput.PrintResponse(env, polled, options.Json);
                return 1;
            }
            if (polled.Result is not { } payload || family.Read(payload) is not { } outcome)
            {
                env.Error.WriteLine($"error: {family.Name}.result missing result");
                CancelBestEffort(env, pipe, family, id);
                return 1;
            }
            if (!outcome.Pending)
            {
                env.Out.WriteLine(outcome.Json);
                return outcome.ExitCode;
            }
            env.Sleep(PollDelay(pendingPolls));
        }
    }

    public static int PrintResult(CliEnvironment env, IConnectionOptions options, ModalFamily family, ControlRequest request)
    {
        var response = env.Send(options.PipeName(env.GetEnv), request);
        if (!response.Ok)
        {
            ClientOutput.PrintResponse(env, response, options.Json);
            return 1;
        }
        if (response.Result is not { } payload || family.Read(payload) is not { } outcome)
        {
            env.Error.WriteLine($"error: {family.Name}.result missing result");
            return 1;
        }
        env.Out.WriteLine(outcome.Json);
        return outcome.ExitCode;
    }

    private static void CancelBestEffort(CliEnvironment env, string pipe, ModalFamily family, string id)
    {
        try
        {
            env.Send(pipe, new ControlRequest { Cmd = family.CancelCommand, Target = id });
        }
        catch (Exception)
        {
            // the transport already failed once; the caller rethrows the original failure
        }
    }
}
