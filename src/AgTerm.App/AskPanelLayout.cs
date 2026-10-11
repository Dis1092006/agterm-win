namespace AgTerm.App;

public sealed record AskButtonArrangement(bool Row, double ButtonWidth);

/// <summary>Upstream AskDialogView sizing for the GUI style: an 8 px cell, padding 2 cells,
/// 1.5 cells between blocks and 1 cell between buttons.</summary>
public static class AskPanelLayout
{
    public const double GuiCell = 8;
    public const double PanelPadding = 2 * GuiCell;
    public const double BlockSpacing = 1.5 * GuiCell;
    public const double ButtonSpacing = GuiCell;

    /// <summary>The width cap counts in terminal cells even for the GUI style, as upstream does.</summary>
    public static double TerminalCell(double fontSize) => Math.Max(8, fontSize * 0.6);

    /// <summary>A fixed width is that percent of the anchor; otherwise the natural width, capped at
    /// 90 percent of the anchor and 72 terminal cells.</summary>
    public static double MaxPanelWidth(double anchorWidth, double fontSize, int? widthPercent) =>
        widthPercent is { } percent
            ? Math.Max(0, anchorWidth) * Math.Clamp(percent, 10, 100) / 100.0
            : Math.Min(Math.Max(0, anchorWidth * 0.9), 72 * TerminalCell(fontSize));

    public static double MaxPanelHeight(double anchorHeight) => Math.Max(0, anchorHeight - 2 * GuiCell);

    /// <summary>Every button takes the widest natural width. A row when it fits, else a column
    /// whose buttons are capped at the available width so a long label wraps.</summary>
    public static AskButtonArrangement Arrange(IReadOnlyList<double> naturalWidths, double available)
    {
        var widest = naturalWidths.Count == 0 ? 0 : naturalWidths.Max();
        var row = widest * naturalWidths.Count + ButtonSpacing * Math.Max(0, naturalWidths.Count - 1);
        return row <= available
            ? new AskButtonArrangement(true, widest)
            : new AskButtonArrangement(false, Math.Min(widest, available));
    }
}
