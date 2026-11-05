using System.Text;
using AgTerm.Core.Model;

namespace AgTerm.Core.Protocol;

public enum MessagePaneErrorKind { Unresolved, Unsupported }

/// <summary>A bus address the host could not resolve to a live pane, or a host without the bus. Only
/// Unresolved becomes a per-recipient rejection; Unsupported refuses the whole command.</summary>
public sealed record MessagePaneError(string Message, MessagePaneErrorKind Kind = MessagePaneErrorKind.Unresolved)
{
    public static MessagePaneError Unsupported { get; } =
        new("msg is not supported on this platform", MessagePaneErrorKind.Unsupported);
}

/// <summary>The value a host resolved, or the reason it could not.</summary>
public readonly struct MessagePaneResult<T>
{
    private MessagePaneResult(T? value, MessagePaneError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }
    public MessagePaneError? Error { get; }

    public static MessagePaneResult<T> Ok(T value) => new(value, null);

    public static implicit operator MessagePaneResult<T>(MessagePaneError error) => new(default, error);
}

/// <summary>msg.*: parses and validates the command, asks the host only to resolve addresses and report a
/// queued message, and runs the mailbox operation itself. ControlDispatcher routes msg.* here; a host that
/// spans several windows runs its own instance over an app-wide IMessagePaneHost.</summary>
public sealed class MessageDispatcher(IMessagePaneHost host, MessageBus? bus = null)
{
    private readonly IMessagePaneHost _actions = host;
    private readonly MessageBus _bus = bus ?? MessageBus.Shared;

    public ControlResponse Dispatch(ControlRequest request)
    {
        var args = request.Args ?? new ControlArgs();
        return request.Cmd switch
        {
            Command.MsgSend => DispatchMessageSend(args),
            Command.MsgRead => DispatchMessageRead(args),
            Command.MsgAck => DispatchMessageAck(args),
            _ => throw new ArgumentOutOfRangeException(nameof(request), $"unexpected msg command: {request.Cmd}"),
        };
    }

    private ControlResponse DispatchMessageSend(ControlArgs args)
    {
        if (args.Body is not { } body) return Err("msg.send requires a body");
        var addresses = args.Recipients ?? [];
        var all = args.All == true;
        if (all && addresses.Length > 0) return Err("msg.send takes --to or --all, not both");
        if (!all && addresses.Length == 0) return Err("msg.send requires --to or --all");
        if (!all && args.Workspace is not null) return Err("msg.send: --workspace narrows --all only");
        var bytes = Encoding.UTF8.GetByteCount(body);
        if (bytes > MessageBus.MaxBodyBytes) return TooLarge(bytes);

        MessageRecipient? sender = null;
        if (args.Identity is { } identity)
        {
            var resolved = ResolveIdentity(identity);
            if (resolved.Error is { } error) return Err(error.Message);
            sender = resolved.Value;
        }

        var targets = new List<MessageRecipient>();
        var rejected = new List<MessageRejection>();
        if (all)
        {
            var panes = _actions.MessagePanes(args.Workspace, args.Window);
            if (panes.Error is { } error) return Err(error.Message);
            targets.AddRange(panes.Value!.Where(p => p.Pane != sender?.Pane));
        }
        else
        {
            foreach (var text in addresses)
            {
                MessageAddress address;
                try { address = MessageAddress.Recipient(text); }
                catch (MessageAddressException e) { return Err(e.Message); }
                var resolved = _actions.ResolveMessagePane(address);
                if (resolved.Error is { Kind: MessagePaneErrorKind.Unsupported } unsupported) return Err(unsupported.Message);
                if (resolved.Error is not null) rejected.Add(new MessageRejection(text, MessageRejectionReason.UnknownPane));
                else targets.Add(resolved.Value);
            }
        }
        var seen = new HashSet<Guid>();
        targets = targets.Where(t => seen.Add(t.Pane)).ToList();

        MessageSendResult result;
        try { result = _bus.Send(body, sender?.Address, targets, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0); }
        catch (MessageBodyTooLargeException) { return TooLarge(bytes); }
        var delivered = result.Delivered.ToHashSet();
        var reached = targets.Where(t => delivered.Contains(t.Address)).ToArray();
        if (reached.Length > 0) _actions.MessageQueued(result.MessageId, sender?.Address, reached);
        return new ControlResponse
        {
            Ok = true,
            Result = new ControlResult
            {
                MessageId = UpperId(result.MessageId),
                Delivered = result.Delivered,
                Rejected = [.. rejected, .. result.Rejected],
            },
        };
    }

    private ControlResponse DispatchMessageRead(ControlArgs args)
    {
        var reader = RequireIdentity(args.Identity, Command.MsgRead);
        if (reader.Error is { } error) return Err(error.Message);
        if (args.Ack != true)
        {
            var batch = _bus.Read(reader.Value.Pane);
            return new ControlResponse { Ok = true, Result = new ControlResult { Messages = batch.Messages, Remaining = batch.Remaining } };
        }
        var acked = _bus.ReadAndAck(reader.Value.Pane);
        return new ControlResponse
        {
            Ok = true,
            Result = new ControlResult
            {
                Messages = acked.Messages,
                Remaining = acked.Remaining,
                Acked = acked.Messages.Select(m => UpperId(m.Id)).ToArray(),
            },
        };
    }

    private ControlResponse DispatchMessageAck(ControlArgs args)
    {
        var reader = RequireIdentity(args.Identity, Command.MsgAck);
        if (reader.Error is { } error) return Err(error.Message);
        if (args.Ids is not { Length: > 0 } raw) return Err("msg.ack requires message ids");
        var ids = new List<Guid>();
        foreach (var text in raw)
        {
            if (!Guid.TryParseExact(text, "D", out var id)) return Err($"invalid message id: {text}");
            ids.Add(id);
        }
        var removed = _bus.Ack(reader.Value.Pane, ids);
        return new ControlResponse { Ok = true, Result = new ControlResult { Acked = removed.Select(UpperId).ToArray() } };
    }

    private MessagePaneResult<MessageRecipient> RequireIdentity(string? text, Command command) =>
        text is null
            ? new MessagePaneError($"{command.ToWire()} requires an identity: --as SESSION:PANE-ID")
            : ResolveIdentity(text);

    private MessagePaneResult<MessageRecipient> ResolveIdentity(string text)
    {
        MessageAddress address;
        try { address = MessageAddress.Identity(text); }
        catch (MessageAddressException e) { return new MessagePaneError(e.Message); }
        return _actions.ResolveMessagePane(address);
    }

    private static ControlResponse TooLarge(int bytes) =>
        Err($"message body too large ({bytes} bytes, max {MessageBus.MaxBodyBytes})");

    private static string UpperId(Guid id) => id.ToString("D").ToUpperInvariant();

    private static ControlResponse Err(string error) => new() { Ok = false, Error = error };
}
