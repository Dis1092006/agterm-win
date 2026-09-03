using AgTerm.Core.Protocol;

namespace AgTerm.Xterm;

public sealed record LiveDisplayMetrics(
    int X,
    int Y,
    int Width,
    int Height,
    double Scale);

public static class LiveWindowGeometry
{
    public const int VisibleMargin = 80;

    public static ControlWindowFrame Capture(
        int xPixels,
        int yPixels,
        int widthPixels,
        int heightPixels,
        int displayIndex,
        LiveDisplayMetrics display)
    {
        Validate(display);
        return new ControlWindowFrame
        {
            X = ToPoints(xPixels - display.X, display.Scale),
            Y = ToPoints(yPixels - display.Y, display.Scale),
            Width = ToPoints(widthPixels, display.Scale),
            Height = ToPoints(heightPixels, display.Scale),
            Display = displayIndex,
        };
    }

    public static (int X, int Y) ClampMove(
        int x,
        int y,
        int windowWidthPixels,
        int windowHeightPixels,
        LiveDisplayMetrics display)
    {
        Validate(display);
        var visibleMarginPixels = ToPixels(VisibleMargin, display.Scale);
        var requestedX = display.X + ToPixels(x, display.Scale);
        var requestedY = display.Y + ToPixels(y, display.Scale);
        var minimumX = display.X - windowWidthPixels + visibleMarginPixels;
        var maximumX = display.X + display.Width - visibleMarginPixels;
        var minimumY = display.Y - windowHeightPixels + visibleMarginPixels;
        var maximumY = display.Y + display.Height - visibleMarginPixels;
        return (
            Clamp(requestedX, minimumX, maximumX),
            Clamp(requestedY, minimumY, maximumY));
    }

    public static int ToPoints(int pixels, double scale) =>
        (int)Math.Round(pixels / scale, MidpointRounding.AwayFromZero);

    public static int ToPixels(int points, double scale) =>
        (int)Math.Round(points * scale, MidpointRounding.AwayFromZero);

    private static int Clamp(int value, int minimum, int maximum) =>
        Math.Min(Math.Max(value, minimum), maximum);

    private static void Validate(LiveDisplayMetrics display)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (display.Width <= 0)
            throw new ArgumentOutOfRangeException(nameof(display));
        if (display.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(display));
        if (!double.IsFinite(display.Scale) || display.Scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(display));
    }
}
