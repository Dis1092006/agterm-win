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
    /// <summary>Port of upstream SessionAskInput.visible. Only the selected session is on screen;
    /// zoom, the dashboard and the quick terminal hide terminal questions without resolving them; a
    /// pane question needs its pane laid out and hides under the scratch, while a session-wide one
    /// draws above it. The quick terminal is a Windows adaptation: upstream floats it in its own
    /// window above the question, here it shares the page and would sit under the question.</summary>
    public static TerminalAskPlacement? For(AppStore store, bool zoomActive, bool dashboardOpen, bool quickTerminalVisible)
    {
        if (zoomActive || dashboardOpen || quickTerminalVisible
            || store.ActiveSession is not { AskPending: { } ask } session)
            return null;
        if (session.AskPaneIdentity is null)
            return new TerminalAskPlacement(session.Id, ask, null, null, SessionPaneIds(session));
        if (session.AskTargetPane is not { } pane || !session.RendersPane(pane) || session.ScratchActive)
            return null;
        var paneId = pane == OverlayPane.Left ? session.Surface?.PaneToken : session.SplitSurface?.PaneToken;
        return string.IsNullOrWhiteSpace(paneId) ? null : new TerminalAskPlacement(session.Id, ask, pane, paneId, new[] { paneId });
    }

    private static string[] SessionPaneIds(Session session) =>
        new[] { session.Surface, session.SplitSurface, session.ScratchSurface, session.OverlaySurface }
            .Select(surface => surface?.PaneToken)
            .OfType<string>()
            .Where(token => token.Length > 0)
            .ToArray();
}
