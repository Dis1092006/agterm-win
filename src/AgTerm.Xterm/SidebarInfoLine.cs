using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>The size of a session info line and of the row that draws it (upstream SidebarInfoMetrics,
/// 80f822bf). The row keeps its plain height as a band for the title and adds the line's height under it;
/// the line rises into the band by <see cref="Lift"/>, which upstream gives back as bottom padding.</summary>
public static class SidebarInfoMetrics
{
    public const double Lift = 6;

    // Segoe UI's ascent plus descent over its em square, the height upstream reads from the font
    private const double LineSpacing = 1.33;

    public static double FontSize(double baseSize) => Math.Max(9, baseSize - 1.5);

    public static double LineHeight(double baseSize) => Math.Ceiling(FontSize(baseSize) * LineSpacing);

    public static double RowHeight(double baseSize) => AppSettings.SidebarRowHeight(baseSize) + LineHeight(baseSize);

    /// <summary>The line's top, measured from the row's top.</summary>
    public static double Top(double baseSize) => AppSettings.SidebarRowHeight(baseSize) - Lift;
}

/// <summary>The colors under a row's info line: its plain text color and the inks of its colored spans.
/// A row-style theme paints the whole row, so the line takes that theme; otherwise the line sits on the
/// sidebar and takes the sidebar's. A name tint never reaches the line.</summary>
public sealed record SidebarInfoInks(string Foreground, SidebarTintInks Inks)
{
    public static SidebarInfoInks Default { get; } = new("#cccccc", SidebarTintInks.Dark);

    public static SidebarInfoInks For(SidebarRowLook look, string sidebarForeground, SidebarTintInks sidebarInks)
    {
        ArgumentNullException.ThrowIfNull(look);
        ArgumentNullException.ThrowIfNull(sidebarInks);
        return look.Row is { } row
            ? new SidebarInfoInks(row.Foreground, row.Inks ?? sidebarInks)
            : new SidebarInfoInks(sidebarForeground, sidebarInks);
    }
}

/// <summary>One drawn run of an info line: its text, a "#rrggbb" color with an opacity, and its style.</summary>
public sealed record SidebarInfoRun(string Text, string Color, double Opacity, bool Bold = false, bool Italic = false,
                                    bool Code = false);

/// <summary>Turns an info line's spans into drawn runs (upstream SidebarInfoCell's attributed string).</summary>
public static class SidebarInfoLine
{
    /// <summary>Plain text is the foreground, softer than the name and softer still when dim; a selected
    /// row raises it. A colored span keeps its ink whether the row is selected or not.</summary>
    public static IReadOnlyList<SidebarInfoRun> Runs(IReadOnlyList<SessionInfoSpan> spans, SidebarInfoInks inks, bool selected)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(inks);
        return spans.Select(span => span.Color is { } color
                ? new SidebarInfoRun(span.Text, inks.Inks.For(color), span.Dim ? 0.55 : 1.0, span.Bold, span.Italic, span.Code)
                : new SidebarInfoRun(span.Text, inks.Foreground, PlainOpacity(span.Dim, selected), span.Bold, span.Italic, span.Code))
            .ToArray();
    }

    private static double PlainOpacity(bool dim, bool selected) => selected
        ? dim ? 0.6 : 0.85
        : dim ? 0.38 : 0.62;
}
