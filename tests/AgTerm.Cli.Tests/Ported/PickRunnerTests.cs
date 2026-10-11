using AgTerm.Cli;
using AgTerm.ControlServer;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests.Ported;

// Ported from agtermctlKitTests SocketClientTests.swift pick cases (upstream ed831ab).
public class PickRunnerTests
{
    private sealed class Script
    {
        public readonly List<ControlRequest> Sent = new();
        public readonly List<TimeSpan> Sleeps = new();
        public readonly StringWriter Out = new();
        public readonly StringWriter Error = new();
        private readonly Queue<Func<ControlRequest, ControlResponse>> _replies = new();

        public Script Reply(ControlResponse response) { _replies.Enqueue(_ => response); return this; }
        public Script Throw() { _replies.Enqueue(_ => throw new PipeClientException("pipe gone")); return this; }

        public CliEnvironment Env() => new()
        {
            Out = Out,
            Error = Error,
            GetEnv = _ => null,
            Sleep = Sleeps.Add,
            Send = (_, request) => { Sent.Add(request); return _replies.Count > 0 ? _replies.Dequeue()(request) : new ControlResponse { Ok = true }; },
        };
    }

    private static readonly ClientOptions Options = new() { Basic = new BasicOptions { Socket = "isolated" } };
    private static ControlRequest Open => new() { Cmd = WireCommand.PickOpen, Args = new ControlArgs { Items = Array.Empty<ControlPickItem>(), AllowCustom = true } };
    private static ControlResponse Opened(string id) => new() { Ok = true, Result = new ControlResult { Id = id } };
    private static ControlResponse Polled(ControlPickResult result) => new() { Ok = true, Result = new ControlResult { Pick = result } };

    [Fact]
    public void ExitCodeMapsEveryOutcome()
    {
        Assert.Equal(1, PickRunner.ExitCode(ControlPickOutcome.Pending));
        Assert.Equal(0, PickRunner.ExitCode(ControlPickOutcome.Picked));
        Assert.Equal(0, PickRunner.ExitCode(ControlPickOutcome.Custom));
        Assert.Equal(2, PickRunner.ExitCode(ControlPickOutcome.Cancelled));
    }

    [Fact]
    public void PollBackoffUsesTenFastSleepsThenSlowSleeps()
    {
        Assert.All(Enumerable.Range(0, 10), n => Assert.Equal(TimeSpan.FromSeconds(0.1), PickRunner.PollDelay(n)));
        Assert.Equal(TimeSpan.FromSeconds(0.5), PickRunner.PollDelay(10));
        Assert.Equal(TimeSpan.FromSeconds(0.5), PickRunner.PollDelay(50));
    }

    [Fact]
    public void NoBlockPrintsTheIdWithoutPolling()
    {
        var script = new Script().Reply(Opened("P"));
        Assert.Equal(0, PickRunner.RunOpen(script.Env(), Options, Open, noBlock: true));
        Assert.Equal("{\"id\":\"P\"}" + Environment.NewLine, script.Out.ToString());
        Assert.Single(script.Sent);
    }

    [Fact]
    public void BlockPollsByIdWithoutWindowPrintsTheResultAndExitsZero()
    {
        var script = new Script().Reply(Opened("P"));
        for (var i = 0; i < 11; i++) script.Reply(Polled(ControlPickResult.Pending()));
        script.Reply(Polled(ControlPickResult.Picked("a", "A", 0)));

        Assert.Equal(0, PickRunner.RunOpen(script.Env(), Options, Open, noBlock: false));

        Assert.Equal("{\"result\":\"picked\",\"id\":\"a\",\"label\":\"A\",\"index\":0}" + Environment.NewLine, script.Out.ToString());
        Assert.All(script.Sent.Skip(1), poll =>
        {
            Assert.Equal(WireCommand.PickResult, poll.Cmd);
            Assert.Equal("P", poll.Target);
            Assert.Null(poll.Args);
        });
        Assert.Equal(Enumerable.Repeat(TimeSpan.FromSeconds(0.1), 10).Append(TimeSpan.FromSeconds(0.5)), script.Sleeps);
    }

    [Fact]
    public void BlockExitsTwoAfterPrintingACancelledResult()
    {
        var script = new Script().Reply(Opened("P")).Reply(Polled(ControlPickResult.Cancelled()));
        Assert.Equal(2, PickRunner.RunOpen(script.Env(), Options, Open, noBlock: false));
        Assert.Equal("{\"result\":\"cancelled\"}" + Environment.NewLine, script.Out.ToString());
    }

    [Fact]
    public void OpenErrorAndMissingIdFailWithOne()
    {
        var refused = new Script().Reply(new ControlResponse { Ok = false, Error = "pick already pending" });
        Assert.Equal(1, PickRunner.RunOpen(refused.Env(), Options, Open, noBlock: false));
        Assert.Equal("error: pick already pending" + Environment.NewLine, refused.Error.ToString());

        var noId = new Script().Reply(new ControlResponse { Ok = true });
        Assert.Equal(1, PickRunner.RunOpen(noId.Env(), Options, Open, noBlock: false));
        Assert.Equal("error: pick.open result missing id" + Environment.NewLine, noId.Error.ToString());
    }

    [Fact]
    public void MalformedPollResultCancelsThePickItCanNoLongerWaitOn()
    {
        var script = new Script().Reply(Opened("P")).Reply(new ControlResponse { Ok = true });
        Assert.Equal(1, PickRunner.RunOpen(script.Env(), Options, Open, noBlock: false));
        Assert.Equal("error: pick.result missing result" + Environment.NewLine, script.Error.ToString());
        Assert.Equal(WireCommand.PickCancel, script.Sent.Last().Cmd);
        Assert.Equal("P", script.Sent.Last().Target);
    }

    [Fact]
    public void ServerErrorOnPollDoesNotCancel()
    {
        var script = new Script().Reply(Opened("P")).Reply(new ControlResponse { Ok = false, Error = "unknown pick: P" });
        Assert.Equal(1, PickRunner.RunOpen(script.Env(), Options, Open, noBlock: false));
        Assert.DoesNotContain(script.Sent, request => request.Cmd == WireCommand.PickCancel);
    }

    [Fact]
    public void ThrowingPollCancelsThenRethrows()
    {
        var script = new Script().Reply(Opened("P")).Throw();
        Assert.Throws<PipeClientException>(() => PickRunner.RunOpen(script.Env(), Options, Open, noBlock: false));
        Assert.Equal(WireCommand.PickCancel, script.Sent.Last().Cmd);
    }

    [Fact]
    public void JsonServerErrorGoesToStdout()
    {
        var json = new ClientOptions { Basic = new BasicOptions { Socket = "isolated", Json = true } };
        var script = new Script().Reply(new ControlResponse { Ok = false, Error = "no open window" });
        Assert.Equal(1, PickRunner.RunOpen(script.Env(), json, Open, noBlock: false));
        Assert.Contains("\"error\":\"no open window\"", script.Out.ToString());
    }

    [Fact]
    public void ResultPrintsPendingAndExitsOne()
    {
        var script = new Script().Reply(Polled(ControlPickResult.Pending()));
        var request = new ControlRequest { Cmd = WireCommand.PickResult, Target = "P" };
        Assert.Equal(1, PickRunner.PrintResult(script.Env(), Options, request));
        Assert.Equal("{\"result\":\"pending\"}" + Environment.NewLine, script.Out.ToString());
    }
}
