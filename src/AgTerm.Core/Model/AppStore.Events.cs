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
        // every path that hands this store a session ends here (new, undone close, recent restore),
        // so it is where the session's pane-focus baseline is taken
        WatchPaneFocus(session);
        EmitControlEvent(ControlEventKind.SessionCreated, workspace, session.Id,
            new ControlEventPayload { Name = session.DisplayName });
        ScheduleTreeChanged();
    }

    /// <summary>pane.split / pane.scratch carry shown/hidden; callers emit only on a real transition.</summary>
    internal void EmitPaneVisibility(ControlEventKind kind, Session session, bool shown) =>
        EmitControlEvent(kind, WorkspaceForSession(session.Id)?.Id, session.Id,
            new ControlEventPayload { Name = session.DisplayName, Status = shown ? "shown" : "hidden" });

    /// <summary>session.selected fires on every change of this window's selection, whoever made it, a
    /// close reselecting included. A cleared selection carries no session; window focus is not a
    /// selection, and a Restore is a reload, not a selection.</summary>
    private void EmitSessionSelected(Guid? previous)
    {
        if (_restoringSelection) return;
        var selected = ActiveSession;
        EmitControlEvent(ControlEventKind.SessionSelected,
            selected is null ? null : WorkspaceForSession(selected.Id)?.Id, selected?.Id,
            new ControlEventPayload
            {
                Name = selected?.DisplayName,
                Previous = previous?.ToString("D").ToUpperInvariant(),
            });
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
