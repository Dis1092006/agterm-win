using AgTerm.Core.Model;

namespace AgTerm.Xterm;

public sealed record RendererSettingsSnapshot(
    string FontFamily,
    double FontSize,
    string ForegroundColor,
    string BackgroundColor,
    string CursorColor,
    string SelectionBackgroundColor,
    double BackgroundOpacity,
    int BackgroundBlur,
    bool CursorBlink,
    string CursorStyle,
    double ScrollSensitivity,
    int Scrollback,
    bool RightClickPaste,
    bool ScreenReaderMode)
{
    public IReadOnlyList<string>? AnsiColors { get; init; }

    public string? CursorTextColor { get; init; }

    public string? SelectionForegroundColor { get; init; }

    public bool PreservePaneFontSizes { get; init; }

    /// <summary>"selection", "clipboard" or "off": whether a mouse selection is copied.</summary>
    public string CopyOnSelect { get; init; } = "selection";

    private const string DefaultFontFamily = "Cascadia Mono, Consolas, monospace";
    private const double DefaultFontSize = 14.0;
    // Theme/DarkTheme/FollowSystemAppearance resolution belongs to D3's live theme catalog.
    // Until then this explicit Windows base palette replaces bridge-local color literals.
    private const string DefaultForegroundColor = "#CCCCCC";
    private const string DefaultBackgroundColor = "#0C0C0C";
    private const string DefaultCursorColor = "#FFFFFF";
    private const string DefaultSelectionBackgroundColor = "#FFFFFF40";
    private const double DefaultBackgroundOpacity = 1.0;
    private const int DefaultBackgroundBlur = 0;
    private const bool DefaultCursorBlink = true;
    private const string DefaultCursorStyle = "block";
    private const double DefaultScrollSensitivity = 3.0;
    private const int DefaultScrollback = 10000;
    private const bool DefaultRightClickPaste = true;
    private const bool DefaultScreenReaderMode = false;

    public static RendererSettingsSnapshot From(
        AppSettings settings,
        TerminalTheme? theme = null,
        bool preservePaneFontSizes = false)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var fontFamily = string.IsNullOrWhiteSpace(settings.FontFamily)
            ? DefaultFontFamily
            : settings.FontFamily.Trim();

        var fontSize = Math.Clamp(settings.FontSize ?? DefaultFontSize, 6.0, 72.0);
        var backgroundOpacity = Math.Clamp(settings.BackgroundOpacity ?? DefaultBackgroundOpacity, 0.0, 1.0);
        var backgroundBlur = Math.Clamp(settings.BackgroundBlur ?? DefaultBackgroundBlur, 0, 100);
        var scrollSensitivity = Math.Clamp(settings.MouseScrollMultiplier ?? DefaultScrollSensitivity, 0.1, 10.0);
        var rightClickPaste = settings.RightClickPaste ?? DefaultRightClickPaste;

        return new RendererSettingsSnapshot(
            fontFamily,
            fontSize,
            theme?.ForegroundColor ?? DefaultForegroundColor,
            theme?.BackgroundColor ?? DefaultBackgroundColor,
            theme?.CursorColor ?? DefaultCursorColor,
            theme?.SelectionBackgroundColor ?? DefaultSelectionBackgroundColor,
            backgroundOpacity,
            backgroundBlur,
            DefaultCursorBlink,
            DefaultCursorStyle,
            scrollSensitivity,
            DefaultScrollback,
            rightClickPaste,
            DefaultScreenReaderMode)
        {
            AnsiColors = theme?.AnsiColors,
            CursorTextColor = theme?.CursorTextColor,
            SelectionForegroundColor = theme?.SelectionForegroundColor,
            PreservePaneFontSizes = preservePaneFontSizes,
            CopyOnSelect = settings.EffectiveCopyOnSelect() switch
            {
                CopyOnSelectMode.Clipboard => "clipboard",
                CopyOnSelectMode.Off => "off",
                _ => "selection",
            },
        };
    }
}
