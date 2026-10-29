using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>The colors of one sidebar row, copied from a theme: "#rrggbb" each.</summary>
public sealed record SidebarRowColors(string Background, string Foreground);

/// <summary>Where a row shows its theme colors: on the whole row or in a block around the name. At
/// most one slot is set; none for a row with no theme colors.</summary>
public sealed record SidebarRowLook(SidebarRowColors? Row, SidebarRowColors? Label)
{
    public static SidebarRowLook Default { get; } = new(null, null);

    public static SidebarRowLook From(SidebarRowColors? colors, SidebarThemeStyle style) => colors is null
        ? Default
        : style switch
        {
            SidebarThemeStyle.Label => new(null, colors),
            _ => new(colors, null),
        };
}

/// <summary>
/// Turns sidebar rows into the colors of the theme they show. A Windows-only extension
/// (PLATFORM-NOTES "Windows-only extensions"). It also carries the workspace theme palette's
/// preview and saved pick (see <see cref="WorkspacePreviewOverride{T}"/>). Used on the UI thread only.
/// </summary>
public sealed class SidebarRowThemes
{
    private readonly Func<string, TerminalTheme?> _resolve;
    private readonly WorkspacePreviewOverride<string?> _override = new();

    public SidebarRowThemes(Func<string, TerminalTheme?> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        _resolve = resolve;
    }

    public SidebarRowColors? ColorsFor(SidebarRow row, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!enabled)
            return null;
        var name = _override.TryGet(row.WorkspaceId, out var shown) ? shown : row.ThemeName;
        return name is not null && _resolve(name) is { } theme
            ? new SidebarRowColors(theme.BackgroundColor, theme.ForegroundColor)
            : null;
    }

    public void Preview(Guid workspaceId, string? themeName) => _override.Preview(workspaceId, themeName);

    public void EndPreview() => _override.EndPreview();

    public void Saved(Guid workspaceId, string? themeName, long untilGeneration) =>
        _override.Saved(workspaceId, themeName, untilGeneration);

    public void Observe(long generation) => _override.Observe(generation);
}
