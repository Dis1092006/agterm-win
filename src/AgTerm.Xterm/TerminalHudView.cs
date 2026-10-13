using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>One drawn row of the panel; Dim marks a detail row.</summary>
public sealed record TerminalHudRow(string Text, bool Dim);

/// <summary>What the page draws for the window's HUD. The page uses these rows and sizes as given
/// and measures no text for layout, so the panel on screen and tree come from the same numbers.
/// HeightPoints is null for an unmeasured pane, and the page then uses HeightPercent.</summary>
public sealed record TerminalHudView(
    string? PaneId,
    IReadOnlyList<TerminalHudRow> Rows,
    int WidthPercent,
    double? HeightPoints,
    int HeightPercent,
    string Position,
    bool Sticky,
    bool Frame,
    string? Background,
    string? Foreground,
    double? FontSize,
    IReadOnlyList<string> SpinnerFrames,
    double SpinnerInterval,
    int Revision);

public static class TerminalHudViews
{
    public static TerminalHudView From(TerminalHudPlacement placement)
    {
        var spec = placement.Spec;
        return new TerminalHudView(
            placement.PaneId,
            Rows(spec),
            placement.Size.WidthPercent,
            placement.Size.HeightPoints,
            placement.Size.HeightPercent,
            HudPositions.Wire(spec.Position),
            spec.Sticky,
            spec.Frame,
            CssColor(spec.BackgroundColor),
            CssColor(spec.TextColor),
            placement.FontSize,
            spec.Spinner is { } frames ? HudSpinners.Frames(frames) : Array.Empty<string>(),
            spec.Spinner is { } interval ? HudSpinners.IntervalSeconds(interval) : 0,
            placement.Revision);
    }

    private static TerminalHudRow[] Rows(HudSpec spec)
    {
        var lines = HudLayout.BodyLines(spec);
        var messageRows = HudLayout.Wrap(spec.Message, HudLayout.MaxColumns).Count;
        // BodyLines puts the empty separator between the halves only when the message has rows
        var detailStart = messageRows > 0 && lines.Count > messageRows ? messageRows + 1 : messageRows;
        return lines.Select((text, index) => new TerminalHudRow(text, index >= detailStart)).ToArray();
    }

    // the wire takes a color with or without its '#' and tree echoes it as sent, but CSS needs the '#'
    private static string? CssColor(string? hex) =>
        hex is null || hex.StartsWith('#') ? hex : "#" + hex;
}
