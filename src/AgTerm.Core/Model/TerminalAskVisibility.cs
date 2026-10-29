namespace AgTerm.Core.Model;

/// <summary>A terminal question the window draws now: its session, the pane it covers and that
/// pane's page id, or the whole session area when Pane is null. CoveredPaneIds are the page panes
/// whose focus goes to the question; the quick terminal is never one of them.</summary>
public sealed record TerminalAskPlacement(Guid SessionId, PendingAsk Ask, OverlayPane? Pane, string? PaneId,
                                          IReadOnlyList<string> CoveredPaneIds)
{
    /// <summary>By value over the pane list: the window skips a snapshot equal to the last one, and
    /// a list compared by reference would make every snapshot new.</summary>
    public bool Equals(TerminalAskPlacement? other) =>
        other is not null && SessionId == other.SessionId && Ask == other.Ask && Pane == other.Pane
        && PaneId == other.PaneId && CoveredPaneIds.SequenceEqual(other.CoveredPaneIds);

    public override int GetHashCode() => HashCode.Combine(SessionId, Ask, Pane, PaneId);
}

public static class TerminalAskVisibility
{
    /// <summary>Only the selected session is drawn. Session zoom and the dashboard hide questions without resolving them. A pane question needs its pane laid out and hides under scratch; a session question draws above it. Detached quick leaves the question drawn and defers keyboard focus at the page boundary.</summary>
    public static TerminalAskPlacement? For(AppStore store, bool zoomActive, bool dashboardOpen, bool quickTerminalVisible)
    {
        if (zoomActive || dashboardOpen
            || store.ActiveSession is not { AskPending: { } ask } session)
            return null;
        if (session.AskPaneIdentity is null)
            return new TerminalAskPlacement(session.Id, ask, null, null, SessionPaneIds(session));
        if (session.AskTargetPane is not { } pane || !session.RendersPane(pane) || session.ScratchActive)
            return null;
        var paneId = pane == OverlayPane.Left ? session.Surface?.PaneToken : session.SplitSurface?.PaneToken;
        return string.IsNullOrWhiteSpace(paneId) ? null : new TerminalAskPlacement(session.Id, ask, pane, paneId, PaneQuestionCovers(session, pane, paneId));
    }

    /// <summary>Upstream's overlay belongs to the session's focused side and defers its focus to a
    /// question there (wantsFocus, deferFocusToAsk), so that question also covers the overlay pane.
    /// A pane shown alone is the focused side.</summary>
    private static string[] PaneQuestionCovers(Session session, OverlayPane pane, string paneId)
    {
        var focusedSide = !session.IsSplit || pane == (session.SplitFocused ? OverlayPane.Right : OverlayPane.Left);
        var overlayId = session.OverlayActive ? session.OverlaySurface?.PaneToken : null;
        return focusedSide && !string.IsNullOrWhiteSpace(overlayId) ? new[] { paneId, overlayId } : new[] { paneId };
    }

    private static string[] SessionPaneIds(Session session) =>
        new[] { session.Surface, session.SplitSurface, session.ScratchSurface, session.OverlaySurface }
            .Select(surface => surface?.PaneToken)
            .OfType<string>()
            .Where(token => token.Length > 0)
            .ToArray();
}
