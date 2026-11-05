using AgTerm.Core.Model;

namespace AgTerm.Core.Protocol;

/// <summary>What session.tint or workspace.tint asks of a row: set a color, with or without a
/// placement, or clear. A null Color is clear.</summary>
public sealed record ControlTintUpdate(TintColor? Color, TintParts? Parts)
{
    public static ControlTintUpdate Clear { get; } = new(null, null);

    public static ControlTintUpdate Set(TintColor color, TintParts? parts) => new(color, parts);

    /// <summary>The tint the row gets over <paramref name="current"/>: a set without a placement
    /// recolors and keeps the row's parts, one with a placement replaces them, clear removes the
    /// tint.</summary>
    public SidebarTint? Resolved(SidebarTint? current) => Color switch
    {
        null => null,
        { } color when Parts is { } parts => new SidebarTint(color, parts),
        { } color => SidebarTint.Recolor(current, color),
    };
}
