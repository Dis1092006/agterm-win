namespace AgTerm.Core.Model;

/// <summary>A width and a height in CSS pixels, as the page measures them.</summary>
public readonly record struct HudRect(double Width, double Height);

/// <summary>The last metrics the page reported for a session: the cell it measured at FontSize, and
/// the rectangles of the session area and of each pane it lays out.</summary>
public sealed record HudSessionMetrics(double FontSize, double CellWidth, double CellHeight,
                                       HudRect Session, HudRect? Left, HudRect? Right)
{
    /// <summary>The metrics a HUD at fontSize measures against, over the session area (null pane) or
    /// one pane. A monospace cell scales linearly with the font, so one probe serves a HUD with its own
    /// font size.</summary>
    public PaneMetrics For(OverlayPane? pane, double fontSize)
    {
        HudRect? rect = pane switch
        {
            null => Session,
            OverlayPane.Left => Left,
            _ => Right,
        };
        if (rect is not { } area) return PaneMetrics.Unmeasured;
        var scale = FontSize > 0 ? fontSize / FontSize : 1;
        return new PaneMetrics(CellWidth * scale, CellHeight * scale, area.Width, area.Height,
            HudLayout.PanelPaddingWidth, HudLayout.PanelPaddingHeight);
    }
}
