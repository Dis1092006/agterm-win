namespace AgTerm.Core.Model;

/// <summary>The theme a pane shows, decided by the workspace whose session owns the pane.
/// ThemeName null means the global theme.</summary>
public sealed record PaneThemeEntry(Guid WorkspaceId, string? ThemeName);

/// <summary>
/// Which theme every session pane shows, plus the selected session's workspace (the terminal-area
/// background follows it). The quick terminal is never listed: it always shows the global theme.
/// Compared by value, because the window skips a selection snapshot equal to the last one, and a
/// reference comparison would make every sidebar change resubmit the layout.
/// </summary>
public sealed class PaneThemeTable : IEquatable<PaneThemeTable>
{
    public static PaneThemeTable Empty { get; } = new(
        new Dictionary<string, PaneThemeEntry>(StringComparer.Ordinal),
        new HashSet<Guid>(),
        selected: null);

    public PaneThemeTable(
        IReadOnlyDictionary<string, PaneThemeEntry> panes,
        IReadOnlySet<Guid> workspaceIds,
        PaneThemeEntry? selected)
    {
        ArgumentNullException.ThrowIfNull(panes);
        ArgumentNullException.ThrowIfNull(workspaceIds);
        Panes = panes;
        WorkspaceIds = workspaceIds;
        Selected = selected;
    }

    /// <summary>Pane token to the entry of the workspace that owns the pane.</summary>
    public IReadOnlyDictionary<string, PaneThemeEntry> Panes { get; }

    /// <summary>Every workspace in the tree, also one without sessions.</summary>
    public IReadOnlySet<Guid> WorkspaceIds { get; }

    /// <summary>The selected session's workspace; null when nothing is selected.</summary>
    public PaneThemeEntry? Selected { get; }

    public bool Equals(PaneThemeTable? other) =>
        other is not null
        && Equals(Selected, other.Selected)
        && WorkspaceIds.SetEquals(other.WorkspaceIds)
        && Panes.Count == other.Panes.Count
        && Panes.All(pane => other.Panes.TryGetValue(pane.Key, out var entry) && entry == pane.Value);

    public override bool Equals(object? obj) => Equals(obj as PaneThemeTable);

    public override int GetHashCode() => HashCode.Combine(Panes.Count, WorkspaceIds.Count, Selected);
}

/// <summary>The resolution rule for pane themes: a session's panes show its workspace's theme.
/// It knows names only; the window turns them into colors and treats a name the catalog lacks as
/// the global theme. A theme per session (stage 3) is added here.</summary>
public static class PaneThemeAssignment
{
    public static PaneThemeTable For(AppStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var panes = new Dictionary<string, PaneThemeEntry>(StringComparer.Ordinal);
        foreach (var workspace in store.Workspaces)
        {
            var entry = new PaneThemeEntry(workspace.Id, workspace.ThemeName);
            foreach (var token in workspace.Sessions.SelectMany(PaneTokens))
                panes[token] = entry;
        }

        var selected = store.SelectedSessionId is { } id && store.WorkspaceForSession(id) is { } owner
            ? new PaneThemeEntry(owner.Id, owner.ThemeName)
            : null;
        return new PaneThemeTable(panes, store.Workspaces.Select(w => w.Id).ToHashSet(), selected);
    }

    /// <summary>The theme a new pane of this session opens in, so it never shows the global theme
    /// first.</summary>
    public static string? ForSession(AppStore store, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.WorkspaceForSession(sessionId)?.ThemeName;
    }

    private static IEnumerable<string> PaneTokens(Session session) =>
        new[] { session.Surface, session.SplitSurface, session.ScratchSurface, session.OverlaySurface, session.SearchSurface }
            .Select(surface => surface?.PaneToken)
            .OfType<string>()
            .Where(token => token.Length > 0);
}
