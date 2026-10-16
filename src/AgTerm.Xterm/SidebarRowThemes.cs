using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>The colors of one sidebar row, copied from a theme: "#rrggbb" each.</summary>
public sealed record SidebarRowColors(string Background, string Foreground);

/// <summary>
/// Turns sidebar rows into the colors of the theme they show. A Windows-only extension
/// (PLATFORM-NOTES "Windows-only extensions"). It also carries the workspace theme palette's
/// preview for one workspace, and a pick the palette just saved: the save answer can reach the
/// window before the sidebar snapshot that has the new theme, so the pick is held until the rows
/// show it, or until they show that something else changed it. Used on the UI thread only.
/// </summary>
public sealed class SidebarRowThemes
{
    private readonly Func<string, TerminalTheme?> _resolve;
    private IReadOnlyList<SidebarRow> _rows = [];
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
        _override = new Override(workspaceId, themeName, StaleName: null, Held: false);

    public void EndPreview() => _override = null;

    public void Saved(Guid workspaceId, string? themeName)
    {
        var (found, shownName) = ThemeShown(_rows, workspaceId);
        _override = found && shownName == themeName
            ? null
            : new Override(workspaceId, themeName, StaleName: shownName, Held: true);
    }

    public void Observe(IReadOnlyList<SidebarRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = rows;
        if (_override is not { Held: true } held)
            return;
        var (found, shownName) = ThemeShown(rows, held.WorkspaceId);
        // a stale snapshot still shows the theme from before the save; anything else ends the hold
        if (!found || shownName != held.StaleName)
            _override = null;
    }

    private static (bool Found, string? ThemeName) ThemeShown(IReadOnlyList<SidebarRow> rows, Guid workspaceId) =>
        rows.FirstOrDefault(r => r.WorkspaceId == workspaceId) is { } row
            ? (true, row.ThemeName)
            : (false, null);

    private sealed record Override(Guid WorkspaceId, string? ThemeName, string? StaleName, bool Held);
}
