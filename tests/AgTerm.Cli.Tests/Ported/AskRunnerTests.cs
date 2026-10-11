using AgTerm.Cli;
using AgTerm.ControlServer;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests.Ported;

// Ported from agtermctlKitTests SocketClientTests.swift ask cases (upstream ed831ab).
public class AskRunnerTests
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
 
    private static ControlRequest Open => new()
    {
        Cmd = WireCommand.AskOpen,
        Args = new ControlArgs { Title = "Continue?", Buttons = new[] { new ControlAskButton { Id = "yes", Label = "Yes" } }, Window = "w" },
    };
    private static ControlResponse Opened(string id) => new() { Ok = true, Result = new ControlResult { Id = id } };
    private static ControlResponse Polled(ControlAskResult result) => new() { Ok = true, Result = new ControlResult { Ask = result } };

    [Theory]
    [InlineData(ControlAskOutcome.Pending, 1)]
    [InlineData(ControlAskOutcome.Answered, 0)]
    [InlineData(ControlAskOutcome.Cancelled, 2)]
    [InlineData(ControlAskOutcome.Escaped, 3)]
    public void ExitCodeMapsEveryOutcome(ControlAskOutcome outcome, int exit) => Assert.Equal(exit, AskRunner.ExitCode(outcome));

    [Fact]
    public void NoBlockPrintsOnlyTheIdWithoutPolling()
    {
        var script = new Script().Reply(new ControlResponse { Ok = true, Result = new ControlResult { Id = "ask-id", Pane = "right" } });
        Assert.Equal(0, AskRunner.RunOpen(script.Env(), Options, Open, noBlock: true));
        Assert.Equal("{\"id\":\"ask-id\"}" + Environment.NewLine, script.Out.ToString());
        Assert.Single(script.Sent);
        Assert.Empty(script.Sleeps);
    }

    [Fact]
    public void BlockingPollUsesTheGlobalIdAndBackoffAndPrintsOneAnswer()
    {
        var script = new Script().Reply(Opened("ask-id"));
        for (var i = 0; i < 11; i++) script.Reply(Polled(ControlAskResult.Pending()));
        script.Reply(Polled(ControlAskResult.Answered("yes", "Yes", 0)));

        Assert.Equal(0, AskRunner.RunOpen(script.Env(), Options, Open, noBlock: false));

        Assert.Equal("w", script.Sent[0].Args!.Window);
        Assert.All(script.Sent.Skip(1), poll =>
        {
            Assert.Equal(WireCommand.AskResult, poll.Cmd);
            Assert.Equal("ask-id", poll.Target);
            Assert.Null(poll.Args);
        });
        Assert.Equal(Enumerable.Repeat(TimeSpan.FromSeconds(0.1), 10).Append(TimeSpan.FromSeconds(0.5)), script.Sleeps);
        Assert.Equal("{\"result\":\"answered\",\"id\":\"yes\",\"label\":\"Yes\",\"index\":0}" + Environment.NewLine, script.Out.ToString());
    }

    [Theory]
    [InlineData(ControlAskOutcome.Cancelled, 2, "cancelled")]
    [InlineData(ControlAskOutcome.Escaped, 3, "escaped")]
    public void ADismissalPrintsBeforeItsExit(ControlAskOutcome outcome, int exit, string wire)
    {
        var script = new Script().Reply(Opened("ask-id")).Reply(Polled(new ControlAskResult { Result = outcome }));
        Assert.Equal(exit, AskRunner.RunOpen(script.Env(), Options, Open, noBlock: false));
        Assert.Equal($"{{\"result\":\"{wire}\"}}" + Environment.NewLine, script.Out.ToString());
    }

    [Fact]
    public void AMissingOpenIdFailsBeforePolling()
    {
        var script = new Script().Reply(new ControlResponse { Ok = true });
        Assert.Equal(1, AskRunner.RunOpen(script.Env(), Options, Open, noBlock: false));
        Assert.Single(script.Sent);
        Assert.Equal("error: ask.open result missing id" + Environment.NewLine, script.Error.ToString());
    }

    [Fact]
    public void AMalformedPollCancelsTheExactAsk()
    {
        var script = new Script().Reply(Opened("ask-id")).Reply(new ControlResponse { Ok = true });
        Assert.Equal(1, AskRunner.RunOpen(script.Env(), Options, Open, noBlock: false));
        Assert.Equal(new[] { WireCommand.AskOpen, WireCommand.AskResult, WireCommand.AskCancel }, script.Sent.Select(r => r.Cmd));
        Assert.Equal("ask-id", script.Sent[^1].Target);
        Assert.Null(script.Sent[^1].Args);
        Assert.Equal("error: ask.result missing result" + Environment.NewLine, script.Error.ToString());
    }

    [Fact]
    public void AFailedCancelKeepsTheTransportFailure()
    {
        var script = new Script().Reply(Opened("ask-id")).Throw().Throw();
        var error = Assert.Throws<PipeClientException>(() => AskRunner.RunOpen(script.Env(), Options, Open, noBlock: false));
        Assert.Equal("pipe gone", error.Message);
        Assert.Equal(new[] { WireCommand.AskOpen, WireCommand.AskResult, WireCommand.AskCancel }, script.Sent.Select(r => r.Cmd));
    }

    [Fact]
    public void AnUnknownAskAnswerDoesNotCancel()
    {
        var script = new Script().Reply(Opened("ask-id")).Reply(new ControlResponse { Ok = false, Error = "unknown ask: ask-id" });
        Assert.Equal(1, AskRunner.RunOpen(script.Env(), Options, Open, noBlock: false));
        Assert.Equal(new[] { WireCommand.AskOpen, WireCommand.AskResult }, script.Sent.Select(r => r.Cmd));
    }

    [Theory]
    [InlineData(ControlAskOutcome.Pending, 1)]
    [InlineData(ControlAskOutcome.Answered, 0)]
    [InlineData(ControlAskOutcome.Cancelled, 2)]
    [InlineData(ControlAskOutcome.Escaped, 3)]
    public void OneShotResultPrintsEveryOutcomeAndMapsTheExit(ControlAskOutcome outcome, int exit)
    {
        var result = outcome == ControlAskOutcome.Answered ? ControlAskResult.Answered("yes", "Yes", 0) : new ControlAskResult { Result = outcome };
        var script = new Script().Reply(Polled(result));
        var request = new ControlRequest { Cmd = WireCommand.AskResult, Target = "ask-id", Args = new ControlArgs { Window = "w" } };

        Assert.Equal(exit, AskRunner.PrintResult(script.Env(), Options, request));

        Assert.Same(request, Assert.Single(script.Sent));
        Assert.Equal(ControlJson.Serialize(result) + Environment.NewLine, script.Out.ToString());
    }

    [Fact]
    public void AOneShotMalformedResultDoesNotCancelSomeoneElsesDialog()
    {
        var script = new Script().Reply(new ControlResponse { Ok = true });
        Assert.Equal(1, AskRunner.PrintResult(script.Env(), Options, new ControlRequest { Cmd = WireCommand.AskResult, Target = "ask-id" }));
        Assert.Single(script.Sent);
        Assert.Equal("error: ask.result missing result" + Environment.NewLine, script.Error.ToString());
    }
}
