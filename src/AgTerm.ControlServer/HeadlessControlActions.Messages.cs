using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.ControlServer;

// msg.*: the host side of the message bus (port of ControlServer+Messages.swift). The dispatcher owns the
// mailboxes; the host only resolves addresses to live panes and records the message event.

public sealed partial class HeadlessControlActions
{
    public MessagePaneResult<MessageRecipient> ResolveMessagePane(MessageAddress address)
    {
        MessagePaneResult<MessageRecipient> result = new MessagePaneError($"no such session: {address.Session}");
        var response = ResolveSession(address.Session, null, (store, id) =>
        {
            result = store.MessageRecipient(id, address.Pane) is { } recipient
                ? MessagePaneResult<MessageRecipient>.Ok(recipient)
                : new MessagePaneError(NotALivePane(address.Pane, id));
            return new ControlResponse { Ok = true };
        });
        return response.Ok ? result : new MessagePaneError(response.Error ?? $"no such session: {address.Session}");
    }

    public MessagePaneResult<IReadOnlyList<MessageRecipient>> MessagePanes(string? workspace, string? window)
    {
        if (workspace is null) return MessagePaneResult<IReadOnlyList<MessageRecipient>>.Ok(_store.MessageRecipients());
        IReadOnlyList<MessageRecipient> panes = [];
        var response = ResolveWorkspace(workspace, window, (store, id) =>
        {
            panes = store.MessageRecipients(id);
            return new ControlResponse { Ok = true };
        });
        return response.Ok
            ? MessagePaneResult<IReadOnlyList<MessageRecipient>>.Ok(panes)
            : new MessagePaneError(response.Error ?? $"no such workspace: {workspace}");
    }

    public void MessageQueued(Guid messageId, string? from, IReadOnlyList<MessageRecipient> recipients)
    {
        foreach (var recipient in recipients) _store.EmitMessageQueued(messageId, from, recipient);
    }

    /// <summary>The refusal for an address whose session resolved but whose pane is not one of its live panes.</summary>
    public static string NotALivePane(MessageAddressPane pane, Guid session) =>
        $"pane {pane.Text} is not a live pane of session {session.ToString("D").ToUpperInvariant()}";
}
