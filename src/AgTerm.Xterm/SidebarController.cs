using System.Collections.Frozen;
using System.Diagnostics;
using System.Collections.ObjectModel;
using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>One workspace entry for the sidebar's menus (Move to ▸, delete gating).</summary>
public sealed record SidebarWorkspaceInfo(Guid Id, string Name);

public enum SidebarSurfaceStatus
{
    Ready,
    Pending,
    Failed,
}

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
    public IReadOnlyDictionary<Guid, SidebarSurfaceStatus> SurfaceStatuses { get; init; } =
        new ReadOnlyDictionary<Guid, SidebarSurfaceStatus>(new Dictionary<Guid, SidebarSurfaceStatus>());
    public Guid? SelectedSessionId { get; init; }
    public bool CanRemoveWorkspace { get; init; }
    public bool SidebarVisible { get; init; }
    public SidebarMode Mode { get; init; }
    public double SidebarWidth { get; init; }
    public Guid? CurrentWorkspaceId { get; init; }
    public Guid? FocusedWorkspaceId { get; init; }
    public string? FocusedWorkspaceName { get; init; }
    public int FlaggedCount { get; init; }
    public PendingCloseSummary? PendingClose { get; init; }

    /// <summary>The title bar buttons' state, captured with the rows: every change they draw (selection,
    /// scratch, split, a status) also invalidates the sidebar.</summary>
    public TitleBarState TitleBar { get; init; } = TitleBarState.Empty;

    /// <summary>The broadcast generation this snapshot was captured for; later captures have larger
    /// ones.</summary>
    public long Generation { get; init; }
}

/// <summary>
/// The UI-free control plane of the sidebar: subscribes to the store's coarse invalidation
/// events, snapshots the projection under the host gate, and marshals it to the view through the
/// injected dispatcher; every sidebar gesture funnels into the SAME AppStore mutators the control
/// socket drives (the third caller — toolbar/menu/control in upstream terms). The view stays a
/// thin XAML shell.
///
/// Threading: snapshot composition runs through coordinator reads/mutations under the host gate;
/// UI delivery is marshaled through runOnUi, and gesture calls never block waiting for gate work.
/// </summary>
public sealed class SidebarController : IDisposable
{
    private readonly ILiveStoreCoordinator _coordinator;
    private readonly Action<Action> _runOnUi;
    private readonly string _homeDirectory;
    private readonly Action<string>? _log;
    private readonly Func<SidebarConfirmationRequest, Task<bool>> _confirm;
    private readonly Func<AppSettings> _settings;
    private int _started;
    private int _disposed;
    private long _latestRequestedGeneration;

    public SidebarController(
        ILiveStoreCoordinator coordinator,
        Action<Action> runOnUi,
        string? homeDirectory = null,
        Action<string>? log = null,
        Func<SidebarConfirmationRequest, Task<bool>>? confirm = null,
        Func<AppSettings>? settings = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _runOnUi = runOnUi ?? throw new ArgumentNullException(nameof(runOnUi));
        _homeDirectory = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _log = log;
        _confirm = confirm ?? (_ => Task.FromResult(true));
        _settings = settings ?? (() => new AppSettings());
    }

    public event Action<SidebarSnapshot>? SnapshotChanged;

    /// <summary>Subscribe to the store and broadcast the bootstrap snapshot.</summary>
    public void Start()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            return;
        }

        _coordinator.SidebarInvalidated += OnSidebarInvalidated;
        RequestBroadcast();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref _started, 0) != 0)
        {
            _coordinator.SidebarInvalidated -= OnSidebarInvalidated;
        }
    }

    /// <summary>Starts a fresh capture and returns its generation. A store change that finished before
    /// this call shows in that snapshot or in a later one, because only the latest generation reaches
    /// the view.</summary>
    public long RequestSnapshot() => RequestBroadcast();

    // MARK: - gestures (all mutations route through ILiveStoreCoordinator)

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

    public async Task ClearFlagsAsync(CancellationToken cancellationToken = default)
    {
        var flaggedIds = await _coordinator.ReadAsync(store =>
            store.FlaggedSessions.Select(session => session.Id).ToArray(), cancellationToken);
        if (flaggedIds.Length == 0) return;

        var message = flaggedIds.Length == 1
            ? "This unflags 1 session. The session itself is not closed."
            : $"This unflags {flaggedIds.Length} sessions. The sessions themselves are not closed.";
        var request = new SidebarConfirmationRequest(
            SidebarConfirmationKind.ClearFlagged,
            "Clear flagged sessions?",
            message,
            "Clear");
        if (!await _confirm(request)) return;

        await _coordinator.MutateAsync(store =>
        {
            var existingIds = flaggedIds
                .Where(id => store.SessionWithId(id) is not null)
                .ToArray();
            if (existingIds.Length > 0) store.SetFlag(false, existingIds);
        }, cancellationToken);
    }

    public void RequestClearFlags() =>
        FireAndForget(() => ClearFlagsAsync(), "SidebarController clear flagged failed");

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

    public async Task CloseSessionsAsync(
        IReadOnlyList<Guid> sessionIds,
        SidebarCloseOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);
        var sessions = await _coordinator.ReadAsync(store => sessionIds
            .Distinct()
            .Select(store.SessionWithId)
            .OfType<Session>()
            .Select(session => new SessionCloseSnapshot(session.Id, session.DisplayName))
            .ToArray(), cancellationToken);
        if (sessions.Length == 0) return;

        if (options.Confirm)
        {
            var request = CloseConfirmation(sessions, options.GraceUndo);
            if (!await _confirm(request)) return;
        }

        await _coordinator.MutateAsync(store =>
        {
            var existingIds = sessions
                .Where(session => store.SessionWithId(session.Id) is not null)
                .Select(session => session.Id)
                .ToArray();
            if (existingIds.Length != sessions.Length) return;

            if (options.GraceUndo)
                store.SoftCloseSessions(existingIds);
            else
                foreach (var id in existingIds) store.CloseSession(id);
        }, cancellationToken);
    }

    public void RequestCloseSessions(
        IReadOnlyList<Guid> sessionIds,
        SidebarCloseOptions options) =>
        FireAndForget(
            () => CloseSessionsAsync(sessionIds, options),
            "SidebarController close sessions failed");

    /// <summary>The workspace-row "New Session" / "+": a fresh shell in the workspace, selected
    /// and realized like the toolbar path. Without a cwd it opens where newSessionDirectory says.</summary>
    public void NewSession(Guid workspaceId, string? cwd = null)
    {
        var settings = _settings();
        Mutate(store => store.AddSession(workspaceId, cwd ?? NewSessionCwd(store, settings)));
    }

    // upstream AppActions.resolvedNewSessionCwd: the settings are read on the calling (UI) thread,
    // the active session's focused-pane cwd under the gate, before the new session exists
    private string NewSessionCwd(AppStore store, AppSettings settings) =>
        settings.ResolveNewSessionCwd(store.ActiveSession?.FocusedCwd, _homeDirectory);

    public void ImportDirectories(IReadOnlyList<DirectoryImportRequest> requests) => Mutate(store =>
    {
        foreach (var request in requests)
            store.AddSession(request.WorkspaceId, request.DirectoryPath);
    });

    public void DeleteWorkspace(Guid workspaceId) => Mutate(store =>
    {
        if (store.CanRemoveWorkspace) store.RemoveWorkspace(workspaceId);
    });

    public async Task DeleteWorkspaceAsync(
        Guid workspaceId,
        bool graceUndo,
        CancellationToken cancellationToken = default)
    {
        var workspace = await _coordinator.ReadAsync(store =>
        {
            if (!store.CanRemoveWorkspace) return null;

            var target = store.Workspaces.FirstOrDefault(candidate => candidate.Id == workspaceId);
            return target is null
                ? null
                : new WorkspaceDeleteSnapshot(
                    target.Id,
                    target.Name,
                    target.Sessions.Select(session => session.Id).ToFrozenSet());
        }, cancellationToken);
        if (workspace is null) return;

        if (workspace.SessionIds.Count > 0)
        {
            var request = DeleteWorkspaceConfirmation(workspace, graceUndo);
            if (!await _confirm(request)) return;
        }

        await _coordinator.MutateAsync(store =>
        {
            var currentWorkspace = store.Workspaces.FirstOrDefault(candidate => candidate.Id == workspace.Id);
            if (!store.CanRemoveWorkspace ||
                currentWorkspace is null ||
                !workspace.SessionIds.SetEquals(
                    currentWorkspace.Sessions.Select(session => session.Id)))
            {
                return;
            }

            if (graceUndo)
                store.SoftRemoveWorkspace(workspace.Id);
            else
                store.RemoveWorkspace(workspace.Id);
        }, cancellationToken);
    }

    public void RequestDeleteWorkspace(Guid workspaceId, bool graceUndo) =>
        FireAndForget(
            () => DeleteWorkspaceAsync(workspaceId, graceUndo),
            "SidebarController delete workspace failed");

    public void NewWorkspace() => Mutate(store => store.AddWorkspace(store.DefaultWorkspaceName));

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

    public void NewSessionInActiveWorkspace(string? cwd = null)
    {
        var settings = _settings();
        Mutate(store =>
        {
            if (store.CurrentWorkspaceId is { } workspace)
                store.AddSession(workspace, cwd ?? NewSessionCwd(store, settings));
        });
    }

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

    public void UndoClose(Guid pendingCloseId) =>
        Mutate(store => store.UndoPendingClose(pendingCloseId));

    private static SidebarConfirmationRequest CloseConfirmation(
        IReadOnlyList<SessionCloseSnapshot> sessions, bool graceUndo)
    {
        if (sessions.Count == 1)
        {
            return new SidebarConfirmationRequest(
                SidebarConfirmationKind.CloseSession,
                $"Close “{sessions[0].DisplayName}”?",
                graceUndo
                    ? "The session will close after a short undo window."
                    : "The session will close immediately and can be reopened from File > Open Recent.",
                "Close");
        }

        return new SidebarConfirmationRequest(
            SidebarConfirmationKind.CloseSessions,
            $"Close {sessions.Count} Sessions?",
            graceUndo
                ? "The sessions will close after a short undo window."
                : "The sessions will close immediately and can be reopened from File > Open Recent.",
            "Close");
    }

    private static SidebarConfirmationRequest DeleteWorkspaceConfirmation(
        WorkspaceDeleteSnapshot workspace, bool graceUndo)
    {
        var message = workspace.SessionIds.Count == 1
            ? graceUndo
                ? "This closes its session after a short undo window."
                : "This closes its session immediately. You can reopen it from File > Open Recent."
            : graceUndo
                ? $"This closes {workspace.SessionIds.Count} sessions after a short undo window."
                : $"This closes {workspace.SessionIds.Count} sessions immediately. You can reopen it from File > Open Recent.";
        return new SidebarConfirmationRequest(
            SidebarConfirmationKind.DeleteWorkspace,
            $"Delete “{workspace.Name}”?",
            message,
            "Delete");
    }

    private sealed record SessionCloseSnapshot(Guid Id, string DisplayName);

    private sealed record WorkspaceDeleteSnapshot(
        Guid Id,
        string Name,
        FrozenSet<Guid> SessionIds);

    private void Mutate(Action<AppStore> action) =>
        FireAndForget(
            () => _coordinator.MutateAsync(action),
            "SidebarController mutate failed");

    /// <summary>Open the session's focused-pane cwd in Explorer. Store read is under the gate;
    /// process launch runs outside the gate.</summary>
    public void RevealSession(Guid sessionId) =>
        FireAndForget(
            () => RevealSessionAsync(sessionId),
            "SidebarController reveal session failed");

    private async Task RevealSessionAsync(Guid sessionId)
    {
        var path = await _coordinator.ReadAsync(store =>
        {
            if (store.SessionWithId(sessionId) is not { } session)
            {
                return (string?)null;
            }

            var cwd = session.FocusedCwd;
            if (cwd.Length == 0 || !Directory.Exists(cwd))
            {
                return (string?)null;
            }

            return cwd;
        }).ConfigureAwait(false);

        if (path is null)
        {
            return;
        }

        Process.Start(WindowsExplorer.StartInfo($"\"{path}\""));
    }

    /// <summary>Projects AppStore into immutable/materialized sidebar view state.</summary>
    private static SidebarSnapshot Capture(AppStore store, long generation)
    {
        var surfaceStatuses = new Dictionary<Guid, SidebarSurfaceStatus>();
        foreach (var workspace in store.Workspaces)
        {
            foreach (var session in workspace.Sessions)
            {
                surfaceStatuses[session.Id] = session.Surface switch
                {
                    PendingTerminalSurface => SidebarSurfaceStatus.Pending,
                    null => SidebarSurfaceStatus.Failed,
                    _ => SidebarSurfaceStatus.Ready,
                };
            }
        }

        return new SidebarSnapshot
        {
            Generation = generation,
            Rows = SidebarProjection.Project(store),
            ShapeKey = SidebarProjection.ShapeKey(store),
            Workspaces = store.Workspaces
                .Select(w => new SidebarWorkspaceInfo(w.Id, w.Name))
                .ToList(),
            SurfaceStatuses = new ReadOnlyDictionary<Guid, SidebarSurfaceStatus>(surfaceStatuses),
            SelectedSessionId = store.SelectedSessionId,
            CanRemoveWorkspace = store.CanRemoveWorkspace,
            SidebarVisible = store.SidebarVisible,
            Mode = store.SidebarMode,
            SidebarWidth = store.SidebarWidth,
            CurrentWorkspaceId = store.CurrentWorkspaceId,
            FocusedWorkspaceId = store.FocusedWorkspaceId,
            FocusedWorkspaceName = store.FocusedWorkspaceId is { } focused
                ? store.Workspaces.FirstOrDefault(w => w.Id == focused)?.Name
                : null,
            FlaggedCount = store.FlaggedSessions.Count,
            PendingClose = store.PendingCloseSummary,
            TitleBar = TitleBarState.Project(store),
        };
    }

    private void OnSidebarInvalidated() => RequestBroadcast();

    /// <summary>
    /// Request a new snapshot generation. If multiple reads overlap, only the latest generation
    /// may reach the UI; late completions are dropped.
    /// </summary>
    private long RequestBroadcast()
    {
        if (Volatile.Read(ref _started) == 0 || Volatile.Read(ref _disposed) != 0)
        {
            return Volatile.Read(ref _latestRequestedGeneration);
        }

        var generation = Interlocked.Increment(ref _latestRequestedGeneration);

        Task<SidebarSnapshot> readTask;
        try
        {
            readTask = _coordinator.ReadAsync(store => Capture(store, generation));
        }
        catch (Exception ex)
        {
            LogException("SidebarController snapshot read start failed", ex);
            return generation;
        }

        if (readTask.IsCompleted)
        {
            CompleteBroadcast(readTask, generation);
            return generation;
        }

        _ = readTask.ContinueWith(
            static (task, state) =>
            {
                var payload = (CompletionState)state!;
                payload.Controller.CompleteBroadcast(task, payload.Generation);
            },
            new CompletionState(this, generation),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return generation;
    }

    private void CompleteBroadcast(Task<SidebarSnapshot> task, long generation)
    {
        if (task.IsCanceled)
        {
            Log("SidebarController snapshot read canceled");
            return;
        }

        if (task.IsFaulted)
        {
            if (!ShouldSuppress(task.Exception))
                LogException("SidebarController snapshot read failed", task.Exception);
            return;
        }

        SidebarSnapshot snapshot;
        try
        {
            snapshot = task.Result;
        }
        catch (Exception ex)
        {
            LogException("SidebarController snapshot read result failed", ex);
            return;
        }

        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _started) == 0)
        {
            return;
        }

        if (generation != Volatile.Read(ref _latestRequestedGeneration))
        {
            return;
        }

        _runOnUi(() =>
        {
            if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _started) == 0)
            {
                return;
            }

            if (generation != Volatile.Read(ref _latestRequestedGeneration))
            {
                return;
            }

            SnapshotChanged?.Invoke(snapshot);
        });
    }

    private readonly record struct CompletionState(SidebarController Controller, long Generation);

    private void FireAndForget(Func<Task> work, string context)
    {
        Task task;
        try
        {
            task = work();
        }
        catch (Exception ex)
        {
            if (!ShouldSuppress(ex))
            {
                LogException(context, ex);
            }
            return;
        }

        if (task.IsCompleted)
        {
            ObserveFault(task, context);
            return;
        }

        _ = task.ContinueWith(
            static (completed, state) =>
            {
                var payload = (FaultState)state!;
                payload.Controller.ObserveFault(completed, payload.Context);
            },
            new FaultState(this, context),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ObserveFault(Task task, string context)
    {
        if (task.IsCanceled)
        {
            Log($"{context}: canceled");
            return;
        }

        if (!task.IsFaulted)
        {
            return;
        }

        if (!ShouldSuppress(task.Exception))
        {
            LogException(context, task.Exception);
        }
    }

    private bool ShouldSuppress(Exception? exception)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            return false;
        }

        return exception switch
        {
            ObjectDisposedException => true,
            AggregateException aggregate => aggregate.Flatten().InnerExceptions.All(e => e is ObjectDisposedException),
            _ => false,
        };
    }

    private void LogException(string context, Exception? exception)
    {
        if (exception is null)
        {
            Log(context);
            return;
        }

        Log($"{context}: {exception.GetBaseException().Message}");
    }

    private void Log(string message) => _log?.Invoke(message);

    private readonly record struct FaultState(SidebarController Controller, string Context);
}
