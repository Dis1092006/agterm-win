using System.Text;

namespace AgTerm.Core.Model;

public enum SidebarRowKind
{
    Workspace,
    Session,
}

/// <summary>
/// One sidebar row's render content — a value record so the view reconciles by equality (the port
/// of upstream's RowContent diff: a changed row reloads, an identical one is left alone, and only
/// a ShapeKey change rebuilds the whole list).
/// </summary>
public sealed record SidebarRow(SidebarRowKind Kind, Guid Id, string Label)
{
    // workspace rows
    public bool Collapsed { get; init; }
    public bool Focused { get; init; }

    // session rows
    public bool Selected { get; init; }
    public bool Flagged { get; init; }
    public bool HasSplit { get; init; }
    public SplitAxis SplitAxis { get; init; }
    public AgentIndicator Indicator { get; init; }

    // both (session count / workspace roll-up)
    public int Unseen { get; init; }

    // both: a session row carries the workspace it lives in, and the theme it shows
    public Guid WorkspaceId { get; init; }
    public string? ThemeName { get; init; }

    // both: the row's own tint; a workspace tint never reaches its session rows
    public SidebarTint? Tint { get; init; }

    // workspace rows
    public string? IconName { get; init; }

    // session rows: the caller-set info line, drawn under the name when the display setting allows it
    public SessionInfo? Info { get; init; }

    // session rows (Windows-only): the agent CLI running in the focused pane, and whether the label
    // comes from a manual rename
    public SessionAgent? Agent { get; init; }
    public bool HasCustomName { get; init; }
}

/// <summary>
/// The host-free projection of an AppStore into ordered sidebar rows. Tree mode: the focus-filtered
/// VisibleWorkspaces, each workspace open on screen followed by its session rows (a collapsed one keeps
/// only its roll-up row; a reveal opens one without saving it, see AppStore.SyncSidebarReveal).
/// Flagged mode follows the flagged layout: flat is the flagged list labeled
/// "name : workspace", plain is the same list labeled by name, and tree nests the flagged sessions
/// under rows for the workspaces that have one, ignoring the focus filter (upstream WorkspaceSidebar's
/// flagged projection, 902cd853 + f8c3eba3). Pure reads — call under the host gate and hand the rows
/// to the UI thread.
/// </summary>
public static class SidebarProjection
{
    public static IReadOnlyList<SidebarRow> Project(AppStore store)
    {
        var rows = new List<SidebarRow>();
        if (store.SidebarMode == SidebarMode.Flagged)
        {
            if (store.FlaggedLayout == FlaggedViewLayout.Tree)
            {
                foreach (var workspace in store.Workspaces)
                {
                    var flagged = workspace.Sessions.Where(s => s.Flagged).ToList();
                    if (flagged.Count == 0) continue;
                    rows.Add(WorkspaceRow(store, workspace, unseen: flagged.Sum(s => s.UnseenCount)));
                    if (!store.IsWorkspaceOpenInSidebar(workspace)) continue;
                    foreach (var session in flagged)
                        rows.Add(SessionRow(store, workspace, session, session.DisplayName));
                }
                return rows;
            }
            foreach (var session in store.OrderedFlaggedSessions)
            {
                var workspace = store.WorkspaceForSession(session.Id);
                var label = store.FlaggedLayout == FlaggedViewLayout.Plain
                    ? session.DisplayName
                    : $"{session.DisplayName} : {workspace?.Name ?? ""}";
                rows.Add(SessionRow(store, workspace, session, label));
            }
            return rows;
        }

        foreach (var workspace in store.VisibleWorkspaces)
        {
            rows.Add(WorkspaceRow(store, workspace, workspace.UnseenCount));
            if (!store.IsWorkspaceOpenInSidebar(workspace)) continue;
            foreach (var session in workspace.Sessions)
                rows.Add(SessionRow(store, workspace, session, session.DisplayName));
        }
        return rows;
    }

    /// <summary>The list's identity/order fingerprint: ordered row ids + mode (and, in flagged mode, the
    /// layout) + the collapse set + the focus filter. A changed key means rows appeared/vanished/moved —
    /// rebuild the list; an unchanged key lets the view diff row contents in place (a rename never
    /// rebuilds).</summary>
    public static string ShapeKey(AppStore store)
    {
        var key = new StringBuilder(store.SidebarMode == SidebarMode.Flagged
            ? $"flagged-{store.FlaggedLayout.RawValue()}:"
            : "tree:");
        foreach (var row in Project(store))
        {
            key.Append(row.Id.ToString("N"));
            if (row is { Kind: SidebarRowKind.Workspace, Collapsed: true }) key.Append('-');
            key.Append('|');
        }
        return key.ToString();
    }

    private static SidebarRow WorkspaceRow(AppStore store, Workspace workspace, int unseen) =>
        new(SidebarRowKind.Workspace, workspace.Id, workspace.Name)
        {
            Collapsed = !store.IsWorkspaceOpenInSidebar(workspace),
            Focused = store.FocusedWorkspaceIds.Contains(workspace.Id),
            Unseen = unseen,
            WorkspaceId = workspace.Id,
            ThemeName = workspace.ThemeName,
            IconName = workspace.IconName,
            Tint = workspace.Tint,
        };

    private static SidebarRow SessionRow(AppStore store, Workspace? workspace, Session session, string label) =>
        new(SidebarRowKind.Session, session.Id, label)
        {
            WorkspaceId = workspace?.Id ?? Guid.Empty,
            ThemeName = SessionThemeName(workspace, session),
            Selected = store.SelectedSessionId == session.Id,
            Flagged = session.Flagged,
            HasSplit = session.HasSplit,
            SplitAxis = session.SplitAxis,
            Indicator = session.AgentIndicator,
            Unseen = session.UnseenCount,
            Tint = session.Tint,
            Info = session.Info,
            Agent = session.FocusedAgent,
            HasCustomName = Session.TrimmedOrNull(session.CustomName) is not null,
        };

    // stage 3 (a theme per session) changes only this: the session's own theme first
    private static string? SessionThemeName(Workspace? workspace, Session session) => workspace?.ThemeName;
}
