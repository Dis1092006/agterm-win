using AgTerm.Core.Model;

namespace AgTerm.App;

public sealed record AskRect(double X, double Y, double Width, double Height);

public static class AskAnchorGeometry
{
    /// <summary>The page lays a shown split out as ratio, a divider, then the rest
    /// (bridge.js setSplitGridColumns).</summary>
    public const double SplitDividerWidth = 4;

    public static AskRect Pane(AskRect area, OverlayPane pane, bool splitShown, double ratio)
    {
        if (!splitShown) return area;
        var content = Math.Max(0, area.Width - SplitDividerWidth);
        var left = content * ratio;
        return pane == OverlayPane.Left
            ? area with { Width = left }
            : area with { X = area.X + left + SplitDividerWidth, Width = content - left };
    }
}
