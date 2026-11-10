using AgTerm.Cli;
using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests.Ported;

// Port of agtermctlKitTests/MessageCommandsTests.swift (upstream b0839692, #847). The Swift
// MessageReadDependencies (send / sleep / now) map onto CliEnvironment (Send / Sleep / Now), and
// SocketReply onto ControlReply, whose line in these fakes is ControlJson.Serialize of the response.
public class MessageCommandsTests
{
    private const string SessionId = "AAAA1111-0000-4000-8000-000000000001";
    private const string PaneId = "BBBB2222-0000-4000-8000-000000000002";
    private static readonly Guid First = Guid.Parse("CCCC3333-0000-4000-8000-000000000003");
    private static readonly Guid Second = Guid.Parse("DDDD4444-0000-4000-8000-000000000004");

    private static string Upper(Guid id) => id.ToString("D").ToUpperInvariant();

    private sealed class FakeApp
    {
        private readonly List<ControlReply> _replies;

        public FakeApp(params ControlReply[] replies) => _replies = replies.ToList();

        public List<ControlRequest> Requests { get; } = new();
        public List<TimeSpan> Sleeps { get; } = new();
        public double Clock { get; private set; }
        public StringWriter Out { get; } = new();
        public StringWriter Error { get; } = new();
        public List<double> Watchdogs { get; } = new();
        public Dictionary<string, string> Variables { get; init; } = new();

        public CliEnvironment Environment => new()
        {
            GetEnv = Variables.GetValueOrDefault,
            Out = Out,
            Error = Error,
            Send = (_, request) =>
            {
                Requests.Add(request);
                var reply = _replies[0];
                if (_replies.Count > 1) _replies.RemoveAt(0);
                return reply;
            },
            Sleep = delay =>
            {
                Sleeps.Add(delay);
                Clock += delay.TotalSeconds;
            },
            Now = () => Clock,
            StartWatchdog = (seconds, _) =>
            {
                Watchdogs.Add(seconds);
                return new NoopDisposable();
            },
        };
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private static T Parse<T>(params string[] argv) => Assert.IsType<T>(Agtermctl.ParseAsRoot(["msg", .. argv]));

    private static BusMessage Message(Guid id, string body, string? from = null) =>
        new() { Id = id, From = from, To = $"{SessionId}:{PaneId}", Ts = 1, Body = body };

    private static ControlReply Reply(ControlResult? result, bool ok = true, string? error = null) =>
        new ControlResponse { Ok = ok, Result = result, Error = error }.AsReply();

    private static string Line(ControlReply reply) => reply.Line;

    private static Func<string, string?> Env(Dictionary<string, string> variables) => variables.GetValueOrDefault;

    private static readonly Func<string, string?> PaneEnv =
        Env(new() { ["AGTERM_SESSION_ID"] = SessionId, ["AGTERM_PANE_ID"] = PaneId });

    private static readonly Func<string, string?> NoEnv = _ => null;

    [Fact]
    public void IdentityComesFromThePanesEnvironment()
    {
        var send = Parse<MessageCommands.Send>("send", "hi", "--to", $"{SessionId}:right");
        Assert.Equal($"{SessionId}:{PaneId}", send.MakeRequest("hi", PaneEnv).Args?.Identity);
    }

    [Fact]
    public void AsOverridesTheEnvironment()
    {
        var send = Parse<MessageCommands.Send>("send", "hi", "--all", "--as", "S:P");
        Assert.Equal("S:P", send.MakeRequest("hi", PaneEnv).Args?.Identity);
        Assert.Equal("S:P", send.MakeRequest("hi", Env(new() { ["AGTERM_SESSION_ID"] = SessionId })).Args?.Identity);
    }

    public static TheoryData<Dictionary<string, string>> IncompletePairs => new()
    {
        new Dictionary<string, string> { ["AGTERM_SESSION_ID"] = "S" },
        new Dictionary<string, string> { ["AGTERM_PANE_ID"] = "P" },
        new Dictionary<string, string> { ["AGTERM_SESSION_ID"] = "S", ["AGTERM_PANE_ID"] = "" },
        new Dictionary<string, string> { ["AGTERM_SESSION_ID"] = "" },
        new Dictionary<string, string> { ["AGTERM_PANE_ID"] = "" },
        new Dictionary<string, string> { ["AGTERM_SESSION_ID"] = "", ["AGTERM_PANE_ID"] = "" },
    };

    [Theory]
    [MemberData(nameof(IncompletePairs))]
    public void AnIncompleteEnvironmentPairIsAnError(Dictionary<string, string> variables)
    {
        var send = Parse<MessageCommands.Send>("send", "hi", "--all");
        var error = Assert.Throws<CliValidationException>(() => send.MakeRequest("hi", Env(variables)));
        Assert.Equal("incomplete pane identity: AGTERM_SESSION_ID and AGTERM_PANE_ID must both be set; "
            + "pass --as SESSION:PANE-ID", error.Message);
    }

    [Fact]
    public void WithoutAnyIdentitySendIsAnonymousAndReadSendsNone()
    {
        var send = Parse<MessageCommands.Send>("send", "hi", "--all");
        var read = Parse<MessageCommands.Read>("read");
        var ack = Parse<MessageCommands.Ack>("ack", Upper(First));
        Assert.Null(send.MakeRequest("hi", NoEnv).Args?.Identity);
        Assert.Null(read.MakeRequest(NoEnv).Args?.Identity);
        Assert.Null(ack.MakeRequest(NoEnv).Args?.Identity);
    }

    [Fact]
    public void SendCarriesRecipientsWorkspaceAndBody()
    {
        var send = Parse<MessageCommands.Send>("send", "x", "--to", "a:left", "--to", "b:right");
        var request = send.MakeRequest("x", NoEnv);
        Assert.Equal(WireCommand.MsgSend, request.Cmd);
        Assert.Equal(new[] { "a:left", "b:right" }, request.Args?.Recipients);
        Assert.Null(request.Args?.All);
        Assert.Equal("x", request.Args?.Body);

        var broadcast = Parse<MessageCommands.Send>("send", "--stdin", "--all", "--workspace", "w");
        var all = broadcast.MakeRequest("y", NoEnv);
        Assert.True(all.Args?.All);
        Assert.Equal("w", all.Args?.Workspace);
        Assert.Null(all.Args?.Recipients);
    }

    [Theory]
    [InlineData("send", "--to", "a:left")]
    [InlineData("send", "hi", "--stdin", "--to", "a:left")]
    public void SendNeedsExactlyOneBodySource(params string[] argv)
    {
        var error = Assert.Throws<CliValidationException>(() => Agtermctl.ParseAsRoot(["msg", .. argv]));
        Assert.Equal("provide the body as TEXT or --stdin, exactly one", error.Message);
    }

    [Fact]
    public void SendReadsTheBodyFromStdin()
    {
        var send = Parse<MessageCommands.Send>("send", "--stdin", "--all") with
        {
            StdinStream = () => new MemoryStream("from stdin\n"u8.ToArray()),
        };
        var app = new FakeApp(Reply(new ControlResult { MessageId = Upper(First), Delivered = [], Rejected = [] }));

        Assert.Equal(0, send.Execute(app.Environment));
        Assert.Equal("from stdin\n", Assert.Single(app.Requests).Args?.Body);
        Assert.Equal(Upper(First) + "\n", app.Out.ToString());
    }

    [Fact]
    public void SendRefusesStdinThatIsNotUtf8()
    {
        var send = Parse<MessageCommands.Send>("send", "--stdin", "--all") with
        {
            StdinStream = () => new MemoryStream(new byte[] { 0xff, 0xfe }),
        };
        var error = Assert.Throws<CliValidationException>(() => send.Execute(new FakeApp(Reply(null)).Environment));
        Assert.Equal("stdin must be valid UTF-8", error.Message);
    }

    [Fact]
    public void SendPrintsTheIdAndReportsRejectionsAsAFailure()
    {
        var clean = Reply(new ControlResult { MessageId = Upper(First), Delivered = ["a:b"], Rejected = [] });
        Assert.Equal(new MessageOutput(Stdout: Upper(First) + "\n"), MessageCommands.Send.Output(clean, json: false));

        var partial = Reply(new ControlResult
        {
            MessageId = Upper(First),
            Delivered = ["a:b"],
            Rejected =
            [
                new MessageRejection("c:d", MessageRejectionReason.MailboxFull),
                new MessageRejection("e:f", MessageRejectionReason.UnknownPane),
            ],
        });
        Assert.Equal(new MessageOutput(Stdout: Upper(First) + "\n",
                Stderr: "rejected c:d: mailbox full\nrejected e:f: unknown pane\n", Failed: true),
            MessageCommands.Send.Output(partial, json: false));
        Assert.Equal(new MessageOutput(Stdout: Line(partial) + "\n", Failed: true),
            MessageCommands.Send.Output(partial, json: true));
    }

    [Fact]
    public void ABroadcastReachingNoPaneSucceeds()
    {
        var empty = Reply(new ControlResult { MessageId = Upper(First), Delivered = [], Rejected = [] });
        Assert.False(MessageCommands.Send.Output(empty, json: false).Failed);
    }

    [Fact]
    public void AServerErrorFailsEveryCommand()
    {
        var refused = Reply(null, ok: false, error: "msg.read requires an identity: --as SESSION:PANE-ID");
        var human = new MessageOutput(Stderr: "error: msg.read requires an identity: --as SESSION:PANE-ID\n", Failed: true);
        Assert.Equal(human, MessageCommands.Send.Output(refused, json: false));
        Assert.Equal(human, Parse<MessageCommands.Read>("read").Output(refused));
        Assert.Equal(human, MessageCommands.Ack.Output(refused, json: false));
        Assert.Equal(new MessageOutput(Stdout: Line(refused) + "\n", Failed: true),
            MessageCommands.Ack.Output(refused, json: true));
    }

    [Fact]
    public void ReadPrintsEachMessageAsItsOwnRecord()
    {
        var batch = Reply(new ControlResult
        {
            Messages = [Message(First, "one", from: "S:P"), Message(Second, "two\n")],
            Remaining = 3,
        });
        Assert.Equal(new MessageOutput(
                Stdout: $"{Upper(First)} from S:P\none\n{Upper(Second)} from outside agterm\ntwo\n",
                Stderr: "3 more pending\n"),
            Parse<MessageCommands.Read>("read").Output(batch));
    }

    [Fact]
    public void AnEmptyReadPrintsNothing()
    {
        var empty = Reply(new ControlResult { Messages = [], Remaining = 0 });
        Assert.Equal(new MessageOutput(), Parse<MessageCommands.Read>("read").Output(empty));
    }

    [Fact]
    public void ReadJsonIsTheServerLine()
    {
        var batch = Reply(new ControlResult { Messages = [Message(First, "one")], Remaining = 0 });
        Assert.Equal(new MessageOutput(Stdout: Line(batch) + "\n"), Parse<MessageCommands.Read>("read", "--json").Output(batch));
    }

    [Fact]
    public void JsonPrintsAFieldTheClientDoesNotModel()
    {
        const string line = """{"ok":true,"result":{"messages":[],"remaining":0,"fromTheFuture":1}}""";
        var reply = new ControlReply(ControlJson.Deserialize<ControlResponse>(line)!, line);
        Assert.Equal(new MessageOutput(Stdout: line + "\n"), MessageCommands.Send.Output(reply, json: true));
        Assert.Equal(new MessageOutput(Stdout: line + "\n"), Parse<MessageCommands.Read>("read", "--json").Output(reply));
        Assert.Equal(new MessageOutput(Stdout: line + "\n"), MessageCommands.Ack.Output(reply, json: true));
    }

    [Fact]
    public void ReadAckSendsAckAndPrintsWhenTheAppConfirmsIt()
    {
        var read = Parse<MessageCommands.Read>("read", "--ack");
        Assert.True(read.MakeRequest(PaneEnv).Args?.Ack);
        var confirmed = Reply(new ControlResult { Messages = [Message(First, "one")], Remaining = 0, Acked = [Upper(First)] });
        Assert.Equal(new MessageOutput(Stdout: $"{Upper(First)} from outside agterm\none\n"), read.Output(confirmed));
    }

    public static TheoryData<string[]?> UnconfirmedAcks => new()
    {
        null,
        Array.Empty<string>(),
        new[] { "00000000-0000-4000-8000-000000000000" },
    };

    [Theory]
    [MemberData(nameof(UnconfirmedAcks))]
    public void ReadAckFailsWithoutPrintingWhenTheAppDoesNotConfirm(string[]? acked)
    {
        var batch = Reply(new ControlResult { Messages = [Message(First, "one")], Remaining = 0, Acked = acked });
        foreach (var argv in new[] { new[] { "read", "--ack" }, new[] { "read", "--ack", "--json" } })
        {
            var output = Parse<MessageCommands.Read>(argv).Output(batch);
            Assert.Empty(output.Stdout);
            Assert.True(output.Failed);
            Assert.Equal("error: the app did not confirm which messages it removed; nothing was printed\n", output.Stderr);
        }
    }

    [Fact]
    public void ReadWithoutWaitAsksOnce()
    {
        var app = new FakeApp(Reply(new ControlResult { Messages = [], Remaining = 0 }));
        var read = Parse<MessageCommands.Read>("read");
        var outcome = read.ReadMail(read.MakeRequest(PaneEnv), app.Environment);
        Assert.False(outcome.TimedOut);
        Assert.Single(app.Requests);
    }

    [Fact]
    public void WaitPollsTheRealReadUntilMailArrives()
    {
        var empty = Reply(new ControlResult { Messages = [], Remaining = 0, Acked = [] });
        var mail = Reply(new ControlResult { Messages = [Message(First, "one")], Remaining = 0, Acked = [Upper(First)] });
        var app = new FakeApp(empty, empty, mail);
        var read = Parse<MessageCommands.Read>("read", "--wait", "--ack");

        var outcome = read.ReadMail(read.MakeRequest(PaneEnv), app.Environment);

        Assert.False(outcome.TimedOut);
        Assert.Equal(new[] { First }, outcome.Reply!.Response.Result?.Messages?.Select(m => m.Id));
        Assert.Equal(3, app.Requests.Count);
        Assert.All(app.Requests, request =>
        {
            Assert.Equal(WireCommand.MsgRead, request.Cmd);
            Assert.True(request.Args?.Ack);
        });
        Assert.Equal(new[] { MessageCommands.Read.PollDelay, MessageCommands.Read.PollDelay }, app.Sleeps);
    }

    [Fact]
    public void WaitStopsOnAServerErrorOrAnUnconfirmedAck()
    {
        var read = Parse<MessageCommands.Read>("read", "--wait", "--ack");
        var refused = new FakeApp(Reply(null, ok: false, error: "pane gone"));
        read.ReadMail(read.MakeRequest(PaneEnv), refused.Environment);
        Assert.Single(refused.Requests);

        var ignored = new FakeApp(Reply(new ControlResult { Messages = [], Remaining = 0 }));
        read.ReadMail(read.MakeRequest(PaneEnv), ignored.Environment);
        Assert.Single(ignored.Requests);
    }

    [Fact]
    public void WaitTimesOut()
    {
        var app = new FakeApp(Reply(new ControlResult { Messages = [], Remaining = 0 }));
        var read = Parse<MessageCommands.Read>("read", "--wait", "--timeout", "1");
        Assert.True(read.ReadMail(read.MakeRequest(PaneEnv), app.Environment).TimedOut);
        Assert.Equal(1, app.Clock);
        Assert.Equal(124, MessageCommands.Read.TimeoutExitCode);
    }

    [Fact]
    public void ATimedOutReadPrintsTimedOutAndExits124UnderAWatchdog()
    {
        var app = new FakeApp(Reply(new ControlResult { Messages = [], Remaining = 0 })) { Variables = { ["AGTERM_SESSION_ID"] = SessionId, ["AGTERM_PANE_ID"] = PaneId } };
        var read = Parse<MessageCommands.Read>("read", "--wait", "--timeout", "2");

        Assert.Equal(124, read.Execute(app.Environment));
        Assert.Equal("timed out\n", app.Error.ToString().ReplaceLineEndings("\n"));
        Assert.Empty(app.Out.ToString());
        Assert.Equal(new[] { 2.0 }, app.Watchdogs);
        Assert.Equal($"{SessionId}:{PaneId}", app.Requests[0].Args?.Identity);
    }

    [Theory]
    [InlineData("read", "--timeout", "5")]
    [InlineData("read", "--wait", "--timeout", "0")]
    [InlineData("read", "--wait", "--timeout", "-1")]
    public void TimeoutNeedsWaitAndAPositiveValue(params string[] argv)
    {
        Assert.Throws<CliValidationException>(() => Agtermctl.ParseAsRoot(["msg", .. argv]));
    }

    [Fact]
    public void TimeoutValidationMessages()
    {
        Assert.Equal("--timeout needs --wait",
            Assert.Throws<CliValidationException>(() => Agtermctl.ParseAsRoot(["msg", "read", "--timeout", "5"])).Message);
        Assert.Equal("--timeout must be a positive number of seconds",
            Assert.Throws<CliValidationException>(() => Agtermctl.ParseAsRoot(["msg", "read", "--wait", "--timeout", "0"])).Message);
    }

    [Fact]
    public void AckSendsTheIdsAndCountsWhatWasRemoved()
    {
        var ack = Parse<MessageCommands.Ack>("ack", Upper(First), Upper(Second));
        var request = ack.MakeRequest(PaneEnv);
        Assert.Equal(WireCommand.MsgAck, request.Cmd);
        Assert.Equal(new[] { Upper(First), Upper(Second) }, request.Args?.Ids);
        Assert.Equal($"{SessionId}:{PaneId}", request.Args?.Identity);

        var removed = Reply(new ControlResult { Acked = [Upper(First)] });
        Assert.Equal(new MessageOutput(Stdout: "acked 1\n"), MessageCommands.Ack.Output(removed, json: false));
        Assert.Equal(new MessageOutput(Stdout: Line(removed) + "\n"), MessageCommands.Ack.Output(removed, json: true));
    }

    [Fact]
    public void AckNeedsAnId()
    {
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(["msg", "ack"]));
    }

    [Fact]
    public void RunWritesTheOutputAndExitsOneOnAFailure()
    {
        var app = new FakeApp(Reply(new ControlResult
        {
            MessageId = Upper(First),
            Delivered = [],
            Rejected = [new MessageRejection("c:d", MessageRejectionReason.UnknownPane)],
        }));

        Assert.Equal(1, Agtermctl.Run(["msg", "send", "hi", "--to", "c:d"], app.Environment));
        Assert.Equal(Upper(First) + "\n", app.Out.ToString());
        Assert.Equal("rejected c:d: unknown pane\n", app.Error.ToString());
    }

    [Fact]
    public void AnIncompleteIdentityExitsOneBeforeConnecting()
    {
        var app = new FakeApp(Reply(null)) { Variables = { ["AGTERM_SESSION_ID"] = SessionId } };

        Assert.Equal(1, Agtermctl.Run(["msg", "read"], app.Environment));
        Assert.Empty(app.Requests);
        Assert.StartsWith("error: incomplete pane identity", app.Error.ToString());
    }
}
