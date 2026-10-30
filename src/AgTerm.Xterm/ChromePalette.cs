using System.Globalization;
using AgTerm.Core.Model;

namespace AgTerm.Xterm;

public readonly record struct ChromeColor(byte A, byte R, byte G, byte B)
{
    public static ChromeColor Transparent => default;

    public ChromeColor WithOpacity(double opacity) =>
        this with { A = (byte)Math.Clamp(Math.Round(opacity * 255.0), 0, 255) };

    internal static ChromeColor? TryParse(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#' ||
            !byte.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, null, out var red) ||
            !byte.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, null, out var green) ||
            !byte.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, null, out var blue))
        {
            return null;
        }

        return new ChromeColor(0xFF, red, green, blue);
    }
}

/// <summary>The colors of the window chrome (title bar, sidebar, lines, footer), as upstream
/// WindowAppearance and WindowContentView draw them: the window background is the global terminal
/// background at the background opacity, text and lines are the terminal foreground.</summary>
public sealed record ChromePalette(
    ChromeColor Background,
    ChromeColor Foreground,
    ChromeColor Secondary,
    ChromeColor Hairline,
    ChromeColor GroupDivider,
    ChromeColor PillFill,
    ChromeColor SidebarWash,
    bool UseDarkChrome,
    bool SidebarIsDark)
{
    private static readonly ChromeColor DefaultBackground = new(0xFF, 0x0C, 0x0C, 0x0C);
    private static readonly ChromeColor DefaultForeground = new(0xFF, 0xCC, 0xCC, 0xCC);

    public static ChromePalette From(LiveThemePresentation presentation, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(settings);

        var renderer = presentation.RendererSettings;
        var background = ChromeColor.TryParse(renderer.BackgroundColor) ?? DefaultBackground;
        var foreground = ChromeColor.TryParse(renderer.ForegroundColor) ?? DefaultForeground;
        var shift = AppSettings.SidebarShiftAmount(
            settings.SidebarBackgroundShift ?? AppSettings.DefaultSidebarBackgroundShift);
        var washChannel = shift > 0 ? (byte)0x00 : (byte)0xFF;
        var wash = shift == 0
            ? ChromeColor.Transparent
            : new ChromeColor(0xFF, washChannel, washChannel, washChannel).WithOpacity(Math.Min(1, Math.Abs(shift)));

        return new ChromePalette(
            background.WithOpacity(renderer.BackgroundOpacity),
            foreground,
            foreground.WithOpacity(0.6),
            foreground.WithOpacity(0.1),
            foreground.WithOpacity(0.25),
            foreground.WithOpacity(0.15),
            wash,
            presentation.UseDarkChrome,
            ThemeBrightness.IsDark(background.R / 255.0, background.G / 255.0, background.B / 255.0, shift));
    }

    /// <summary>Windows high contrast: the system window and text colors, solid, with no wash.</summary>
    public static ChromePalette HighContrast(ChromeColor window, ChromeColor windowText)
    {
        var background = window with { A = 0xFF };
        var text = windowText with { A = 0xFF };
        var dark = ThemeBrightness.IsDark(background.R / 255.0, background.G / 255.0, background.B / 255.0);
        return new ChromePalette(
            background, text, text, text, text,
            ChromeColor.Transparent, ChromeColor.Transparent,
            dark, dark);
    }
}
