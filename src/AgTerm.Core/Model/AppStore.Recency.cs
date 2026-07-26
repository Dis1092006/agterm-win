namespace AgTerm.Core.Model;

// Port of AppStore+Recency.swift

public sealed partial class AppStore
{
    /// <summary>Up to limit most-recently-used session ids across all workspaces, most-recent first,
    /// skipping stale/closed ids (so it returns fewer than limit when fewer qualify). The
    /// dashboard --mru open reads it to populate the grid from the window's recent sessions.</summary>
    public List<Guid> RecentSessions(int limit) =>
        SessionRecency.Top(limit, Workspaces.SelectMany(w => w.Sessions).Select(s => s.Id).ToHashSet());
}
