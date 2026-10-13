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

    public static (int Columns, int Rows) Box(HudSpec spec)
    {
        var widths = BodyLines(spec).Select(CellCount).ToList();
        var content = Math.Max((widths.Count == 0 ? 0 : widths.Max()) + (spec.Spinner is null ? 0 : SpinnerWidth), 1);
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

    /// <summary>Upstream clamps in the store on every path; clamping here once means no caller can
    /// keep an unclamped override. A measured width is already inside the bounds.</summary>
    public static HudPanelSize PanelSize(HudSpec spec, PaneMetrics pane)
    {
        var box = Box(spec);
        var width = ClampSizePercent(spec.SizePercent ?? WidthPercent(box, pane, MaxWidthPercent(spec)), spec);
        return new HudPanelSize(width, HeightPercent(box, pane), HeightPoints(box, pane));
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
