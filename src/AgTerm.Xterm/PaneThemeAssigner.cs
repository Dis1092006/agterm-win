using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>What the page must change: pane themes (null = the global theme), and the
/// terminal-area background when it changed (null = the global theme's).</summary>
public sealed record PaneThemeChanges(
    IReadOnlyDictionary<string, TerminalTheme?> Panes,
    bool BackgroundChanged,
    string? Background)
{
    public bool IsEmpty => Panes.Count == 0 && !BackgroundChanged;
}

/// <summary>
/// Keeps the page's per-pane themes in step with the workspace tree. It remembers what the page
/// already shows, so every table sends only the panes whose theme changed. Confined to the
/// window's UI thread. A pane that is absent from a table keeps its theme: it belongs to a
/// workspace pending a close, whose undo brings it back unchanged.
/// </summary>
public sealed class PaneThemeAssigner
{
    private readonly Func<string, TerminalTheme?> _resolve;
    private readonly Dictionary<string, TerminalTheme?> _sent = new(StringComparer.Ordinal);
    private PaneThemeTable _table = PaneThemeTable.Empty;
    private bool _globalBackground = true;
    private string? _sentBackground;

    public PaneThemeAssigner(Func<string, TerminalTheme?> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        _resolve = resolve;
    }

    /// <summary>A name the catalog lacks resolves to null, the global theme.</summary>
    public TerminalTheme? Resolve(string? themeName) => themeName is null ? null : _resolve(themeName);

    public PaneThemeChanges Update(PaneThemeTable table, bool globalBackground)
    {
        ArgumentNullException.ThrowIfNull(table);
        _table = table;
        _globalBackground = globalBackground;
        return Changes();
    }

    /// <summary>A pane opened in the theme its request carried. A table that arrived meanwhile may
    /// already want another one; the changes say so.</summary>
    public PaneThemeChanges Opened(string paneToken, string? themeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(paneToken);
        _sent[paneToken] = Resolve(themeName);
        return Changes();
    }

    public void Closed(string paneToken) => _sent.Remove(paneToken);

    /// <summary>The dashboard and an empty layout belong to no single session, and a zoomed quick
    /// terminal shows the global theme, so the area around them takes the global background.</summary>
    public static bool UsesGlobalBackground(LiveSelectionSnapshot selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection.DashboardOpen
            || selection.ToTerminalLayout(0) is TerminalLayout.Empty
            || (!string.IsNullOrWhiteSpace(selection.ZoomPaneId) && selection.ZoomPaneId == selection.QuickPaneId);
    }

    private PaneThemeChanges Changes()
    {
        var panes = new Dictionary<string, TerminalTheme?>(StringComparer.Ordinal);
        foreach (var (token, entry) in _table.Panes)
        {
            var theme = Resolve(ThemeName(entry));
            // a pane the page never heard about already shows the global theme
            var unchanged = _sent.TryGetValue(token, out var sent) ? Equals(sent, theme) : theme is null;
            if (unchanged)
                continue;
            panes[token] = theme;
            _sent[token] = theme;
        }

        var background = _globalBackground || _table.Selected is not { } selected
            ? null
            : Resolve(ThemeName(selected))?.BackgroundColor;
        var backgroundChanged = background != _sentBackground;
        _sentBackground = background;
        return new PaneThemeChanges(panes, backgroundChanged, background);
    }

    private static string? ThemeName(PaneThemeEntry entry) => entry.ThemeName;
}
