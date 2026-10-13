namespace AgTerm.Core.Model;

/// <summary>The message panel a control client posts over a session (upstream HudSpec).
/// BackgroundColor comes from the wire's color. BackgroundColor and FontSize are fixed at open:
/// an update stores HoldingCreationFields(live).</summary>
public sealed record HudSpec(
    string Message,
    string? Detail = null,
    HudSpinner? Spinner = null,
    string? BackgroundColor = null,
    string? TextColor = null,
    int? SizePercent = null,
    HudPosition Position = HudPosition.Center,
    double? HideAfter = null,
    bool Markdown = false,
    double? FontSize = null,
    bool Sticky = false,
    bool Frame = true)
{
    public const int MaxTextLength = 256;
    public const int MaxMarkdownLength = 4096;

    /// <summary>Upstream's scheduler converts seconds to nanoseconds in a UInt64, which a large
    /// enough Double traps; the bound is kept so the two hosts refuse the same values.</summary>
    public const double MaxHideAfter = 86400;

    public const double MinFontSize = 6;
    public const double MaxFontSize = 72;

    /// <summary>NaN fails both comparisons, so only a finite size inside the range passes.</summary>
    public static bool IsValidFontSize(double points) => points >= MinFontSize && points <= MaxFontSize;

    public static bool IsValidHideAfter(double seconds) =>
        double.IsFinite(seconds) && seconds >= 0 && seconds <= MaxHideAfter;

    /// <summary>The one spelling of "is this panel timed", so the timer and tree agree on what null means.</summary>
    public double EffectiveHideAfter => HideAfter ?? 0;

    public HudSpec HoldingCreationFields(HudSpec live) =>
        this with { BackgroundColor = live.BackgroundColor, FontSize = live.FontSize };
}

public enum HudSpinner
{
    Bar,
    Braille,
    Circle,
    Blocks,
    Dot,
}

public static class HudSpinners
{
    /// <summary>ASCII, so it renders in any font.</summary>
    public const HudSpinner Default = HudSpinner.Bar;

    /// <summary>tree's spelling of a panel with no spinner. Accepted on input so a caller can echo
    /// tree back; it is not a style.</summary>
    public const string NoneName = "none";

    private static readonly string[] BarFrames = { "|", "/", "-", "\\" };
    private static readonly string[] BrailleFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
    private static readonly string[] CircleFrames = { "◐", "◓", "◑", "◒" };
    private static readonly string[] BlocksFrames = { "▁", "▃", "▄", "▅", "▆", "▇", "▆", "▅", "▄", "▃" };

    // upstream's helper word-splits the frame list, so the off frame is a no-break space; it still
    // takes one cell, so the message does not shift on the blink
    private static readonly string[] DotFrames = { "●", "\u00A0" };

    public static HudSpinner? FromWire(string raw) => raw switch
    {
        "bar" => HudSpinner.Bar,
        "braille" => HudSpinner.Braille,
        "circle" => HudSpinner.Circle,
        "blocks" => HudSpinner.Blocks,
        "dot" => HudSpinner.Dot,
        _ => null,
    };

    public static string Wire(HudSpinner style) => style switch
    {
        HudSpinner.Bar => "bar",
        HudSpinner.Braille => "braille",
        HudSpinner.Circle => "circle",
        HudSpinner.Blocks => "blocks",
        HudSpinner.Dot => "dot",
        _ => throw new ArgumentOutOfRangeException(nameof(style)),
    };

    public static IReadOnlyList<string> Frames(HudSpinner style) => style switch
    {
        HudSpinner.Bar => BarFrames,
        HudSpinner.Braille => BrailleFrames,
        HudSpinner.Circle => CircleFrames,
        HudSpinner.Blocks => BlocksFrames,
        HudSpinner.Dot => DotFrames,
        _ => throw new ArgumentOutOfRangeException(nameof(style)),
    };

    public static double IntervalSeconds(HudSpinner style) => style switch
    {
        HudSpinner.Bar => 0.1,
        HudSpinner.Braille or HudSpinner.Blocks => 0.08,
        HudSpinner.Circle => 0.12,
        // a blink at an animation's rate reads as a flicker rather than a pulse
        HudSpinner.Dot => 0.45,
        _ => throw new ArgumentOutOfRangeException(nameof(style)),
    };

    /// <summary>Lists none too: a rejection naming only the styles would refuse a value the
    /// dispatcher takes.</summary>
    public static string AcceptedNamesList => string.Join("|", AcceptedNames);

    public static string AcceptedNamesPhrase => string.Join(", ", AcceptedNames);

    private static IEnumerable<string> AcceptedNames => Enum.GetValues<HudSpinner>().Select(Wire).Append(NoneName);
}

/// <summary>The nine anchors of the panel, spelled as session.background's. A type of its own
/// because the HUD also takes the top and bottom aliases and session.background does not.</summary>
public enum HudPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    Center,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

/// <summary>One axis of an anchor, so both offsets are the same math over a different extent.</summary>
public enum HudBand
{
    Leading,
    Middle,
    Trailing,
}

public static class HudPositions
{
    public const HudPosition Default = HudPosition.Center;

    /// <summary>Percent of the pane kept clear at an anchored edge; a sticky panel keeps none.</summary>
    public const int EdgeMarginPercent = 10;

    private static readonly string[] AliasNames = { "top", "bottom" };

    /// <summary>The one entry point from text to a position, so the dispatcher and the CLI accept
    /// the same names. Case-sensitive, as upstream.</summary>
    public static HudPosition? Parse(string raw) => raw switch
    {
        "top-left" => HudPosition.TopLeft,
        "top-center" => HudPosition.TopCenter,
        "top-right" => HudPosition.TopRight,
        "center-left" => HudPosition.CenterLeft,
        "center" => HudPosition.Center,
        "center-right" => HudPosition.CenterRight,
        "bottom-left" => HudPosition.BottomLeft,
        "bottom-center" => HudPosition.BottomCenter,
        "bottom-right" => HudPosition.BottomRight,
        // upstream's first spellings, kept for old callers; tree reports the canonical anchor
        "top" => HudPosition.TopCenter,
        "bottom" => HudPosition.BottomCenter,
        _ => null,
    };

    public static string Wire(HudPosition position) => position switch
    {
        HudPosition.TopLeft => "top-left",
        HudPosition.TopCenter => "top-center",
        HudPosition.TopRight => "top-right",
        HudPosition.CenterLeft => "center-left",
        HudPosition.Center => "center",
        HudPosition.CenterRight => "center-right",
        HudPosition.BottomLeft => "bottom-left",
        HudPosition.BottomCenter => "bottom-center",
        HudPosition.BottomRight => "bottom-right",
        _ => throw new ArgumentOutOfRangeException(nameof(position)),
    };

    public static HudBand VerticalBand(HudPosition position) => position switch
    {
        HudPosition.TopLeft or HudPosition.TopCenter or HudPosition.TopRight => HudBand.Leading,
        HudPosition.CenterLeft or HudPosition.Center or HudPosition.CenterRight => HudBand.Middle,
        _ => HudBand.Trailing,
    };

    public static HudBand HorizontalBand(HudPosition position) => position switch
    {
        HudPosition.TopLeft or HudPosition.CenterLeft or HudPosition.BottomLeft => HudBand.Leading,
        HudPosition.TopCenter or HudPosition.Center or HudPosition.BottomCenter => HudBand.Middle,
        _ => HudBand.Trailing,
    };

    /// <summary>Lists the aliases too: a rejection naming only the anchors would refuse values the
    /// dispatcher takes.</summary>
    public static string AcceptedNamesList => string.Join("|", AcceptedNames);

    public static string AcceptedNamesPhrase => string.Join(", ", AcceptedNames);

    public static string ValidNamesPhrase => string.Join(", ", ValidNames);

    private static IEnumerable<string> ValidNames => Enum.GetValues<HudPosition>().Select(Wire);

    private static IEnumerable<string> AcceptedNames => ValidNames.Concat(AliasNames);
}
