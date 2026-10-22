using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>The colors of one sidebar row, copied from a theme: "#rrggbb" each.</summary>
public sealed record SidebarRowColors(string Background, string Foreground);

/// <summary>Where a row shows its theme colors: on the whole row, in a square, or in a block around
/// the name. At most one slot is set; none for a row with no theme colors.</summary>
public sealed record SidebarRowLook(SidebarRowColors? Row, SidebarRowColors? Swatch, SidebarRowColors? Label)
{
    public static SidebarRowLook Default { get; } = new(null, null, null);

    public static SidebarRowLook From(SidebarRowColors? colors, SidebarThemeStyle style) => colors is null
        ? Default
        : style switch
        {
            SidebarThemeStyle.Swatch => new(null, colors, null),
            SidebarThemeStyle.Label => new(null, null, colors),
            _ => new(colors, null, null),
        };
}

/// <summary>
/// Turns sidebar rows into the colors of the theme they show. A Windows-only extension
/// (PLATFORM-NOTES "Windows-only extensions"). It also carries the workspace theme palette's
/// preview for one workspace, and a pick the palette just saved: the save answer can reach the
/// window before the sidebar snapshot that has the new theme, so the pick is held until a snapshot
/// requested after the save arrives. Used on the UI thread only.
/// </summary>
public sealed class SidebarRowThemes
{
    private readonly Func<string, TerminalTheme?> _resolve;
    private long _observedGeneration;
    private Override? _override;

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
        var name = _override is { } shown && shown.WorkspaceId == row.WorkspaceId
            ? shown.ThemeName
            : row.ThemeName;
        return name is not null && _resolve(name) is { } theme
            ? new SidebarRowColors(theme.BackgroundColor, theme.ForegroundColor)
            : null;
    }

    public void Preview(Guid workspaceId, string? themeName) =>
        _override = new Override(workspaceId, themeName, HeldUntil: null);

    public void EndPreview() => _override = null;

    /// <summary>Holds the saved pick until the snapshot of <paramref name="untilGeneration"/> or a
    /// later one, which the caller requested after the save finished. Snapshot names alone cannot
    /// tell a stale snapshot from a later change back to the old theme.</summary>
    public void Saved(Guid workspaceId, string? themeName, long untilGeneration) =>
        _override = _observedGeneration >= untilGeneration
            ? null
            : new Override(workspaceId, themeName, HeldUntil: untilGeneration);

    /// <summary>Called with the generation of every snapshot the view applies.</summary>
    public void Observe(long generation)
    {
        _observedGeneration = Math.Max(_observedGeneration, generation);
        if (_override is { HeldUntil: { } until } && _observedGeneration >= until)
            _override = null;
    }

    private sealed record Override(Guid WorkspaceId, string? ThemeName, long? HeldUntil);
}
