using AgTerm.ControlServer;
using AgTerm.Core.Protocol;

namespace AgTerm.App;

/// <summary>Which theme.set a palette pick sends. While following the system appearance in dark mode
/// the screen shows the dark slot, so a pick writes that slot; writing the light slot would change
/// nothing on screen (upstream AppActions+Palette.swift effectiveTheme, ed831ab).</summary>
public static class ThemePaletteSlot
{
    public static ControlArgs ArgsFor(string? themeName, HeadlessThemeSnapshot saved, bool systemDark)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (saved.Sync && systemDark)
        {
            // the palette omits the default row while following: a pair needs two named themes
            ArgumentNullException.ThrowIfNull(themeName);
            return new ControlArgs { Dark = themeName };
        }

        return new ControlArgs { Name = themeName };
    }
}
