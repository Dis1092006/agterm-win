namespace AgTerm.App;

public readonly record struct QuickPanelFrame(int X, int Y, int Width, int Height);

public static class QuickPanelGeometry
{
    public static QuickPanelFrame Frame(QuickPanelFrame workArea, double scale, bool zoomed)
    {
        if (zoomed) return workArea;
        var width = (int)Math.Round(Math.Min(workArea.Width * 0.9, 1100 * scale));
        var height = (int)Math.Round(Math.Min(workArea.Height * 0.9, 700 * scale));
        return new(workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2, width, height);
    }
}
