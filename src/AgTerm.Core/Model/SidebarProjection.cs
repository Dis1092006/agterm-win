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
    public bool ActiveWorkspace { get; init; }

    // session rows
    public bool Selected { get; init; }
    public bool Flagged { get; init; }
    public bool HasSplit { get; init; }
    public AgentIndicator Indicator { get; init; }

    // both (session count / workspace roll-up)
    public int Unseen { get; init; }
}

/// <summary>
/// The host-free projection of an AppStore into ordered sidebar rows. Tree mode: the focus-filtered
/// VisibleWorkspaces, each expanded workspace followed by its session rows (a collapsed one keeps
/// only its roll-up row). Flagged mode: the flat flagged list labeled "name : workspace", no
/// workspace rows (upstream WorkspaceSidebar's flagged projection). Pure reads — call under the
/// host gate and hand the rows to the UI thread.
/// </summary>
public static class SidebarProjection
{
    public static IReadOnlyList<SidebarRow> Project(AppStore store)
    {
        var rows = new List<SidebarRow>();
        if (store.SidebarMode == SidebarMode.Flagged)
        {
            foreach (var session in store.FlaggedSessions)
            {
                var workspace = store.WorkspaceForSession(session.Id);
                rows.Add(SessionRow(store, session,
                    label: $"{session.DisplayName} : {workspace?.Name ?? ""}"));
            }
            return rows;
        }

        var current = store.CurrentWorkspaceId;
        foreach (var workspace in store.VisibleWorkspaces)
        {
            rows.Add(new SidebarRow(SidebarRowKind.Workspace, workspace.Id, workspace.Name)
            {
                Collapsed = !workspace.IsExpanded,
                Focused = store.FocusedWorkspaceId == workspace.Id,
                ActiveWorkspace = current == workspace.Id,
                Unseen = workspace.UnseenCount,
            });
            if (!workspace.IsExpanded) continue;
            foreach (var session in workspace.Sessions)
                rows.Add(SessionRow(store, session, session.DisplayName));
        }
        return rows;
    }

    /// <summary>The list's identity/order fingerprint: ordered row ids + mode + the collapse set +
    /// the focus filter. A changed key means rows appeared/vanished/moved — rebuild the list; an
    /// unchanged key lets the view diff row contents in place (a rename never rebuilds).</summary>
    public static string ShapeKey(AppStore store)
    {
        var key = new StringBuilder(store.SidebarMode == SidebarMode.Flagged ? "flagged:" : "tree:");
        foreach (var row in Project(store))
        {
            key.Append(row.Id.ToString("N"));
            if (row is { Kind: SidebarRowKind.Workspace, Collapsed: true }) key.Append('-');
            key.Append('|');
        }
        return key.ToString();
    }

    private static SidebarRow SessionRow(AppStore store, Session session, string label) =>
        new(SidebarRowKind.Session, session.Id, label)
        {
            Selected = store.SelectedSessionId == session.Id,
            Flagged = session.Flagged,
            HasSplit = session.HasSplit,
            Indicator = session.AgentIndicator,
            Unseen = session.UnseenCount,
        };
}
