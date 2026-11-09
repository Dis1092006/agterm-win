namespace AgTerm.Core.Model;

// Hiding overlays without closing them (port of AppStore+OverlayVisibility.swift)

public sealed partial class AppStore
{
    /// <summary>Hides or shows a session's overlays. A hidden overlay keeps running and stays
    /// addressable. Null on success, including when nothing changed; tree.changed fires only on a
    /// change. The port has only the session-wide slot, so a pane scope holds nothing.</summary>
    public OverlayVisibilityFailure? SetOverlaysHidden(Guid sessionId, OverlayPane? pane, bool hidden)
    {
        if (SessionWithId(sessionId) is not { } session) return OverlayVisibilityFailure.UnknownSession;
        if (pane is not null || !session.OverlayActive) return OverlayVisibilityFailure.NoOverlay;
        if (session.OverlayHidden == hidden) return null;
        session.OverlayHidden = hidden;
        NotifySidebar();
        ScheduleTreeChanged();
        return null;
    }

    /// <summary>Hides the scoped overlays when any of them is shown, otherwise shows them all.</summary>
    public OverlayVisibilityFailure? ToggleOverlaysHidden(Guid sessionId, OverlayPane? pane)
    {
        if (SessionWithId(sessionId) is not { } session) return OverlayVisibilityFailure.UnknownSession;
        return SetOverlaysHidden(sessionId, pane, hidden: session.AnyOverlayShown(pane));
    }

    /// <summary>Runs a control mode against the scoped overlays.</summary>
    public OverlayVisibilityFailure? ApplyOverlayVisibility(Guid sessionId, OverlayPane? pane, OverlayVisibilityMode mode) =>
        mode switch
        {
            OverlayVisibilityMode.Hide => SetOverlaysHidden(sessionId, pane, hidden: true),
            OverlayVisibilityMode.Show => SetOverlaysHidden(sessionId, pane, hidden: false),
            _ => ToggleOverlaysHidden(sessionId, pane),
        };
}
