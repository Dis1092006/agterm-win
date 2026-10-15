namespace AgTerm.Core.Model;

/// <summary>A terminal color theme with the values of a Ghostty theme file. Every color is "#rrggbb".</summary>
public sealed record TerminalTheme(
    string Name,
    string ForegroundColor,
    string BackgroundColor,
    string CursorColor,
    string CursorTextColor,
    string SelectionBackgroundColor,
    string SelectionForegroundColor,
    IReadOnlyList<string> AnsiColors)
{
    public bool IsDark => IsDarkColor(BackgroundColor);

    /// <summary>An unreadable color counts as dark because the window's own default is dark.</summary>
    public static bool IsDarkColor(string color)
    {
        ArgumentNullException.ThrowIfNull(color);
        if (color.Length != 7 || color[0] != '#')
            return true;
        if (!byte.TryParse(color.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var red) ||
            !byte.TryParse(color.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var green) ||
            !byte.TryParse(color.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var blue))
        {
            return true;
        }

        var luminance = 0.299 * red / 255.0 + 0.587 * green / 255.0 + 0.114 * blue / 255.0;
        return luminance < 0.5;
    }
}
