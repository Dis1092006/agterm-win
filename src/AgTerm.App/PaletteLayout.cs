namespace AgTerm.App;

public static class PaletteLayout
{
    public const double MaximumResultsHeight = 320;
    public const double MinimumResultsHeight = 48;

    private const double FullPanelHeight = 459;
    private const double FullTopMargin = 64;
    private const double CompactTopMargin = 24;
    private const double BottomMargin = 24;
    private const double PanelChromeHeight = 51;

    public static double PanelTopMargin(double availableHeight)
    {
        Validate(availableHeight);
        return availableHeight >= FullPanelHeight ? FullTopMargin : CompactTopMargin;
    }

    public static double ResultHeight(double availableHeight)
    {
        Validate(availableHeight);
        var availableResultsHeight = availableHeight
            - PanelTopMargin(availableHeight)
            - BottomMargin
            - PanelChromeHeight;
        return Math.Clamp(
            availableResultsHeight,
            MinimumResultsHeight,
            MaximumResultsHeight);
    }

    private static void Validate(double availableHeight)
    {
        if (!double.IsFinite(availableHeight) || availableHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(availableHeight));
    }
}