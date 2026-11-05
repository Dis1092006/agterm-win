using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

// Port of agtermCoreTests MessageBusTests.swift (upstream b0839692).
public class MessageBusTests
{
    private readonly Guid _paneA = Guid.NewGuid();
    private readonly Guid _paneB = Guid.NewGuid();

    private static MessageRecipient Recipient(Guid pane) => new(pane, $"S:{pane:N}");

    private static MessageSendResult Send(MessageBus bus, string body, params Guid[] panes) =>
        bus.Send(body, "S:sender", panes.Select(Recipient).ToArray(), now: 1);

    private static string[] Bodies(MessageBatch batch) => batch.Messages.Select(m => m.Body).ToArray();

    [Fact]
    public void MessagesComeBackOldestFirstWithTheirMetadata()
    {
        var bus = new MessageBus();
        var first = Send(bus, "one", _paneA);
        Send(bus, "two", _paneA);

        var batch = bus.Read(_paneA);

        Assert.Equal(["one", "two"], Bodies(batch));
        Assert.Equal(first.MessageId, batch.Messages[0].Id);
        Assert.Equal("S:sender", batch.Messages[0].From);
        Assert.Equal($"S:{_paneA:N}", batch.Messages[0].To);
        Assert.Equal(1, batch.Messages[0].Ts);
        Assert.Equal(0, batch.Remaining);
    }

    [Fact]
    public void AReadIsBoundedAndRepeatableUntilAcked()
    {
        var bus = new MessageBus();
        for (var i = 0; i < 5; i++) Send(bus, $"m{i}", _paneA);

        var first = bus.Read(_paneA, limit: 2);
        var again = bus.Read(_paneA, limit: 2);

        Assert.Equal(["m0", "m1"], Bodies(first));
        Assert.Equal(3, first.Remaining);
        Assert.Equal(first.Messages, again.Messages);
        Assert.Equal(first.Remaining, again.Remaining);
    }

    [Fact]
    public void ABatchNeverExceedsTheCapAndReadAndAckKeepsTheRest()
    {
        var bus = new MessageBus();
        var queued = MessageBus.MaxBatch + 8;
        for (var i = 0; i < queued; i++) Send(bus, $"m{i}", _paneA);

        var read = bus.Read(_paneA, limit: 100);
        Assert.Equal(Enumerable.Range(0, MessageBus.MaxBatch).Select(i => $"m{i}"), Bodies(read));
        Assert.Equal(8, read.Remaining);

        var taken = bus.ReadAndAck(_paneA, limit: 100);
        Assert.Equal(read.Messages, taken.Messages);
        Assert.Equal(read.Remaining, taken.Remaining);
        Assert.Equal(Enumerable.Range(MessageBus.MaxBatch, 8).Select(i => $"m{i}"),
            Bodies(bus.Read(_paneA, limit: 100)));
    }

    [Fact]
    public void AckRemovesOnlyTheNamedMessagesAndIsIdempotent()
    {
        var bus = new MessageBus();
        var first = Send(bus, "one", _paneA);
        Send(bus, "two", _paneA);

        Assert.Equal([first.MessageId], bus.Ack(_paneA, [first.MessageId, Guid.NewGuid()]));
        Assert.Empty(bus.Ack(_paneA, [first.MessageId]));
        Assert.Equal(["two"], Bodies(bus.Read(_paneA)));
    }

    [Fact]
    public void AckIsScopedToTheReadersMailbox()
    {
        var bus = new MessageBus();
        var sent = Send(bus, "both", _paneA, _paneB);

        Assert.Equal([sent.MessageId], bus.Ack(_paneA, [sent.MessageId]));
        Assert.Equal(1, bus.Pending(_paneB));
    }

    [Fact]
    public void ReadAndAckRemovesExactlyTheReturnedBatch()
    {
        var bus = new MessageBus();
        for (var i = 0; i < 3; i++) Send(bus, $"m{i}", _paneA);

        var taken = bus.ReadAndAck(_paneA, limit: 2);

        Assert.Equal(["m0", "m1"], Bodies(taken));
        Assert.Equal(1, taken.Remaining);
        Assert.Equal(["m2"], Bodies(bus.Read(_paneA)));
        Assert.Empty(bus.ReadAndAck(Guid.NewGuid()).Messages);
    }

    [Fact]
    public void AFullMailboxRefusesThatRecipientOnly()
    {
        var bus = new MessageBus();
        for (var i = 0; i < MessageBus.MaxMessages; i++) Send(bus, "x", _paneA);

        var result = Send(bus, "late", _paneA, _paneB);

        Assert.Equal([new MessageRejection($"S:{_paneA:N}", MessageRejectionReason.MailboxFull)], result.Rejected);
        Assert.Equal([$"S:{_paneB:N}"], result.Delivered);
        Assert.Equal(MessageBus.MaxMessages, bus.Pending(_paneA));
        Assert.Equal(1, bus.Pending(_paneB));
    }

    [Fact]
    public void TheMailboxByteBudgetRefusesTheMessageThatWouldExceedIt()
    {
        var bus = new MessageBus();
        var chunk = new string('a', MessageBus.MaxBodyBytes);
        var fits = MessageBus.MaxMailboxBytes / MessageBus.MaxBodyBytes;
        for (var i = 0; i < fits; i++) Send(bus, chunk, _paneA);

        var result = Send(bus, "b", _paneA);

        Assert.Equal([MessageRejectionReason.MailboxFull], result.Rejected.Select(r => r.Reason));
        Assert.Equal(fits, bus.Pending(_paneA));
    }

    [Fact]
    public void AnOversizedBodyIsRefusedForEveryone()
    {
        var bus = new MessageBus();
        var body = new string('é', MessageBus.MaxBodyBytes / 2 + 1);

        var error = Assert.Throws<MessageBodyTooLargeException>(() => Send(bus, body, _paneA));

        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(body), error.Bytes);
        Assert.Equal(0, bus.Pending(_paneA));
    }

    [Fact]
    public void AnEmptyRecipientListDeliversNothing()
    {
        var result = Send(new MessageBus(), "x");

        Assert.Empty(result.Delivered);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void DisposeEmptiesOnlyThatPane()
    {
        var bus = new MessageBus();
        Send(bus, "both", _paneA, _paneB);

        bus.Dispose(_paneA);

        Assert.Equal(0, bus.Pending(_paneA));
        Assert.Empty(bus.Read(_paneA).Messages);
        Assert.Equal(1, bus.Pending(_paneB));
    }

    [Fact]
    public void AMessageEncodesWithItsWireFieldsAndOmitsAnAbsentSender()
    {
        var id = Guid.Parse("da8c8908-18dd-4a3f-bca4-3c90c80bef78");
        var message = new BusMessage { Id = id, To = "S:P", Ts = 2.5, Body = "hi" };

        Assert.Equal("{\"id\":\"DA8C8908-18DD-4A3F-BCA4-3C90C80BEF78\",\"to\":\"S:P\",\"ts\":2.5,\"body\":\"hi\"}",
            ControlJson.Serialize(message));
        Assert.Equal("{\"address\":\"S:P\",\"reason\":\"mailbox full\"}",
            ControlJson.Serialize(new MessageRejection("S:P", MessageRejectionReason.MailboxFull)));
        Assert.Equal("{\"address\":\"S:P\",\"reason\":\"unknown pane\"}",
            ControlJson.Serialize(new MessageRejection("S:P", MessageRejectionReason.UnknownPane)));
    }

    [Fact]
    public void ConcurrentSendsAndReadsKeepEveryMessage()
    {
        var bus = new MessageBus();
        Parallel.For(0, 200, i => Send(bus, $"m{i}", _paneA));

        Assert.Equal(200, bus.Pending(_paneA));
    }
}
