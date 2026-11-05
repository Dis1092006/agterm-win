using System.Text;
using System.Text.Json.Serialization;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

/// <summary>One message queued in a pane's mailbox. From is the sender's SESSION:PANE-ID, absent for a
/// sender outside agterm. Ts shares the ControlEvent.ts clock.</summary>
public sealed record BusMessage
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(UppercaseGuidConverter))]
    public required Guid Id { get; init; }

    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("to")]
    public required string To { get; init; }

    [JsonPropertyName("ts")]
    public required double Ts { get; init; }

    [JsonPropertyName("body")]
    public required string Body { get; init; }
}

/// <summary>A resolved recipient: the pane's mailbox key and the address it is reported under.</summary>
public readonly record struct MessageRecipient(Guid Pane, string Address);

[JsonConverter(typeof(MessageRejectionReasonConverter))]
public enum MessageRejectionReason { MailboxFull, UnknownPane }

public sealed class MessageRejectionReasonConverter : WireEnumConverter<MessageRejectionReason>
{
    public MessageRejectionReasonConverter() : base(
        (MessageRejectionReason.MailboxFull, "mailbox full"), (MessageRejectionReason.UnknownPane, "unknown pane"))
    {
    }
}

public static class MessageRejectionReasons
{
    /// <summary>The reason's wire string (the Swift rawValue).</summary>
    public static string ToWire(this MessageRejectionReason reason) => reason switch
    {
        MessageRejectionReason.MailboxFull => "mailbox full",
        MessageRejectionReason.UnknownPane => "unknown pane",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}

/// <summary>A recipient a send could not reach.</summary>
public sealed record MessageRejection(
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("reason")] MessageRejectionReason Reason);

/// <summary>Each recipient of one send; every copy shares MessageId.</summary>
public sealed record MessageSendResult(Guid MessageId, IReadOnlyList<string> Delivered, IReadOnlyList<MessageRejection> Rejected);

/// <summary>The oldest part of a mailbox; Remaining counts the pending messages left out of it.</summary>
public sealed record MessageBatch(IReadOnlyList<BusMessage> Messages, int Remaining);

/// <summary>A body over MessageBus.MaxBodyBytes refuses the whole send.</summary>
public sealed class MessageBodyTooLargeException(int bytes)
    : Exception($"message body too large ({bytes} bytes, max {MessageBus.MaxBodyBytes})")
{
    public int Bytes { get; } = bytes;
}

/// <summary>
/// The app run's per-pane mailboxes, keyed by pane token. It knows nothing of sessions or roles: callers
/// resolve addresses first, and dispose a pane's mailbox when the pane goes. Every window's store shares
/// <see cref="Shared"/>, and each store runs under its own lock, so the bus takes its own.
/// </summary>
public sealed class MessageBus
{
    public static MessageBus Shared { get; } = new();

    public const int MaxBodyBytes = 64 * 1024;
    public const int MaxMessages = 256;
    public const int MaxMailboxBytes = 1024 * 1024;
    public const int MaxBatch = 32;

    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, List<BusMessage>> _mailboxes = new();

    /// <summary>Queues one copy of body per recipient, refusing only the recipients whose mailbox it would
    /// overfill. A body over the limit refuses the whole send.</summary>
    public MessageSendResult Send(string body, string? from, IReadOnlyList<MessageRecipient> recipients, double now)
    {
        var size = Encoding.UTF8.GetByteCount(body);
        if (size > MaxBodyBytes) throw new MessageBodyTooLargeException(size);
        var id = Guid.NewGuid();
        var delivered = new List<string>();
        var rejected = new List<MessageRejection>();
        lock (_lock)
        {
            foreach (var recipient in recipients)
            {
                _mailboxes.TryGetValue(recipient.Pane, out var box);
                if (box is not null && (box.Count >= MaxMessages || Bytes(box) + size > MaxMailboxBytes))
                {
                    rejected.Add(new MessageRejection(recipient.Address, MessageRejectionReason.MailboxFull));
                    continue;
                }
                if (box is null) _mailboxes[recipient.Pane] = box = new List<BusMessage>();
                box.Add(new BusMessage { Id = id, From = from, To = recipient.Address, Ts = now, Body = body });
                delivered.Add(recipient.Address);
            }
        }
        return new MessageSendResult(id, delivered, rejected);
    }

    /// <summary>The oldest messages, not removed, so a repeat read answers the same batch.</summary>
    public MessageBatch Read(Guid pane, int limit = MaxBatch)
    {
        lock (_lock) return ReadLocked(pane, limit);
    }

    /// <summary>The oldest messages, removing exactly those in one step.</summary>
    public MessageBatch ReadAndAck(Guid pane, int limit = MaxBatch)
    {
        lock (_lock)
        {
            var batch = ReadLocked(pane, limit);
            if (batch.Messages.Count > 0) _mailboxes[pane].RemoveRange(0, batch.Messages.Count);
            return batch;
        }
    }

    /// <summary>Removes the named messages from pane's mailbox and answers the ids it removed.</summary>
    public IReadOnlyList<Guid> Ack(Guid pane, IEnumerable<Guid> ids)
    {
        lock (_lock)
        {
            if (!_mailboxes.TryGetValue(pane, out var box)) return [];
            var wanted = ids.ToHashSet();
            var removed = box.Where(m => wanted.Contains(m.Id)).Select(m => m.Id).ToList();
            box.RemoveAll(m => wanted.Contains(m.Id));
            return removed;
        }
    }

    public int Pending(Guid pane)
    {
        lock (_lock) return _mailboxes.TryGetValue(pane, out var box) ? box.Count : 0;
    }

    public void Dispose(Guid pane)
    {
        lock (_lock) _mailboxes.Remove(pane);
    }

    private MessageBatch ReadLocked(Guid pane, int limit)
    {
        if (!_mailboxes.TryGetValue(pane, out var box)) return new MessageBatch([], 0);
        var batch = box.Take(Math.Min(Math.Max(limit, 0), MaxBatch)).ToArray();
        return new MessageBatch(batch, box.Count - batch.Length);
    }

    private static int Bytes(List<BusMessage> box) => box.Sum(m => Encoding.UTF8.GetByteCount(m.Body));
}
