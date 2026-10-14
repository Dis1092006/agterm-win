namespace AgTerm.Core.Model;

/// <summary>The HUD the window draws now: its session, the spec and size the store keeps, and the
/// pane it is anchored to with that pane's page id, or the whole session area when Pane is null.
/// Revision changes on every open, update and close, and not on a resize or a re-measure, so the
/// page can tell a new spec from a redraw of the same one. FontSize is the font fixed at open, and
/// Metrics are the anchor's metrics at that font, null while the page has measured nothing.</summary>
public sealed record TerminalHudPlacement(Guid SessionId, HudSpec Spec, HudPanelSize Size, OverlayPane? Pane,
                                          string? PaneId, int Revision, double? FontSize,
                                          PaneMetrics? Metrics = null);

public static class TerminalHudVisibility
{
    /// <summary>Only the selected session is on screen. Zoom, the dashboard and the quick terminal
    /// hide the HUD without closing it; the quick terminal is a Windows adaptation, because the port
    /// draws it in the same page. A pane HUD needs its pane laid out and a page token. Unlike a
    /// terminal question a HUD is not hidden by the scratch: upstream draws the HUD layer above it.</summary>
    public static TerminalHudPlacement? For(AppStore store, bool zoomActive, bool dashboardOpen, bool quickTerminalVisible)
    {
        if (zoomActive || dashboardOpen || quickTerminalVisible
            || store.ActiveSession is not { Hud: { } spec, HudPanelSize: { } stored } session)
            return null;
        var size = DrawnSize(session, stored);
        if (session.HudPaneIdentity is null)
            return new TerminalHudPlacement(session.Id, spec, size, null, null, session.HudRevision, session.HudFontSize,
                MetricsFor(session, null));
        if (session.HudTargetPane is not { } pane || !session.RendersPane(pane))
            return null;
        var paneId = pane == OverlayPane.Left ? session.Surface?.PaneToken : session.SplitSurface?.PaneToken;
        return string.IsNullOrWhiteSpace(paneId)
            ? null
            : new TerminalHudPlacement(session.Id, spec, size, pane, paneId, session.HudRevision, session.HudFontSize,
                MetricsFor(session, pane));
    }

    /// <summary>The page fits markdown rows to the panel grid, which needs the metrics the store sized
    /// the HUD with: the anchor's rect and the cell at the font fixed at open.</summary>
    private static PaneMetrics? MetricsFor(Session session, OverlayPane? pane) =>
        session.HudMetrics is { } metrics ? metrics.For(pane, session.HudFontSize ?? metrics.FontSize) : null;

    // overlay.resize holds its width through re-measures until the next open or update, and tree
    // reports that width, so the panel on screen takes it too
    private static HudPanelSize DrawnSize(Session session, HudPanelSize stored) =>
        session.HudForcedWidthPercent is { } forced ? stored with { WidthPercent = forced } : stored;
}
