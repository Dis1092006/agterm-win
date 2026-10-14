using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>One styled run of a markdown row; Link is set only for a URL HudLinkPolicy accepts.</summary>
public sealed record TerminalHudRun(string Text, bool Bold, bool Italic, bool Strike, bool Dim, bool Underline, string? Link);

/// <summary>One drawn row of the panel; Dim marks a plain detail row. A plain row is its text; a
/// markdown row also carries its runs.</summary>
public sealed record TerminalHudRow(string Text, bool Dim, IReadOnlyList<TerminalHudRun>? Runs = null)
{
    /// <summary>By value over the runs: the window skips a view equal to the last one, and a list
    /// compared by reference would make every view new.</summary>
    public bool Equals(TerminalHudRow? other) =>
        other is not null && Text == other.Text && Dim == other.Dim
        && (Runs is null ? other.Runs is null : other.Runs is not null && Runs.SequenceEqual(other.Runs));

    public override int GetHashCode() => HashCode.Combine(Text, Dim);
}

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
    int Revision,
    bool Markdown = false);

public static class TerminalHudViews
{
    public static TerminalHudView From(TerminalHudPlacement placement)
    {
        var spec = placement.Spec;
        return new TerminalHudView(
            placement.PaneId,
            spec.Markdown ? MarkdownRows(placement) : Rows(spec),
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
            placement.Revision,
            Markdown: spec.Markdown);
    }

    private static IReadOnlyList<TerminalHudRow> Rows(HudSpec spec)
    {
        var lines = HudLayout.BodyLines(spec);
        var messageRows = HudLayout.Wrap(spec.Message, HudLayout.MaxColumns).Count;
        // BodyLines puts the empty separator between the halves only when the message has rows
        var detailStart = messageRows > 0 && lines.Count > messageRows ? messageRows + 1 : messageRows;
        return lines.Select((text, index) => new TerminalHudRow(text, index >= detailStart)).ToArray();
    }

    private static IReadOnlyList<TerminalHudRow> MarkdownRows(TerminalHudPlacement placement)
    {
        var grid = HudLayout.PaintGrid(placement.Spec, placement.Size, placement.Metrics ?? PaneMetrics.Unmeasured);
        return HudLayout.MarkdownBody(placement.Spec, grid).Rows
            .Select(row => new TerminalHudRow(string.Concat(row.Select(run => run.Text)), false, row.Select(ToRun).ToList()))
            .ToList();
    }

    private static TerminalHudRun ToRun(HudMarkdownRun run) => new(
        run.Text,
        run.Style.HasFlag(HudMarkdownStyle.Bold),
        run.Style.HasFlag(HudMarkdownStyle.Italic),
        run.Style.HasFlag(HudMarkdownStyle.Strikethrough),
        run.Style.HasFlag(HudMarkdownStyle.Dim),
        run.Style.HasFlag(HudMarkdownStyle.Underline),
        run.Link);

    // the wire takes a color with or without its '#' and tree echoes it as sent, but CSS needs the '#'
    private static string? CssColor(string? hex) =>
        hex is null || hex.StartsWith('#') ? hex : "#" + hex;
}
