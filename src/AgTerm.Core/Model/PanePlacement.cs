namespace AgTerm.Core.Model;

/// <summary>Where a pane-scoped control command lands: a role and the pane's identity, or the
/// whole session when both are null.</summary>
public sealed record PanePlacementResult(OverlayPane? Pane, Guid? Identity)
{
    public static readonly PanePlacementResult SessionWide = new(null, null);
}

/// <summary>Port of upstream ControlServer.resolvePanePlacement, host-free so ask and later
/// session.hud resolve panes the same way.</summary>
public static class PanePlacement
{
    public const string PaneNotVisibleError = "pane not visible";
    public const string AskInvalidPaneError = "ask pane must be left or right";

    public static bool TryResolve(OverlayPane? fallback, string? paneId, Session session, bool requireVisible,
                                  string invalidPaneError, out PanePlacementResult placement, out string? error)
    {
        placement = PanePlacementResult.SessionWide;
        error = null;
        var pane = fallback;
        if (paneId is { Length: > 0 } token)
        {
            switch (session.PaneRole(token))
            {
                case StatusPane.Scratch:
                    error = invalidPaneError;
                    return false;
                case StatusPane.Left:
                    pane = OverlayPane.Left;
                    break;
                case StatusPane.Right:
                    pane = OverlayPane.Right;
                    break;
                case null when pane is null:
                    error = $"unknown pane id: {token}";
                    return false;
            }
        }
        if (pane is not { } role) return true;
        if (requireVisible && !session.RendersPane(role))
        {
            error = PaneNotVisibleError;
            return false;
        }
        if (role == OverlayPane.Left)
        {
            placement = new PanePlacementResult(role, session.PaneIdentity);
            return true;
        }
        if (session.SplitPaneIdentity is not { } split)
        {
            error = requireVisible ? PaneNotVisibleError : "session has no split";
            return false;
        }
        placement = new PanePlacementResult(role, split);
        return true;
    }
}

public static class AskAnchors
{
    /// <summary>Whether a GUI question's anchor is still on screen: its session selected and, for a
    /// pane anchor, the same pane in the same role and laid out. Zoom and the dashboard are refused
    /// while a GUI question is pending, so they need no check here.</summary>
    public static bool IsShown(AppStore store, AskAnchor anchor)
    {
        if (store.SelectedSessionId != anchor.SessionId || store.SessionWithId(anchor.SessionId) is not { } session)
            return false;
        if (anchor.PaneIdentity is not { } identity) return true;
        return session.PaneRole(identity) is { } role && role == anchor.Pane && session.RendersPane(role);
    }
}
