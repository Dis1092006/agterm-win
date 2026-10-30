namespace AgTerm.Core.Model;

/// <summary>upstream agtermCore ThemeBrightness: whether a theme background reads as dark, by Rec. 601
/// luminance against 0.5. Each component is sRGB in 0...1.</summary>
public static class ThemeBrightness
{
    public static bool IsDark(double red, double green, double blue) =>
        0.299 * red + 0.587 * green + 0.114 * blue < 0.5;

    /// <summary>Whether the sidebar reads as dark once the sidebar-tint wash is drawn over the background.
    /// <paramref name="shiftAmount"/> is <see cref="AppSettings.SidebarShiftAmount"/>: negative washes
    /// white, positive washes black, the magnitude is the wash opacity.</summary>
    public static bool IsDark(double red, double green, double blue, double shiftAmount)
    {
        var opacity = Math.Min(1, Math.Abs(shiftAmount));
        var added = shiftAmount < 0 ? opacity : 0;
        double Wash(double c) => c * (1 - opacity) + added;
        return IsDark(Wash(red), Wash(green), Wash(blue));
    }
}
