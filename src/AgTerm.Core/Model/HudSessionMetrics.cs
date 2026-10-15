namespace AgTerm.Core.Model;

/// <summary>A width and a height in CSS pixels, as the page measures them.</summary>
public readonly record struct HudRect(double Width, double Height);

/// <summary>The last metrics the page reported for a session: the cell it measured at FontSize, the
/// rectangles of the session area and of each pane it lays out, and the renderer font the page draws
/// the terminal with. FontSize is the drawn panel's font while one is up, so only BaseFontSize names
/// the font a HUD with no font of its own takes.</summary>
public sealed record HudSessionMetrics(double FontSize, double CellWidth, double CellHeight,
                                       HudRect Session, HudRect? Left, HudRect? Right, double BaseFontSize)
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
        var scale = UsableFontSize(FontSize) is { } probe ? fontSize / probe : 1;
        return new PaneMetrics(CellWidth * scale, CellHeight * scale, area.Width, area.Height,
            HudLayout.PanelPaddingWidth, HudLayout.PanelPaddingHeight);
    }

    /// <summary>Null for a font of 0, below 0 or not finite: as the probe font it would scale the cell
    /// to nothing or to NaN, and a HUD that took it as its font would keep a collapsed panel until the
    /// next open.</summary>
    internal static double? UsableFontSize(double? size) =>
        size is { } value && double.IsFinite(value) && value > 0 ? value : null;
}
