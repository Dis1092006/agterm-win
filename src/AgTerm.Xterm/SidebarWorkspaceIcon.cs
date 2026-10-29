using AgTerm.Core.Icons;

namespace AgTerm.Xterm;

/// <summary>
/// The glyph a workspace row draws for its icon name, or null when the row draws the grid: no name,
/// a name the catalog lacks, or a glyph the machine's symbol fonts lack.
/// </summary>
public static class SidebarWorkspaceIcon
{
    public static string? Glyph(string? iconName, Func<int, bool> hasGlyph)
    {
        if (iconName is null) return null;
        if (!SegoeIconCatalog.TryGet(iconName, out var icon)) return null;
        return hasGlyph(icon.CodePoint) ? char.ConvertFromUtf32(icon.CodePoint) : null;
    }
}
