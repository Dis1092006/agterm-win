using System.Text;

namespace AgTerm.Core.Model;

/// <summary>Cell and pane sizes the page measures, in CSS pixels. PaddingWidth and PaddingHeight
/// are the panel's own padding per side, which holds no cells.</summary>
public sealed record PaneMetrics(
    double CellWidth,
    double CellHeight,
    double PaneWidth,
    double PaneHeight,
    double PaddingWidth = 0,
    double PaddingHeight = 0)
{
    public static readonly PaneMetrics Unmeasured = new(0, 0, 0, 0);
}

/// <summary>The panel's share of the pane on each axis. HeightPoints is what the panel is drawn at,
/// so it does not grow with the window; null for an unmeasured pane, where HeightPercent stands in.</summary>
public sealed record HudPanelSize(int WidthPercent, int HeightPercent, double? HeightPoints = null);

/// <summary>Port of upstream HudLayout: message to a cell box, box to the pane percentages tree
/// reports. Counts Unicode scalars, never UTF-16 units or display columns, as upstream does; a wide
/// glyph can overflow the frame.</summary>
public static class HudLayout
{
    public const int MaxColumns = 60;
    public const int MaxSizePercent = 80;
    public const int MinSizePercent = 10;
    public const int HorizontalPadding = 2;
    public const int VerticalPadding = 1;

    /// <summary>The spinner glyph and its space, reserved so turning the spinner on cannot rewrap text.</summary>
    public const int SpinnerWidth = 2;

    public const double PanelPaddingWidth = 8;
    public const double PanelPaddingHeight = 6;

    public static int CellCount(string text)
    {
        var count = 0;
        foreach (var _ in text.EnumerateRunes()) count++;
        return count;
    }

    /// <summary>The unit of the length caps: the same scalars, on the same NFC form Wrap lays out.</summary>
    public static int TextLength(string text) => CellCount(text.Normalize(NormalizationForm.FormC));

    /// <summary>Blank lines are dropped, which keeps the one empty line BodyLines inserts the only
    /// separator between message and detail.</summary>
    public static IReadOnlyList<string> Wrap(string text, int columns)
    {
        var width = Math.Max(columns, 1);
        var lines = new List<string>();
        foreach (var paragraph in text.Normalize(NormalizationForm.FormC).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var current = "";
            foreach (var chunk in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var word = chunk;
                while (CellCount(word) > width)
                {
                    if (current.Length > 0)
                    {
                        lines.Add(current);
                        current = "";
                    }
                    var cut = Utf16OffsetOfScalar(word, width);
                    lines.Add(word[..cut]);
                    word = word[cut..];
                }
                if (word.Length == 0) continue;
                if (current.Length == 0)
                    current = word;
                else if (CellCount(current) + 1 + CellCount(word) <= width)
                    current += " " + word;
                else
                {
                    lines.Add(current);
                    current = word;
                }
            }
            if (current.Length > 0) lines.Add(current);
        }
        return lines;
    }

    /// <summary>Plain text wraps at MaxColumns whatever the pane or the caller's width.</summary>
    public static IReadOnlyList<string> BodyLines(HudSpec spec)
    {
        var lines = Wrap(spec.Message, MaxColumns).ToList();
        var detail = Wrap(spec.Detail ?? "", MaxColumns);
        if (detail.Count == 0) return lines;
        if (lines.Count > 0) lines.Add("");
        lines.AddRange(detail);
        return lines;
    }

    /// <summary>Port of upstream HudLayout.box: the wrapped content plus the frame padding. It decides how
    /// big the panel is; PaintGrid decides where the text sits inside it. A markdown message is measured
    /// by its rows at wrapColumns.</summary>
    public static (int Columns, int Rows) Box(HudSpec spec, int wrapColumns = MaxColumns)
    {
        var widths = spec.Markdown
            ? MarkdownRows(spec, wrapColumns).Select(HudMarkdown.Width).ToList()
            : BodyLines(spec).Select(CellCount).ToList();
        var content = Math.Max(widths.DefaultIfEmpty(0).Max() + (spec.Spinner is not null ? SpinnerWidth : 0), 1);
        return (content + HorizontalPadding * 2, Math.Max(widths.Count, 1) + VerticalPaddingFor(spec) * 2);
    }

    public static int VerticalPaddingFor(HudSpec spec) => spec.Frame ? VerticalPadding : 0;

    /// <summary>A sticky panel off center owes no edge margin, so it may span the pane.</summary>
    public static int MaxWidthPercent(HudSpec spec) =>
        spec.Sticky && spec.Position != HudPosition.Center ? 100 : MaxSizePercent;

    public static int ClampSizePercent(int requested, HudSpec spec) =>
        Math.Min(Math.Max(requested, MinSizePercent), MaxWidthPercent(spec));

    /// <summary>An unmeasured pane gets maxPercent: nothing is known to fit, so the panel takes the
    /// most room allowed.</summary>
    public static int WidthPercent((int Columns, int Rows) box, PaneMetrics pane, int maxPercent = MaxSizePercent)
    {
        var needed = Math.Max(box.Columns, 0) * pane.CellWidth + pane.PaddingWidth * 2;
        var measured = Percent(needed, pane.PaneWidth) ?? maxPercent;
        return Math.Min(Math.Max(measured, MinSizePercent), maxPercent);
    }

    /// <summary>No 10 percent floor: the box already carries its padding rows, so the smallest panel
    /// is as tall as its text. An unmeasured pane gets 10, because 80 percent of a pane is a cover,
    /// not a message.</summary>
    public static int HeightPercent((int Columns, int Rows) box, PaneMetrics pane)
    {
        var needed = Math.Max(box.Rows, 0) * pane.CellHeight + pane.PaddingHeight * 2;
        if (Percent(needed, pane.PaneHeight) is not { } measured) return MinSizePercent;
        return Math.Min(Math.Max(measured, 1), MaxSizePercent);
    }

    public static double? HeightPoints((int Columns, int Rows) box, PaneMetrics pane)
    {
        // written as a positive test so a NaN extent counts as unmeasured, as in Swift's guard
        if (!(pane.PaneHeight > 0 && pane.CellHeight > 0)) return null;
        var needed = Math.Max(box.Rows, 0) * pane.CellHeight + pane.PaddingHeight * 2;
        return Math.Min(needed, pane.PaneHeight * MaxSizePercent / 100);
    }

    /// <summary>The ONE place both axes are decided from a single box: the caller's percent reaches the
    /// width alone, clamped as the store keeps it, and the height is always measured. Upstream clamps in
    /// the store on every path; clamping here once means no caller can keep an unclamped override. A
    /// measured width is already inside the bounds.</summary>
    public static HudPanelSize PanelSize(HudSpec spec, PaneMetrics pane)
    {
        var box = Box(spec, WrapColumns(spec, pane));
        var width = ClampSizePercent(spec.SizePercent ?? WidthPercent(box, pane, MaxWidthPercent(spec)), spec);
        return new HudPanelSize(width, HeightPercent(box, pane), HeightPoints(box, pane));
    }

    /// <summary>The width a markdown message wraps at before its panel exists: the text columns of the
    /// widest panel this pane allows, so a row that cannot fit wraps rather than being clipped. The
    /// caller's percent is clamped as the store clamps it, or the height is budgeted for a width the
    /// panel never has. MaxColumns for a plain message and for an unmeasured pane.</summary>
    public static int WrapColumns(HudSpec spec, PaneMetrics pane)
    {
        var widest = new HudPanelSize(
            spec.SizePercent is { } requested ? ClampSizePercent(requested, spec) : MaxWidthPercent(spec),
            MaxSizePercent);
        return spec.Markdown && PanelGrid(widest, pane) is { } grid ? TextColumns(grid.Columns, spec) : MaxColumns;
    }

    /// <summary>What a panel columns wide leaves for text after its padding and the spinner gutter.
    /// MaxColumns bounds it only while the app measures the width: a caller who set one asked for text
    /// that wide.</summary>
    public static int TextColumns(int columns, HudSpec spec)
    {
        var available = Math.Max(columns - HorizontalPadding * 2 - (spec.Spinner is not null ? SpinnerWidth : 0), 1);
        return spec.SizePercent is null ? Math.Min(available, MaxColumns) : available;
    }

    /// <summary>The cell grid the panel itself gets: its width percent of the pane and its height in
    /// points, each less the panel padding, over one cell. The percent stands in for the height only
    /// when no points were measured. Null for an unmeasured pane or a pane smaller than its padding.</summary>
    public static (int Columns, int Rows)? PanelGrid(HudPanelSize size, PaneMetrics pane)
    {
        if (pane.CellWidth <= 0 || pane.CellHeight <= 0 || pane.PaneWidth <= 0 || pane.PaneHeight <= 0)
            return null;
        var columns = (int)((pane.PaneWidth * size.WidthPercent / 100 - pane.PaddingWidth * 2) / pane.CellWidth);
        var height = size.HeightPoints ?? pane.PaneHeight * size.HeightPercent / 100;
        // the points were built from whole rows, and the division can land a hair under the integer
        var rows = (int)((height - pane.PaddingHeight * 2) / pane.CellHeight + 0.001);
        return columns > 0 && rows > 0 ? (columns, rows) : null;
    }

    /// <summary>The panel's own grid, or the content box when nothing was measured. size must be the
    /// effective one the panel took, forced width included.</summary>
    public static (int Columns, int Rows) PaintGrid(HudSpec spec, HudPanelSize size, PaneMetrics pane) =>
        PanelGrid(size, pane) ?? Box(spec);

    /// <summary>A markdown spec laid out unclipped: the message rows at columns, then, when a detail is
    /// set, a blank row and the detail wrapped plain and dimmed.</summary>
    public static IReadOnlyList<IReadOnlyList<HudMarkdownRun>> MarkdownRows(HudSpec spec, int columns)
    {
        var rows = HudMarkdown.Rows(HudMarkdown.Lines(spec.Message), columns).ToList();
        var detail = Wrap(spec.Detail ?? "", columns);
        if (detail.Count == 0)
            return rows;
        rows.Add(Array.Empty<HudMarkdownRun>());
        rows.AddRange(detail.Select(line => (IReadOnlyList<HudMarkdownRun>)new[] { new HudMarkdownRun(line, HudMarkdownStyle.Dim) }));
        return rows;
    }

    /// <summary>The rows the page draws: wrapped at the grid less its padding and the spinner gutter,
    /// clipped where they still do not fit, and every row after the first indented by the gutter.
    /// BlockWidth is the widest drawn row, gutter included, and never 0.</summary>
    public static (IReadOnlyList<IReadOnlyList<HudMarkdownRun>> Rows, int BlockWidth) MarkdownBody(HudSpec spec, (int Columns, int Rows) grid)
    {
        var gutter = spec.Spinner is not null ? SpinnerWidth : 0;
        var rows = HudMarkdown.Fitted(
            MarkdownRows(spec, TextColumns(grid.Columns, spec)),
            grid.Columns - HorizontalPadding * 2 - gutter,
            grid.Rows - VerticalPaddingFor(spec) * 2);
        var blockWidth = Math.Max(rows.Select(HudMarkdown.Width).DefaultIfEmpty(0).Max() + gutter, 1);
        // the page puts the spinner glyph in front of the first row, so the rest hang under the text
        var indent = new HudMarkdownRun(new string(' ', gutter), HudMarkdownStyle.None);
        var indented = rows
            .Select((row, index) => index > 0 && row.Count > 0 && gutter > 0
                ? (IReadOnlyList<HudMarkdownRun>)row.Prepend(indent).ToList()
                : row)
            .ToList();
        return (indented, blockWidth);
    }

    /// <summary>Rounds up so a panel never lands a cell short of its content.</summary>
    private static int? Percent(double size, double available) =>
        available > 0 ? (int)Math.Ceiling(size / available * 100) : null;

    private static int Utf16OffsetOfScalar(string text, int scalars)
    {
        var offset = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (scalars-- <= 0) break;
            offset += rune.Utf16SequenceLength;
        }
        return offset;
    }
}
