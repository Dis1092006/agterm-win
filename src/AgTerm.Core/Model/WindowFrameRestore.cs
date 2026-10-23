namespace AgTerm.Core.Model;

/// <summary>A display's full bounds and its work area (without the taskbar), in physical pixels.</summary>
public sealed record FrameDisplay(PixelRect Bounds, PixelRect WorkArea, double Scale);

/// <summary>Port of upstream WindowGeometry.bestDisplayIndex and constrain, which fit a saved frame
/// to the displays attached now, so a frame saved on a display that is gone, or above the top edge,
/// never restores off screen or with its title bar hidden.</summary>
public static class WindowFrameRestore
{
    /// <summary>The smallest window, in points, as window.resize allows.</summary>
    public const int MinimumWidth = 320;
    public const int MinimumHeight = 240;

    public static PixelRect Constrain(PixelRect frame, IReadOnlyList<FrameDisplay> displays, int primaryIndex)
    {
        ArgumentNullException.ThrowIfNull(displays);
        if (displays.Count == 0)
            return frame;

        var display = displays[BestDisplayIndex(frame, displays) ?? Math.Clamp(primaryIndex, 0, displays.Count - 1)];
        var area = display.WorkArea;
        var width = Math.Min(Math.Max(frame.Width, Scaled(MinimumWidth, display.Scale)), area.Width);
        var height = Math.Min(Math.Max(frame.Height, Scaled(MinimumHeight, display.Scale)), area.Height);
        return new PixelRect(
            Math.Min(Math.Max(frame.X, area.X), area.X + area.Width - width),
            Math.Min(Math.Max(frame.Y, area.Y), area.Y + area.Height - height),
            width,
            height);
    }

    private static int? BestDisplayIndex(PixelRect frame, IReadOnlyList<FrameDisplay> displays)
    {
        int? bestIndex = null;
        var bestArea = 0L;
        for (var index = 0; index < displays.Count; index++)
        {
            var area = IntersectionArea(frame, displays[index].Bounds);
            if (area > bestArea)
            {
                bestArea = area;
                bestIndex = index;
            }
        }
        return bestIndex;
    }

    private static long IntersectionArea(PixelRect left, PixelRect right)
    {
        var width = Math.Min(left.X + left.Width, right.X + right.Width) - Math.Max(left.X, right.X);
        var height = Math.Min(left.Y + left.Height, right.Y + right.Height) - Math.Max(left.Y, right.Y);
        return width > 0 && height > 0 ? (long)width * height : 0;
    }

    private static int Scaled(int points, double scale) =>
        (int)Math.Round(points * scale, MidpointRounding.AwayFromZero);
}
