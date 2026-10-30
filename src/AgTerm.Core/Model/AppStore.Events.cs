using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

// Control events (port of AppStore+Events.swift)

public sealed partial class AppStore
{
    /// <summary>Emits one window-scoped draft through the app-wide ring seam. The host-owned sink
    /// adds the window id; callers supply model identities and already-normalized kind-specific
    /// payloads.</summary>
    internal void EmitControlEvent(ControlEventKind kind, Guid? workspace = null, Guid? session = null,
                                   ControlEventPayload? payload = null)
    {
        ControlEventSink?.Invoke(new ControlEventDraft
        {
            Kind = kind,
            Workspace = workspace?.ToString("D").ToUpperInvariant(),
            Session = session?.ToString("D").ToUpperInvariant(),
            Payload = payload ?? new ControlEventPayload(),
        });
    }

    internal void ScheduleTreeChanged() => EmitControlEvent(ControlEventKind.TreeChanged);

    internal void EmitSessionCreated(Session session, Guid workspace)
    {
        EmitControlEvent(ControlEventKind.SessionCreated, workspace, session.Id,
            new ControlEventPayload { Name = session.DisplayName });
        ScheduleTreeChanged();
    }

    internal void EmitSessionClosed(Session session, Guid workspace)
    {
        EmitControlEvent(ControlEventKind.SessionClosed, workspace, session.Id,
            new ControlEventPayload { Name = session.DisplayName });
        ScheduleTreeChanged();
    }

    /// <summary>Records an accepted terminal/control notification in the app event ring and returns
    /// its effective title. An unresolved session returns null and emits nothing. Delivery gating
    /// belongs to the caller.</summary>
    public string? RecordNotificationEvent(Guid forSession, string title, string body, bool useSessionNameFallback = true)
    {
        if (SessionWithId(forSession) is not { } session
            || WorkspaceForSession(forSession) is not { } workspace) return null;
        var effectiveTitle = useSessionNameFallback && title.Length == 0 ? session.DisplayName : title;
        EmitControlEvent(ControlEventKind.Notify, workspace.Id, forSession,
            new ControlEventPayload { Name = session.DisplayName, Title = effectiveTitle, Body = body });
        return effectiveTitle;
    }
}
