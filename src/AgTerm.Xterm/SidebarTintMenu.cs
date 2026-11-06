using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>One choice of the row menu's Tint submenu: a color (null for None), or one part shown or
/// hidden.</summary>
public sealed record SidebarTintChoice
{
    private SidebarTintChoice(TintColor? color, TintParts? part, bool shown)
    {
        Color = color;
        Part = part;
        Shown = shown;
    }

    public TintColor? Color { get; }

    /// <summary>Set for a "Show On" choice, which keeps each row's color.</summary>
    public TintParts? Part { get; }

    /// <summary>For a "Show On" choice, whether the part is added to each row or removed from it.</summary>
    public bool Shown { get; }

    public static SidebarTintChoice Of(TintColor? color) => new(color, null, false);

    public static SidebarTintChoice Show(TintParts part, bool shown) => new(null, part, shown);

    /// <summary>The tint one row has after this choice.</summary>
    public SidebarTint? AppliedTo(SidebarTint? current) =>
        Part is { } part ? SidebarTint.WithPart(current, part, Shown) : SidebarTint.Recolor(current, Color);
}

/// <summary>One entry of the Tint submenu.</summary>
public sealed record SidebarTintMenuEntry(string Title, SidebarTintChoice Choice, bool IsChecked, bool IsEnabled);

/// <summary>
/// The row menu's Tint submenu (upstream WorkspaceSidebar+TintMenu.swift): None and the fixed colors, then
/// where the color shows. A batch of rows checks a color only when every target shares it. The placement
/// entries are one checkbox per part (Windows-only), checked when every target shows the part; they act
/// only when every target is tinted.
/// </summary>
public static class SidebarTintMenu
{
    private static readonly (string Title, TintParts Part)[] PlacementOptions =
        [("Icon", TintParts.Icon), ("Name", TintParts.Name), ("Flag", TintParts.Flag)];

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

    /// <summary>A checkbox for each of <paramref name="available"/>: the parts the row kind has. A click
    /// adds the part to every target unless every target already shows it, and then removes it.</summary>
    public static IReadOnlyList<SidebarTintMenuEntry> Placements(IReadOnlyList<SidebarTint?> current, TintParts available)
    {
        ArgumentNullException.ThrowIfNull(current);
        var allTinted = current.Count > 0 && current.All(t => t is not null);
        return PlacementOptions
            .Where(o => (available & o.Part) != 0)
            .Select(o =>
            {
                var everyShows = allTinted && current.All(t => t!.Shows(o.Part));
                return new SidebarTintMenuEntry(o.Title, SidebarTintChoice.Show(o.Part, !everyShows), everyShows, allTinted);
            })
            .ToList();
    }
}
