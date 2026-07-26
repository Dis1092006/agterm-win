using System.Diagnostics;
using AgTerm.ControlServer;
using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>One workspace entry for the sidebar's menus (Move to ▸, delete gating).</summary>
public sealed record SidebarWorkspaceInfo(Guid Id, string Name);

/// <summary>
/// Everything the sidebar view renders, snapshotted UNDER the host gate in one read: the row
/// projection plus the chrome state (mode, width, visibility, focus pill, menu data). Immutable —
/// the view diffs consecutive snapshots by row-record equality and rebuilds only on a ShapeKey
/// change.
/// </summary>
public sealed record SidebarSnapshot
{
    public required IReadOnlyList<SidebarRow> Rows { get; init; }
    public required string ShapeKey { get; init; }
    public required IReadOnlyList<SidebarWorkspaceInfo> Workspaces { get; init; }
    public Guid? SelectedSessionId { get; init; }
    public bool CanRemoveWorkspace { get; init; }
    public bool SidebarVisible { get; init; }
    public SidebarMode Mode { get; init; }
    public double SidebarWidth { get; init; }
    public Guid? FocusedWorkspaceId { get; init; }
    public string? FocusedWorkspaceName { get; init; }
    public int FlaggedCount { get; init; }
}

/// <summary>
/// The UI-free control plane of the sidebar: subscribes to the store's coarse invalidation
/// events, snapshots the projection under the host gate, and marshals it to the view through the
/// injected dispatcher; every sidebar gesture funnels into the SAME AppStore mutators the control
/// socket drives (the third caller — toolbar/menu/control in upstream terms). The view stays a
/// thin XAML shell.
///
/// Threading: snapshot composition happens on whatever thread mutated the store (already under
/// the gate); gestures hop through runOffUi (the app passes Task.Run) because the UI thread must
/// NEVER take the host gate (it would deadlock a blocking surface realization).
/// </summary>
public sealed class SidebarController : IDisposable
{
    private readonly HeadlessHost _host;
    private readonly Action<Action> _runOnUi;
    private readonly Action<Action> _runOffUi;
    private readonly string _homeDirectory;
    private bool _started;

    public SidebarController(HeadlessHost host, Action<Action> runOnUi,
                             Action<Action>? runOffUi = null, string? homeDirectory = null)
    {
        _host = host;
        _runOnUi = runOnUi;
        _runOffUi = runOffUi ?? (work => work());
        _homeDirectory = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public event Action<SidebarSnapshot>? SnapshotChanged;

    /// <summary>Subscribe to the store and broadcast the bootstrap snapshot.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _host.Store.SidebarChanged += Broadcast;
        _host.Store.SidebarVisibilityChanged += Broadcast;
        _host.RunLocked(Broadcast);
    }

    public void Dispose()
    {
        if (!_started) return;
        _started = false;
        _host.Store.SidebarChanged -= Broadcast;
        _host.Store.SidebarVisibilityChanged -= Broadcast;
    }

    // MARK: - gestures (each hops off the UI thread and takes the host gate)

    public void Select(Guid sessionId, IReadOnlyList<Guid>? sidebarSelection = null) =>
        Mutate(store => store.SelectSession(sessionId, sidebarSelection));

    public void ToggleWorkspaceCollapse(Guid workspaceId) => Mutate(store =>
    {
        if (store.Workspaces.FirstOrDefault(w => w.Id == workspaceId) is { } workspace)
            store.SetWorkspaceExpanded(workspaceId, !workspace.IsExpanded);
    });

    public void RenameSession(Guid sessionId, string name) =>
        Mutate(store => store.RenameSession(sessionId, name));

    public void RenameWorkspace(Guid workspaceId, string name) =>
        Mutate(store => store.RenameWorkspace(workspaceId, name));

    public void SetFlag(IReadOnlyList<Guid> sessionIds, bool on) =>
        Mutate(store => store.SetFlag(on, sessionIds));

    public void ClearFlags() => Mutate(store => store.ClearFlags());

    public void ClearStatuses(IReadOnlyList<Guid> sessionIds) => Mutate(store =>
    {
        foreach (var id in sessionIds) store.SetAgentIndicator(new AgentIndicator(), id);
    });

    public void DuplicateSession(Guid sessionId) =>
        Mutate(store => store.DuplicateSession(sessionId));

    public void MoveSessionsTo(IReadOnlyList<Guid> sessionIds, Guid workspaceId) =>
        Mutate(store => store.MoveSessions(sessionIds, workspaceId));

    public void CloseSessions(IReadOnlyList<Guid> sessionIds) => Mutate(store =>
    {
        foreach (var id in sessionIds) store.CloseSession(id);
    });

    /// <summary>The workspace-row "New Session" / "+": a fresh shell in the workspace, selected
    /// and realized like the toolbar path.</summary>
    public void NewSession(Guid workspaceId, string? cwd = null) =>
        Mutate(store => store.AddSession(workspaceId, cwd ?? _homeDirectory));

    public void DeleteWorkspace(Guid workspaceId) => Mutate(store =>
    {
        if (store.CanRemoveWorkspace) store.RemoveWorkspace(workspaceId);
    });

    public void NewWorkspace(string name) => Mutate(store => store.AddWorkspace(name));

    public void FocusWorkspaceToggle(Guid workspaceId) => Mutate(store =>
        store.SetFocusedWorkspace(store.FocusedWorkspaceId == workspaceId ? null : workspaceId));

    public void ClearFocus() => Mutate(store => store.SetFocusedWorkspace(null));

    public void SetSidebarMode(SidebarMode mode) => Mutate(store => store.SetSidebarMode(mode));

    public void ToggleFlaggedView() => Mutate(store => store.SetSidebarMode(
        store.SidebarMode == SidebarMode.Tree ? SidebarMode.Flagged : SidebarMode.Tree));

    public void SetSidebarVisible(bool visible) => Mutate(store => store.SetSidebarVisible(visible));

    public void ToggleSidebarVisible() => Mutate(store => store.ToggleSidebarVisible());

    /// <summary>Divider drag end: clamp to the upstream 160…560 range and persist.</summary>
    public void SetSidebarWidth(double width) => Mutate(store =>
    {
        store.SidebarWidth = Math.Clamp(width, AppStore.SidebarWidthMin, AppStore.SidebarWidthMax);
        store.Save();
    });

    /// <summary>Open the session's focused-pane cwd in Explorer (the Reveal menu item). The
    /// existence gate matches the control arm; the process launch happens outside the gate.</summary>
    public void RevealSession(Guid sessionId)
    {
        _runOffUi(() =>
        {
            string? path = null;
            _host.RunLocked(() =>
            {
                if (_host.Store.SessionWithId(sessionId) is { } session &&
                    session.FocusedCwd is { Length: > 0 } cwd && Directory.Exists(cwd))
                    path = cwd;
            });
            if (path is not null)
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false });
        });
    }

    /// <summary>Apply a session drag-drop: raw (pre-removal) child index in, SidebarDrop math,
    /// then the store's remove-block/insert-block move. draggedIds must be in visual order.</summary>
    public void DropSessions(IReadOnlyList<Guid> draggedIds,
                             SidebarDrop.SessionDropTarget target, int rawChildIndex)
    {
        Mutate(store =>
        {
            var sources = new List<SidebarDrop.SessionSource>();
            foreach (var id in draggedIds)
            {
                if (store.SessionLocation(id) is not { } location) return;
                sources.Add(new SidebarDrop.SessionSource(location.Workspace, location.Index));
            }
            if (SidebarDrop.ResolveSessions(sources, target, rawChildIndex) is not { } move) return;
            store.MoveSessions(draggedIds, move.Workspace, move.Destination);
        });
    }

    /// <summary>Apply a workspace drag-drop (raw pre-removal index in, post-removal move out).</summary>
    public void DropWorkspace(Guid workspaceId, int rawIndex)
    {
        Mutate(store =>
        {
            var current = store.Workspaces.FindIndex(w => w.Id == workspaceId);
            if (current < 0) return;
            if (SidebarDrop.ResolveWorkspace(current, store.Workspaces.Count, rawIndex) is not { } move)
                return;
            store.MoveWorkspace(workspaceId, move.Destination);
        });
    }

    // MARK: - hotkey-driven variants (BuiltinAction dispatch targets the ACTIVE session/workspace)

    public void NewSessionInActiveWorkspace(string? cwd = null) => Mutate(store =>
    {
        if (store.CurrentWorkspaceId is { } workspace)
            store.AddSession(workspace, cwd ?? _homeDirectory);
    });

    public void CloseActiveSession() => Mutate(store =>
    {
        if (store.SelectedSessionId is { } id) store.CloseSession(id);
    });

    public void DuplicateActiveSession() => Mutate(store =>
    {
        if (store.SelectedSessionId is { } id) store.DuplicateSession(id);
    });

    public void ToggleActiveFlag() => Mutate(store =>
    {
        if (store.ActiveSession is { } session) store.SetFlag(!session.Flagged, session.Id);
    });

    public void ClearActiveStatus() => Mutate(store =>
    {
        if (store.SelectedSessionId is { } id) store.SetAgentIndicator(new AgentIndicator(), id);
    });

    public void Navigate(SessionNavigation direction) =>
        Mutate(store => store.NavigateSession(direction));

    public void FocusActiveWorkspaceToggle() => Mutate(store =>
    {
        if (store.CurrentWorkspaceId is { } workspace)
            store.SetFocusedWorkspace(store.FocusedWorkspaceId == workspace ? null : workspace);
    });

    public void UndoClose() => Mutate(store => store.UndoPendingClose());

    private void Mutate(Action<AppStore> action) =>
        _runOffUi(() => _host.RunLocked(() => action(_host.Store)));

    /// <summary>Compose the snapshot (callers hold the gate — the store's events fire inside the
    /// mutators) and hand it to the UI thread.</summary>
    private void Broadcast()
    {
        var store = _host.Store;
        var snapshot = new SidebarSnapshot
        {
            Rows = SidebarProjection.Project(store),
            ShapeKey = SidebarProjection.ShapeKey(store),
            Workspaces = store.Workspaces
                .Select(w => new SidebarWorkspaceInfo(w.Id, w.Name)).ToList(),
            SelectedSessionId = store.SelectedSessionId,
            CanRemoveWorkspace = store.CanRemoveWorkspace,
            SidebarVisible = store.SidebarVisible,
            Mode = store.SidebarMode,
            SidebarWidth = store.SidebarWidth,
            FocusedWorkspaceId = store.FocusedWorkspaceId,
            FocusedWorkspaceName = store.FocusedWorkspaceId is { } focused
                ? store.Workspaces.FirstOrDefault(w => w.Id == focused)?.Name
                : null,
            FlaggedCount = store.FlaggedSessions.Count,
        };
        _runOnUi(() => SnapshotChanged?.Invoke(snapshot));
    }
}
