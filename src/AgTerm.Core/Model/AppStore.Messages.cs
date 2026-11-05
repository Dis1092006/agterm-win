using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

// The message bus side of the store (port of AppStore+Messages.swift). A mailbox is keyed by the pane
// token a surface carries (AGTERM_PANE_ID), so every helper reads the token off the slot's surface.

public sealed partial class AppStore
{
    /// <summary>The bus this store's panes receive on. Every store of the app shares one; tests swap in their own.</summary>
    public MessageBus MessageBus { get; set; } = MessageBus.Shared;

    /// <summary>Drops the mailbox of every pane the session holds, scratch included. A session leaving the
    /// tree takes its mail, even through a soft close that is later undone.</summary>
    internal void DisposeMailboxes(Session session)
    {
        DisposeMailbox(session.Surface);
        DisposeMailbox(session.SplitSurface);
        DisposeMailbox(session.ScratchSurface);
    }

    /// <summary>Drops the mailbox keyed by one surface's pane token.</summary>
    public void DisposeMailbox(ITerminalSurface? surface)
    {
        if (MailboxKey(surface?.PaneToken) is { } pane) MessageBus.Dispose(pane);
    }

    /// <summary>Drops the mailbox of every pane this store holds: its window is going away.</summary>
    public void DisposeAllMailboxes()
    {
        foreach (var session in Workspaces.SelectMany(w => w.Sessions)) DisposeMailboxes(session);
    }

    /// <summary>Resolves one pane of a session to a live pane: a role picks that slot's surface now, a pane
    /// id must be one of the session's own live panes. Null for no such session or no such live pane.</summary>
    public MessageRecipient? MessageRecipient(Guid sessionId, MessageAddressPane pane)
    {
        if (SessionWithId(sessionId) is not { } session) return null;
        var live = LivePanes(session);
        Guid? found = pane.PaneId is { } id
            ? live.Where(p => p.Pane == id).Select(p => (Guid?)p.Pane).FirstOrDefault()
            : live.Where(p => p.Role == pane.PaneRole).Select(p => (Guid?)p.Pane).FirstOrDefault();
        return found is { } match ? new MessageRecipient(match, MessageAddress.Canonical(session.Id, match)) : null;
    }

    /// <summary>Every live pane of this store's sessions, or of one workspace's.</summary>
    public IReadOnlyList<MessageRecipient> MessageRecipients(Guid? workspace = null) =>
        Workspaces.Where(w => workspace is null || w.Id == workspace)
            .SelectMany(w => w.Sessions)
            .SelectMany(session => LivePanes(session)
                .Select(p => new MessageRecipient(p.Pane, MessageAddress.Canonical(session.Id, p.Pane))))
            .ToArray();

    /// <summary>Emits the message event when this store holds the recipient's pane.</summary>
    public bool EmitMessageQueued(Guid messageId, string? from, MessageRecipient recipient)
    {
        foreach (var workspace in Workspaces)
        {
            foreach (var session in workspace.Sessions)
            {
                if (LivePanes(session).FirstOrDefault(p => p.Pane == recipient.Pane) is not { Token: { } token }) continue;
                EmitControlEvent(ControlEventKind.Message, workspace.Id, session.Id, new ControlEventPayload
                {
                    Name = session.DisplayName,
                    PaneId = token,
                    From = from,
                    MessageId = messageId.ToString("D").ToUpperInvariant(),
                });
                return true;
            }
        }
        return false;
    }

    /// <summary>The tree read-back for a pane token: its unacked count, null at zero.</summary>
    internal int? PendingMessages(string? token) =>
        MailboxKey(token) is { } pane && MessageBus.Pending(pane) is var count and > 0 ? count : null;

    /// <summary>The session's panes a message can reach: a realized surface carrying a pane token.</summary>
    private static IEnumerable<(StatusPane Role, Guid Pane, string Token)> LivePanes(Session session)
    {
        foreach (var (role, surface) in new[]
                 {
                     (StatusPane.Left, session.Surface), (StatusPane.Right, session.SplitSurface),
                     (StatusPane.Scratch, session.ScratchSurface),
                 })
        {
            if (surface is null or IRealizableSurface { IsRealized: false }) continue;
            if (surface.PaneToken is { } token && MailboxKey(token) is { } pane) yield return (role, pane, token);
        }
    }

    private static Guid? MailboxKey(string? token) => token is null ? null : MessageAddress.ParsePaneId(token);
}
