using System.Globalization;
using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// The "#rrggbb" color of each tint color on one background. Taken from the ANSI palette of the theme
/// that paints the background: red, green, yellow, blue and purple are ANSI 1 to 5, gray is ANSI 7, and
/// orange is the even mix of ANSI 1 and 3. Without a usable palette, the macOS system colors that
/// upstream draws, in the light or dark appearance of the background.
/// </summary>
public sealed record SidebarTintInks(
    string Red,
    string Orange,
    string Yellow,
    string Green,
    string Blue,
    string Purple,
    string Gray)
{
    public static SidebarTintInks Light { get; } =
        new("#ff3b30", "#ff9500", "#ffcc00", "#34c759", "#007aff", "#af52de", "#8e8e93");

    public static SidebarTintInks Dark { get; } =
        new("#ff453a", "#ff9f0a", "#ffd60a", "#30d158", "#0a84ff", "#bf5af2", "#8e8e93");

    public static SidebarTintInks Of(IReadOnlyList<string>? ansi, bool dark) =>
        FromAnsi(ansi) ?? (dark ? Dark : Light);

    /// <summary>The inks of an ANSI palette, or null when it lacks one of the entries they use.</summary>
    public static SidebarTintInks? FromAnsi(IReadOnlyList<string>? ansi)
    {
        if (ansi is not { Count: >= 8 }) return null;
        int[] used = [1, 2, 3, 4, 5, 7];
        if (!used.All(i => ChromeColor.TryParse(ansi[i]) is not null)) return null;
        return new SidebarTintInks(ansi[1], Mix(ansi[1], ansi[3]), ansi[3], ansi[2], ansi[4], ansi[5], ansi[7]);
    }

    public string For(TintColor color) => color switch
    {
        TintColor.Red => Red,
        TintColor.Orange => Orange,
        TintColor.Yellow => Yellow,
        TintColor.Green => Green,
        TintColor.Blue => Blue,
        TintColor.Purple => Purple,
        _ => Gray,
    };

    private static string Mix(string first, string second)
    {
        var a = ChromeColor.TryParse(first)!.Value;
        var b = ChromeColor.TryParse(second)!.Value;
        static int Half(byte x, byte y) => (x + y + 1) / 2;
        return string.Create(CultureInfo.InvariantCulture, $"#{Half(a.R, b.R):x2}{Half(a.G, b.G):x2}{Half(a.B, b.B):x2}");
    }
}

/// <summary>The tint colors of one row's icon and name; null where the row shows no tint.</summary>
public sealed record SidebarRowTint(string? Icon, string? Name)
{
    public static SidebarRowTint None { get; } = new(null, null);

    /// <summary>Each part takes the inks of the background under it: a row-style theme paints the whole
    /// row, a label-style theme only the block around the name, and the sidebar the rest.</summary>
    public static SidebarRowTint For(SidebarTint? tint, SidebarRowLook look, SidebarTintInks sidebar)
    {
        ArgumentNullException.ThrowIfNull(look);
        ArgumentNullException.ThrowIfNull(sidebar);
        if (tint is null) return None;
        var iconInks = look.Row?.Inks ?? sidebar;
        var nameInks = (look.Row ?? look.Label)?.Inks ?? sidebar;
        return new SidebarRowTint(
            tint.Shows(TintParts.Icon) ? iconInks.For(tint.Color) : null,
            tint.Shows(TintParts.Name) ? nameInks.For(tint.Color) : null);
    }
}
