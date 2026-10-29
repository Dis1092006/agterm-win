namespace AgTerm.Core.Model;

/// <summary>
/// A user-named group of sessions (e.g. "work", "personal") with a stable UUID identity; its
/// Sessions list holds live Session references.
/// </summary>
public sealed class Workspace
{
    public Workspace(Guid id, string name, List<Session>? sessions = null, bool isExpanded = true)
    {
        Id = id;
        Name = name;
        Sessions = sessions ?? new List<Session>();
        IsExpanded = isExpanded;
    }

    public Workspace(string name, List<Session>? sessions = null, bool isExpanded = true)
        : this(Guid.NewGuid(), name, sessions, isExpanded)
    {
    }

    public Guid Id { get; }

    public string Name { get; set; }

    public List<Session> Sessions { get; }

    /// <summary>Whether the sidebar row is expanded (its session rows shown). Defaults true so a
    /// freshly created workspace opens; persisted per workspace.</summary>
    public bool IsExpanded { get; set; }

    /// <summary>The workspace's own terminal theme, or null for the global theme. Windows-only:
    /// upstream has no theme per workspace. A name the catalog lacks is kept, so a theme that
    /// comes back with a later theme pack works again.</summary>
    public string? ThemeName { get; set; }

    /// <summary>The Segoe icon name shown on the workspace's sidebar row, or null for the default
    /// icon. Windows-only: upstream has no icon per workspace. The name is stored as given, so one
    /// the catalog lacks is kept.</summary>
    public string? IconName { get; set; }

    /// <summary>Total unseen-notification count across this workspace's sessions, for the badge on a
    /// collapsed workspace row.</summary>
    public int UnseenCount => Sessions.Sum(s => s.UnseenCount);

    /// <summary>Captures the persisted value of this workspace. Only a collapsed workspace writes the
    /// flag; an expanded one omits it, so an all-expanded tree serializes identically to a legacy
    /// snapshot.</summary>
    public WorkspaceSnapshot ToSnapshot() => new()
    {
        Id = Id,
        Name = Name,
        Sessions = Sessions.Select(s => s.ToSnapshot()).ToArray(),
        Collapsed = IsExpanded ? null : true,
        Theme = ThemeName,
        Icon = IconName,
    };
}
