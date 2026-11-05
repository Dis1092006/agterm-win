using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>One choice of the row menu's Tint submenu: a color (null for None), or the parts that show it.</summary>
public sealed record SidebarTintChoice
{
    private SidebarTintChoice(TintColor? color, TintParts? parts)
    {
        Color = color;
        Parts = parts;
    }

    public TintColor? Color { get; }

    /// <summary>Set for a "Show On" choice, which keeps each row's color.</summary>
    public TintParts? Parts { get; }

    public static SidebarTintChoice Of(TintColor? color) => new(color, null);

    public static SidebarTintChoice On(TintParts parts) => new(null, parts);

    /// <summary>The tint one row has after this choice.</summary>
    public SidebarTint? AppliedTo(SidebarTint? current) =>
        Parts is { } parts ? SidebarTint.Place(current, parts) : SidebarTint.Recolor(current, Color);
}

/// <summary>One entry of the Tint submenu.</summary>
public sealed record SidebarTintMenuEntry(string Title, SidebarTintChoice Choice, bool IsChecked, bool IsEnabled);

/// <summary>
/// The row menu's Tint submenu (upstream WorkspaceSidebar+TintMenu.swift): None and the fixed colors, then
/// where the color shows. A batch of rows checks an entry only when every target shares it, and the
/// placement entries act only when every target is tinted.
/// </summary>
public static class SidebarTintMenu
{
    private static readonly (string Title, TintParts Parts)[] PlacementOptions =
        [("Icon", TintParts.Icon), ("Name", TintParts.Name), ("Icon and Name", TintParts.Icon | TintParts.Name)];

    public static IReadOnlyList<SidebarTintMenuEntry> Colors(IReadOnlyList<SidebarTint?> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var colors = current.Select(t => t?.Color).Distinct().ToList();
        var entries = new List<SidebarTintMenuEntry>
        {
            new("None", SidebarTintChoice.Of(null), colors is [null], true),
        };
        foreach (var color in Enum.GetValues<TintColor>())
        {
            var wire = TintColors.Wire(color);
            entries.Add(new(char.ToUpperInvariant(wire[0]) + wire[1..], SidebarTintChoice.Of(color), colors.SequenceEqual([color]), true));
        }
        return entries;
    }

    public static IReadOnlyList<SidebarTintMenuEntry> Placements(IReadOnlyList<SidebarTint?> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var allTinted = current.All(t => t is not null);
        var placements = current.Select(t => t?.Parts).Distinct().ToList();
        return PlacementOptions
            .Select(o => new SidebarTintMenuEntry(o.Title, SidebarTintChoice.On(o.Parts), placements.SequenceEqual([o.Parts]), allTinted))
            .ToList();
    }
}
