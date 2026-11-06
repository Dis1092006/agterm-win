namespace AgTerm.Core.Model;

/// <summary>
/// The sidebar's view-only reveal (upstream WorkspaceSidebar.syncSelection, 902cd853). The sidebar
/// opens the selected session's collapsed workspace on screen, and Workspace.IsExpanded stays as it
/// was saved. Live window state, never saved.
/// </summary>
public partial class AppStore
{
    /// <summary>Workspaces the reveal opened on screen while their saved state is collapsed. A
    /// collapse removes the id; a hidden sidebar forgets them all, as upstream drops its outline.</summary>
    private readonly HashSet<Guid> _sidebarRevealedWorkspaceIds = new();

    /// <summary>The selected session the last reveal handled. The reveal runs again only when the
    /// selection changes, so a title or status update leaves a deliberate fold alone.</summary>
    private Guid? _lastRevealedSelection;

    /// <summary>The view the last reveal ran in. A mode or flagged layout switch moves the selected
    /// row while its id stays the same, so it forgets the last reveal.</summary>
    private (SidebarMode Mode, FlaggedViewLayout Layout)? _lastRevealView;

    /// <summary>Moves on each reveal: the sidebar scrolls the selected row into view when it does.</summary>
    public long SidebarRevealGeneration { get; private set; }

    /// <summary>Whether the sidebar shows the workspace open: saved expanded, or opened by a reveal.
    /// The tree read-back still reports the saved state.</summary>
    public bool IsWorkspaceOpenInSidebar(Workspace workspace) =>
        workspace.IsExpanded || _sidebarRevealedWorkspaceIds.Contains(workspace.Id);

    /// <summary>The render-time step the sidebar runs before it projects the rows. On a new selection,
    /// or after a view switch, it opens the selected session's collapsed workspace on screen and moves
    /// SidebarRevealGeneration. A hidden sidebar reveals nothing and forgets what it revealed.</summary>
    public void SyncSidebarReveal()
    {
        if (!SidebarVisible)
        {
            ForgetSidebarReveal();
            return;
        }

        var view = (SidebarMode, FlaggedLayout);
        if (_lastRevealView != view)
        {
            _lastRevealView = view;
            _lastRevealedSelection = null;
        }

        if (SelectedSessionId is not { } id || SessionWithId(id) is not { } session
            || WorkspaceForSession(id) is not { } owner || !SidebarListsSession(owner, session))
        {
            _lastRevealedSelection = null;
            return;
        }

        if (_lastRevealedSelection == id) return;
        _lastRevealedSelection = id;
        SidebarRevealGeneration++;
        if (RendersWorkspaceRows && !owner.IsExpanded) _sidebarRevealedWorkspaceIds.Add(owner.Id);
    }

    /// <summary>Drops a workspace from the reveal when it is collapsed. True when it was open only
    /// by the reveal.</summary>
    private bool ForgetRevealedWorkspace(Guid workspaceId) => _sidebarRevealedWorkspaceIds.Remove(workspaceId);

    private bool KeepRevealedWorkspaces(IReadOnlySet<Guid> expandedIds) =>
        _sidebarRevealedWorkspaceIds.RemoveWhere(id => !expandedIds.Contains(id)) > 0;

    /// <summary>A fresh sidebar: nothing revealed, and the next sync reveals the selection.</summary>
    private void ForgetSidebarReveal()
    {
        _sidebarRevealedWorkspaceIds.Clear();
        _lastRevealedSelection = null;
        _lastRevealView = null;
    }

    /// <summary>Whether the current view has a row for the session, under a workspace row that may be
    /// collapsed: the tree lists the filter's workspaces, the flagged view only flagged sessions.</summary>
    private bool SidebarListsSession(Workspace owner, Session session) =>
        SidebarMode == SidebarMode.Flagged
            ? session.Flagged
            : VisibleWorkspaces.Any(w => w.Id == owner.Id);
}
