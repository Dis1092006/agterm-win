using AgTerm.Core.Icons;

namespace AgTerm.Xterm;

public enum SidebarWorkspaceIconKind
{
    Grid,
    Glyph,
    Image,
}

/// <summary>
/// What a workspace row draws for its icon: the glyph of a Segoe name, an image file, or the grid
/// when it has no icon, a name the catalog lacks, a glyph the machine's symbol fonts lack, or an
/// image that failed to load.
/// </summary>
public static class SidebarWorkspaceIcon
{
    public static string? Glyph(string? iconName, Func<int, bool> hasGlyph)
    {
        if (iconName is null) return null;
        if (!SegoeIconCatalog.TryGet(iconName, out var icon)) return null;
        return hasGlyph(icon.CodePoint) ? char.ConvertFromUtf32(icon.CodePoint) : null;
    }

    public static string? ImageFile(string? iconName) =>
        iconName is not null && WorkspaceIconFile.IsFile(iconName) ? iconName : null;

    public static SidebarWorkspaceIconKind KindOf(string? iconName, Func<int, bool> hasGlyph, bool imageFailed) =>
        ImageFile(iconName) is not null
            ? imageFailed ? SidebarWorkspaceIconKind.Grid : SidebarWorkspaceIconKind.Image
            : Glyph(iconName, hasGlyph) is not null ? SidebarWorkspaceIconKind.Glyph
            : SidebarWorkspaceIconKind.Grid;
}
