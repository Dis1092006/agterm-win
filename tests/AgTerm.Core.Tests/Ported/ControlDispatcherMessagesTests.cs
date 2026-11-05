using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported from agtermCore ControlDispatcherMessagesTests.swift (upstream b0839692, #847). Pane ids
/// are written in the 32-digit token form the port's AGTERM_PANE_ID carries.</summary>
public class ControlDispatcherMessagesTests
{
    private readonly Guid _paneA = Guid.NewGuid();
    private readonly Guid _paneB = Guid.NewGuid();
    private string AddressA => $"S1:{_paneA:N}";
    private string AddressB => $"S2:{_paneB:N}";
    private MessageRecipient RecipientA => new(_paneA, AddressA);
    private MessageRecipient RecipientB => new(_paneB, AddressB);

    private MockControlActions Actions()
    {
        var actions = new MockControlActions();
        actions.MessagePaneMap[new MessageAddress("s1", MessageAddressPane.Role(StatusPane.Left))] = RecipientA;
        actions.MessagePaneMap[new MessageAddress("S1", MessageAddressPane.Id(_paneA))] = RecipientA;
        actions.MessagePaneMap[new MessageAddress("S2", MessageAddressPane.Id(_paneB))] = RecipientB;
        actions.BroadcastPanes.AddRange([RecipientA, RecipientB]);
        return actions;
    }

    private static Task<ControlResponse?> Dispatch(Command cmd, ControlArgs args, MockControlActions actions, MessageBus bus) =>
        new ControlDispatcher(actions, bus).Dispatch(new ControlRequest { Cmd = cmd, Args = args });

    private static string Upper(Guid id) => id.ToString("D").ToUpperInvariant();

    [Fact]
    public async Task SendDeliversOncePerResolvedPaneAndRejectsTheUnresolved()
    {
        var (actions, bus) = (Actions(), new MessageBus());

        var response = await Dispatch(Command.MsgSend,
            new ControlArgs { Body = "hi", Recipients = ["s1:left", AddressA, "s9:right"] }, actions, bus);

        var result = response!.Result!;
        Assert.True(response.Ok);
        Assert.Equal([AddressA], result.Delivered!);
        Assert.Equal([new MessageRejection("s9:right", MessageRejectionReason.UnknownPane)], result.Rejected!);
        var id = result.MessageId!;
        Assert.Equal([id], bus.Read(_paneA).Messages.Select(m => Upper(m.Id)));
        Assert.Null(bus.Read(_paneA).Messages[0].From);
        Assert.Equal([new MockControlActions.Queued(id, null, [RecipientA])], actions.QueuedMessages);
    }

    [Fact]
    public async Task BroadcastExcludesTheSenderAndStampsItsIdentity()
    {
        var (actions, bus) = (Actions(), new MessageBus());

        var response = await Dispatch(Command.MsgSend,
            new ControlArgs { Body = "hi", All = true, Identity = AddressA }, actions, bus);

        Assert.Equal([AddressB], response!.Result!.Delivered!);
        Assert.Equal(0, bus.Pending(_paneA));
        Assert.Equal(AddressA, bus.Read(_paneB).Messages[0].From);
    }

    [Fact]
    public async Task AnonymousBroadcastReachesEveryPaneAndPassesItsScope()
    {
        var (actions, bus) = (Actions(), new MessageBus());

        var response = await Dispatch(Command.MsgSend,
            new ControlArgs { Workspace = "w1", Window = "win", Body = "hi", All = true }, actions, bus);

        Assert.Equal([AddressA, AddressB], response!.Result!.Delivered!);
        Assert.Equal([new MockControlActions.Scope("w1", "win")], actions.BroadcastScopes);
    }

    [Fact]
    public async Task ASendThatDeliversNothingQueuesNoEvent()
    {
        var (actions, bus) = (Actions(), new MessageBus());

        var response = await Dispatch(Command.MsgSend, new ControlArgs { Body = "hi", Recipients = ["s9:right"] }, actions, bus);

        Assert.True(response!.Ok);
        Assert.Empty(response.Result!.Delivered!);
        Assert.Empty(actions.QueuedMessages);
    }

    [Fact]
    public async Task AHostWithoutTheBusRefusesAnAnonymousSend()
    {
        var (actions, bus) = (Actions(), new MessageBus());
        actions.MessagesUnsupported = true;

        var response = await Dispatch(Command.MsgSend, new ControlArgs { Body = "hi", Recipients = ["s1:left"] }, actions, bus);

        Assert.False(response!.Ok);
        Assert.Equal("msg is not supported on this platform", response.Error);
        Assert.Equal(0, bus.Pending(_paneA));
    }

    [Fact]
    public async Task EveryRecipientUnknownOnASupportingHostIsStillOk()
    {
        var response = await Dispatch(Command.MsgSend,
            new ControlArgs { Body = "hi", Recipients = ["s8:left", "s9:right"] }, Actions(), new MessageBus());

        Assert.True(response!.Ok);
        Assert.Equal([MessageRejectionReason.UnknownPane, MessageRejectionReason.UnknownPane],
            response.Result!.Rejected!.Select(r => r.Reason));
    }

    public static TheoryData<ControlArgs, string> SendRefusals => new()
    {
        { new ControlArgs { Recipients = ["S1:left"] }, "msg.send requires a body" },
        { new ControlArgs { Body = "hi" }, "msg.send requires --to or --all" },
        { new ControlArgs { Body = "hi", Recipients = [] }, "msg.send requires --to or --all" },
        { new ControlArgs { Body = "hi", All = true, Recipients = ["S1:left"] }, "msg.send takes --to or --all, not both" },
        { new ControlArgs { Workspace = "w1", Body = "hi", Recipients = ["S1:left"] }, "msg.send: --workspace narrows --all only" },
        { new ControlArgs { Body = "hi", Recipients = ["S1"] }, "address \"S1\" names a session, not a pane" },
        { new ControlArgs { Body = "hi", Recipients = ["S1:nope"] }, "invalid address \"S1:nope\": expected SESSION:PANE" },
        { new ControlArgs { Body = "hi", All = true, Identity = "S1:left" }, "identity \"S1:left\" must name a pane id, not a role" },
    };

    [Theory]
    [MemberData(nameof(SendRefusals))]
    public async Task SendRefusesBeforeQueueing(ControlArgs args, string error)
    {
        var (actions, bus) = (Actions(), new MessageBus());

        var response = await Dispatch(Command.MsgSend, args, actions, bus);

        Assert.False(response!.Ok);
        Assert.Equal(error, response.Error);
        Assert.Equal(0, bus.Pending(_paneA));
    }

    [Fact]
    public async Task AnOversizedBodyIsRefused()
    {
        var body = new string('x', MessageBus.MaxBodyBytes + 1);

        var response = await Dispatch(Command.MsgSend, new ControlArgs { Body = body, Recipients = [AddressA] }, Actions(), new MessageBus());

        Assert.Equal($"message body too large ({MessageBus.MaxBodyBytes + 1} bytes, max {MessageBus.MaxBodyBytes})", response!.Error);
    }

    [Theory]
    [InlineData(Command.MsgSend)]
    [InlineData(Command.MsgRead)]
    [InlineData(Command.MsgAck)]
    public async Task AnIdentityTheHostRefusesFailsTheCommand(Command cmd)
    {
        var (actions, bus) = (Actions(), new MessageBus());
        var stranger = $"S1:{Guid.NewGuid():N}";

        var response = await Dispatch(cmd,
            new ControlArgs { Body = "hi", All = true, Identity = stranger, Ids = [Upper(Guid.NewGuid())] }, actions, bus);

        Assert.False(response!.Ok);
        Assert.Equal($"pane not found: {stranger}", response.Error);
        Assert.Equal(0, bus.Pending(_paneB));
    }

    [Theory]
    [InlineData(Command.MsgRead)]
    [InlineData(Command.MsgAck)]
    public async Task ReadAndAckRequireAnIdentity(Command cmd)
    {
        var response = await Dispatch(cmd, new ControlArgs { Ids = [Upper(Guid.NewGuid())] }, Actions(), new MessageBus());

        Assert.Equal($"{cmd.ToWire()} requires an identity: --as SESSION:PANE-ID", response!.Error);
    }

    [Fact]
    public async Task ReadLeavesTheMessagesAndCarriesNoAckField()
    {
        var bus = new MessageBus();
        bus.Send("one", null, [RecipientA], now: 1);

        var response = await Dispatch(Command.MsgRead, new ControlArgs { Identity = AddressA }, Actions(), bus);

        Assert.Equal(["one"], response!.Result!.Messages!.Select(m => m.Body));
        Assert.Equal(0, response.Result.Remaining);
        Assert.Null(response.Result.Acked);
        Assert.Equal(1, bus.Pending(_paneA));
    }

    [Fact]
    public async Task ReadWithAckRemovesTheBatchAndNamesIt()
    {
        var bus = new MessageBus();
        var sent = bus.Send("one", null, [RecipientA], now: 1);

        var response = await Dispatch(Command.MsgRead, new ControlArgs { Identity = AddressA, Ack = true }, Actions(), bus);
        var empty = await Dispatch(Command.MsgRead, new ControlArgs { Identity = AddressA, Ack = true }, Actions(), bus);

        Assert.Equal([Upper(sent.MessageId)], response!.Result!.Acked!);
        Assert.Equal(0, bus.Pending(_paneA));
        Assert.Empty(empty!.Result!.Acked!);
        Assert.Empty(empty.Result.Messages!);
        Assert.Equal("{\"messages\":[],\"remaining\":0,\"acked\":[]}", ControlJson.Serialize(empty.Result));
    }

    [Fact]
    public async Task AckRemovesTheNamedMessagesOnly()
    {
        var bus = new MessageBus();
        var first = bus.Send("one", null, [RecipientA], now: 1);
        bus.Send("two", null, [RecipientA], now: 2);

        var response = await Dispatch(Command.MsgAck,
            new ControlArgs { Identity = AddressA, Ids = [first.MessageId.ToString("D")] }, Actions(), bus);

        Assert.Equal([Upper(first.MessageId)], response!.Result!.Acked!);
        Assert.Equal(["two"], bus.Read(_paneA).Messages.Select(m => m.Body));
    }

    [Theory]
    [InlineData(null, "msg.ack requires message ids")]
    [InlineData(new string[0], "msg.ack requires message ids")]
    [InlineData(new[] { "nope" }, "invalid message id: nope")]
    [InlineData(new[] { "da8c890818dd4a3fbca43c90c80bef78" }, "invalid message id: da8c890818dd4a3fbca43c90c80bef78")]
    public async Task AckRefusesMissingOrMalformedIds(string[]? ids, string error)
    {
        var response = await Dispatch(Command.MsgAck, new ControlArgs { Identity = AddressA, Ids = ids }, Actions(), new MessageBus());

        Assert.Equal(error, response!.Error);
    }

    [Fact]
    public void TheNewFieldsRoundTripAndTheScalarToKeepsItsMeaning()
    {
        var request = new ControlRequest
        {
            Cmd = Command.MsgSend,
            Args = new ControlArgs { To = "next", Body = "b", All = true, Recipients = ["S:left"], Identity = "S:P", Ack = true, Ids = ["i"] },
        };
        var json = ControlJson.Serialize(request);

        Assert.Equal(json, ControlJson.Serialize(ControlJson.Deserialize<ControlRequest>(json)));
        Assert.Equal("next", ControlJson.Deserialize<ControlRequest>(json)!.Args!.To);
    }

    [Fact]
    public void TheMessageEventCarriesSenderAndIdButNoBody()
    {
        var payload = new ControlEventPayload { PaneId = "P", From = "S:Q", MessageId = "m" };
        var json = ControlJson.Serialize(new ControlEvent { Seq = 1, Ts = 1, Kind = ControlEventKind.Message, Session = "S", Payload = payload });

        Assert.Contains("\"kind\":\"message\"", json);
        Assert.DoesNotContain("\"body\"", json);
    }

    [Fact]
    public async Task AHostWithoutTheBusRefusesEveryMessageCommand()
    {
        var host = Actions();
        host.MessagesUnsupported = true;

        foreach (var (cmd, args) in new (Command, ControlArgs)[]
                 {
                     (Command.MsgSend, new ControlArgs { Body = "hi", All = true }),
                     (Command.MsgRead, new ControlArgs { Identity = AddressA }),
                     (Command.MsgAck, new ControlArgs { Identity = AddressA, Ids = [Upper(Guid.NewGuid())] }),
                 })
        {
            var response = await new ControlDispatcher(host, new MessageBus()).Dispatch(new ControlRequest { Cmd = cmd, Args = args });
            Assert.Equal("msg is not supported on this platform", response!.Error);
        }
    }
}
