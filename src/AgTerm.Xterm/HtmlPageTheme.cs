using System.Globalization;
using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>The terminal theme as a page's default style (upstream HtmlOverlayRegistry.theme, c63e5158).</summary>
public static class HtmlPageTheme
{
    /// <summary>The terminal's colors, with the overlay's own background first; whether the page is dark
    /// follows the background it ends up with.</summary>
    public static HtmlOverlayTheme From(RendererSettingsSnapshot renderer, string? backgroundColor)
    {
        var background = Hex(backgroundColor) ?? Hex(renderer.BackgroundColor) ?? "#282c34";
        var foreground = Hex(renderer.ForegroundColor) ?? "#ffffff";
        var dark = ThemeBrightness.IsDark(Channel(background, 1), Channel(background, 3), Channel(background, 5));
        return new HtmlOverlayTheme(background, foreground, dark, renderer.AnsiColors?.Select(color => Hex(color) ?? "").ToArray());
    }

    // #rrggbb in lower case, or null for anything else
    private static string? Hex(string? color)
    {
        if (color is null || !WatermarkConfig.IsValidColorHex(color)) return null;
        return "#" + color.TrimStart('#').ToLowerInvariant();
    }

    private static double Channel(string hex, int start) =>
        int.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
}
