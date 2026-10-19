using System.Collections.Concurrent;
using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class SidebarWorkflowTests : IDisposable
{
    private readonly string _stateDir = Directory.CreateTempSubdirectory("agterm-workflow-").FullName;
    private readonly HeadlessHost _host;
    private readonly LiveStoreCoordinator _coordinator;

    public SidebarWorkflowTests()
    {
        _host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            PipeName = "agterm-workflow-" + Guid.NewGuid().ToString("N"),
            HomeDirectory = _stateDir,
        });
        _coordinator = new LiveStoreCoordinator(_host, isUiThread: () => false);
    }

    public void Dispose()
    {
        _coordinator.Dispose();
        _host.Dispose();
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Confirmed_close_snapshots_the_name_releases_the_gate_and_soft_closes_by_default()
    {
        var sessionId = await _coordinator.ReadAsync(store => store.SelectedSessionId!.Value);
        await _coordinator.MutateAsync(store => store.RenameSession(sessionId, "build"));
        var promptStarted = new TaskCompletionSource<SidebarConfirmationRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrompt = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = new SidebarController(
            _coordinator,
            runOnUi: work => work(),
            confirm: request =>
            {
                promptStarted.SetResult(request);
                return releasePrompt.Task;
            });

        var close = controller.CloseSessionsAsync(
            new[] { sessionId },
            new SidebarCloseOptions(Confirm: true, GraceUndo: true));
        var request = await promptStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(SidebarConfirmationKind.CloseSession, request.Kind);
        Assert.Equal("Close “build”?", request.Title);
        Assert.Equal("The session will close after a short undo window.", request.Message);
        Assert.Equal("Close", request.PrimaryButtonText);
        await _coordinator.MutateAsync(store => store.RenameSession(sessionId, "renamed while open"))
            .WaitAsync(TimeSpan.FromSeconds(2));
        releasePrompt.SetResult(true);
        await close.WaitAsync(TimeSpan.FromSeconds(2));

        var state = await _coordinator.ReadAsync(store => new
        {
            Exists = store.SessionWithId(sessionId) is not null,
            store.PendingCloseSummary,
        });
        Assert.False(state.Exists);
        Assert.Equal(PendingCloseKind.Session, state.PendingCloseSummary?.Kind);
        Assert.Equal("renamed while open", state.PendingCloseSummary?.Title);
    }

    [Fact]
    public async Task Cancelled_close_leaves_the_session_open()
    {
        var sessionId = await _coordinator.ReadAsync(store => store.SelectedSessionId!.Value);
        using var controller = CreateController(_ => Task.FromResult(false));

        await controller.CloseSessionsAsync(
            new[] { sessionId },
            new SidebarCloseOptions(Confirm: true, GraceUndo: true));

        Assert.True(await _coordinator.ReadAsync(store => store.SessionWithId(sessionId) is not null));
    }

    [Fact]
    public async Task Control_pipe_remains_usable_while_close_confirmation_is_open()
    {
        _host.Start();
        var sessionId = await _coordinator.ReadAsync(store => store.SelectedSessionId!.Value);
        var promptStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrompt = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = CreateController(_ =>
        {
            promptStarted.SetResult();
            return releasePrompt.Task;
        });

        var close = controller.CloseSessionsAsync(
            new[] { sessionId },
            new SidebarCloseOptions(Confirm: true, GraceUndo: true));
        await promptStarted.Task;
        var response = await Task.Run(() => new ControlPipeClient(_host.PipeName).Send(
                new ControlRequest { Cmd = Command.Tree }))
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(response.Ok, response.Error);
        releasePrompt.SetResult(false);
        await close;
    }

    [Fact]
    public async Task Confirmed_batch_close_uses_plural_text_and_hard_closes_when_grace_is_off()
    {
        Guid first = default;
        Guid second = default;
        await _coordinator.MutateAsync(store =>
        {
            first = store.SelectedSessionId!.Value;
            second = store.AddSession(store.CurrentWorkspaceId!.Value, _stateDir)!.Id;
        });
        SidebarConfirmationRequest? prompt = null;
        using var controller = CreateController(request =>
        {
            prompt = request;
            return Task.FromResult(true);
        });

        await controller.CloseSessionsAsync(
            new[] { second, first },
            new SidebarCloseOptions(Confirm: true, GraceUndo: false));

        Assert.Equal(SidebarConfirmationKind.CloseSessions, prompt?.Kind);
        Assert.Equal("Close 2 Sessions?", prompt?.Title);
        Assert.Equal(
            "The sessions will close immediately and can be reopened from File > Open Recent.",
            prompt?.Message);
        var state = await _coordinator.ReadAsync(store => new
        {
            FirstExists = store.SessionWithId(first) is not null,
            SecondExists = store.SessionWithId(second) is not null,
            store.PendingCloseSummary,
        });
        Assert.False(state.FirstExists);
        Assert.False(state.SecondExists);
        Assert.Null(state.PendingCloseSummary);
    }

    [Fact]
    public async Task Confirmed_close_revalidates_a_session_removed_while_the_dialog_is_open()
    {
        var sessionId = await _coordinator.ReadAsync(store => store.SelectedSessionId!.Value);
        var promptStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrompt = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = CreateController(_ =>
        {
            promptStarted.SetResult();
            return releasePrompt.Task;
        });

        var close = controller.CloseSessionsAsync(
            new[] { sessionId },
            new SidebarCloseOptions(Confirm: true, GraceUndo: true));
        await promptStarted.Task;
        await _coordinator.MutateAsync(store => store.CloseSession(sessionId));
        releasePrompt.SetResult(true);
        await close;

        Assert.Null(await _coordinator.ReadAsync(store => store.PendingCloseSummary));
    }

    [Fact]
    public async Task Confirmed_batch_close_aborts_when_any_snapshotted_session_is_stale()
    {
        Guid first = default;
        Guid second = default;
        await _coordinator.MutateAsync(store =>
        {
            first = store.SelectedSessionId!.Value;
            second = store.AddSession(store.CurrentWorkspaceId!.Value, _stateDir)!.Id;
        });
        var promptStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrompt = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = CreateController(_ =>
        {
            promptStarted.SetResult();
            return releasePrompt.Task;
        });

        var close = controller.CloseSessionsAsync(
            new[] { first, second },
            new SidebarCloseOptions(Confirm: true, GraceUndo: true));
        await promptStarted.Task;
        await _coordinator.MutateAsync(store => store.CloseSession(first));
        releasePrompt.SetResult(true);
        await close;

        Assert.True(await _coordinator.ReadAsync(store => store.SessionWithId(second) is not null));
        Assert.Null(await _coordinator.ReadAsync(store => store.PendingCloseSummary));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_workspace_skips_confirmation_and_is_removed_only_when_another_exists(
        bool anotherWorkspaceExists)
    {
        Guid workspaceId = default;
        await _coordinator.MutateAsync(store =>
        {
            workspaceId = store.CurrentWorkspaceId!.Value;
            store.CloseSession(store.SelectedSessionId!.Value);
            if (anotherWorkspaceExists) store.AddWorkspace("survivor", clearFocus: false);
        });
        var confirmations = 0;
        using var controller = CreateController(_ =>
        {
            confirmations++;
            return Task.FromResult(true);
        });

        await controller.DeleteWorkspaceAsync(workspaceId, graceUndo: true);

        Assert.Equal(0, confirmations);
        Assert.Equal(
            !anotherWorkspaceExists,
            await _coordinator.ReadAsync(store => store.Workspaces.Any(w => w.Id == workspaceId)));
    }

    [Fact]
    public async Task Confirmed_workspace_delete_snapshots_the_target_releases_the_gate_and_soft_removes_by_default()
    {
        Guid workspaceId = default;
        Guid survivorId = default;
        await _coordinator.MutateAsync(store =>
        {
            workspaceId = store.CurrentWorkspaceId!.Value;
            store.RenameWorkspace(workspaceId, "build");
            survivorId = store.AddWorkspace("survivor", clearFocus: false).Id;
        });
        var promptStarted = new TaskCompletionSource<SidebarConfirmationRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrompt = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = CreateController(request =>
        {
            promptStarted.SetResult(request);
            return releasePrompt.Task;
        });

        var delete = controller.DeleteWorkspaceAsync(workspaceId, graceUndo: true);
        var request = await promptStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await _coordinator.MutateAsync(store =>
            store.RenameWorkspace(workspaceId, "renamed while open"))
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(SidebarConfirmationKind.DeleteWorkspace, request.Kind);
        Assert.Equal("Delete “build”?", request.Title);
        Assert.Equal("This closes its session after a short undo window.", request.Message);
        Assert.Equal("Delete", request.PrimaryButtonText);
        releasePrompt.SetResult(true);
        await delete.WaitAsync(TimeSpan.FromSeconds(2));

        var state = await _coordinator.ReadAsync(store => new
        {
            TargetExists = store.Workspaces.Any(w => w.Id == workspaceId),
            SurvivorExists = store.Workspaces.Any(w => w.Id == survivorId),
            store.PendingCloseSummary,
        });
        Assert.False(state.TargetExists);
        Assert.True(state.SurvivorExists);
        Assert.Equal(PendingCloseKind.Workspace, state.PendingCloseSummary?.Kind);
        Assert.Equal("renamed while open", state.PendingCloseSummary?.Title);
    }

    [Fact]
    public async Task Confirmed_workspace_delete_aborts_when_session_count_changes_while_the_dialog_is_open()
    {
        Guid workspaceId = default;
        Guid originalSessionId = default;
        await _coordinator.MutateAsync(store =>
        {
            workspaceId = store.CurrentWorkspaceId!.Value;
            originalSessionId = store.SelectedSessionId!.Value;
            store.RenameWorkspace(workspaceId, "build");
            store.AddWorkspace("survivor", clearFocus: false);
        });
        var promptStarted = new TaskCompletionSource<SidebarConfirmationRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrompt = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = CreateController(request =>
        {
            promptStarted.SetResult(request);
            return releasePrompt.Task;
        });

        var delete = controller.DeleteWorkspaceAsync(workspaceId, graceUndo: true);
        var request = await promptStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Guid addedSessionId = default;
        await _coordinator.MutateAsync(store =>
        {
            addedSessionId = store.AddSession(workspaceId, _stateDir, select: false)!.Id;
        }).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(SidebarConfirmationKind.DeleteWorkspace, request.Kind);
        releasePrompt.SetResult(true);
        await delete.WaitAsync(TimeSpan.FromSeconds(2));

        var state = await _coordinator.ReadAsync(store =>
        {
            var target = store.Workspaces.SingleOrDefault(workspace => workspace.Id == workspaceId);
            return new
            {
                WorkspaceExists = target is not null,
                SessionIds = target?.Sessions.Select(session => session.Id).ToArray() ?? [],
                store.PendingCloseSummary,
            };
        });
        Assert.True(state.WorkspaceExists);
        Assert.Equal(2, state.SessionIds.Length);
        Assert.Contains(originalSessionId, state.SessionIds);
        Assert.Contains(addedSessionId, state.SessionIds);
        Assert.Null(state.PendingCloseSummary);
    }

    [Fact]
    public async Task Confirmed_workspace_delete_aborts_when_session_membership_changes_without_count_change()
    {
        var workspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var survivorWorkspaceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var originalSessionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var replacementSessionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        await _coordinator.MutateAsync(store =>
        {
            store.Workspaces =
            [
                new Workspace(workspaceId, "build", [new Session(_stateDir, originalSessionId)]),
                new Workspace(survivorWorkspaceId, "survivor"),
            ];
            store.SelectedSessionId = originalSessionId;
        });
        var promptStarted = new TaskCompletionSource<SidebarConfirmationRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrompt = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = CreateController(request =>
        {
            promptStarted.SetResult(request);
            return releasePrompt.Task;
        });

        var delete = controller.DeleteWorkspaceAsync(workspaceId, graceUndo: true);
        var request = await promptStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await _coordinator.MutateAsync(store =>
        {
            store.CloseSession(originalSessionId);
            var target = Assert.Single(store.Workspaces, workspace => workspace.Id == workspaceId);
            target.Sessions.Add(new Session(_stateDir, replacementSessionId));
        }).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(SidebarConfirmationKind.DeleteWorkspace, request.Kind);
        releasePrompt.SetResult(true);
        await delete.WaitAsync(TimeSpan.FromSeconds(2));

        var state = await _coordinator.ReadAsync(store => new
        {
            WorkspaceExists = store.Workspaces.Any(workspace => workspace.Id == workspaceId),
            OriginalSessionExists = store.SessionWithId(originalSessionId) is not null,
            ReplacementSessionExists = store.SessionWithId(replacementSessionId) is not null,
            store.PendingCloseSummary,
        });
        Assert.True(state.WorkspaceExists);
        Assert.False(state.OriginalSessionExists);
        Assert.True(state.ReplacementSessionExists);
        Assert.Null(state.PendingCloseSummary);
    }

    [Fact]
    public async Task Confirmed_workspace_delete_hard_removes_and_mentions_open_recent_when_grace_is_off()
    {
        Guid workspaceId = default;
        await _coordinator.MutateAsync(store =>
        {
            workspaceId = store.CurrentWorkspaceId!.Value;
            store.RenameWorkspace(workspaceId, "build");
            store.AddSession(workspaceId, _stateDir, select: false);
            store.AddWorkspace("survivor", clearFocus: false);
        });
        SidebarConfirmationRequest? prompt = null;
        using var controller = CreateController(request =>
        {
            prompt = request;
            return Task.FromResult(true);
        });

        await controller.DeleteWorkspaceAsync(workspaceId, graceUndo: false);

        Assert.Equal(SidebarConfirmationKind.DeleteWorkspace, prompt?.Kind);
        Assert.Equal("Delete “build”?", prompt?.Title);
        Assert.Equal(
            "This closes 2 sessions immediately. You can reopen it from File > Open Recent.",
            prompt?.Message);
        Assert.Equal("Delete", prompt?.PrimaryButtonText);
        Assert.False(await _coordinator.ReadAsync(store =>
            store.Workspaces.Any(w => w.Id == workspaceId)));
        Assert.Null(await _coordinator.ReadAsync(store => store.PendingCloseSummary));
    }

    [Fact]
    public async Task Cancelled_workspace_delete_leaves_the_workspace_open()
    {
        Guid workspaceId = default;
        await _coordinator.MutateAsync(store =>
        {
            workspaceId = store.CurrentWorkspaceId!.Value;
            store.AddWorkspace("survivor", clearFocus: false);
        });
        using var controller = CreateController(_ => Task.FromResult(false));

        await controller.DeleteWorkspaceAsync(workspaceId, graceUndo: true);

        Assert.True(await _coordinator.ReadAsync(store =>
            store.Workspaces.Any(w => w.Id == workspaceId)));
        Assert.Null(await _coordinator.ReadAsync(store => store.PendingCloseSummary));
    }

    [Fact]
    public async Task Confirmed_workspace_delete_does_not_remove_a_replacement_when_the_target_is_stale()
    {
        Guid workspaceId = default;
        Guid firstSurvivorId = default;
        Guid secondSurvivorId = default;
        await _coordinator.MutateAsync(store =>
        {
            workspaceId = store.CurrentWorkspaceId!.Value;
            firstSurvivorId = store.AddWorkspace("first survivor", clearFocus: false).Id;
            secondSurvivorId = store.AddWorkspace("second survivor", clearFocus: false).Id;
        });
        var promptStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrompt = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = CreateController(_ =>
        {
            promptStarted.SetResult();
            return releasePrompt.Task;
        });

        var delete = controller.DeleteWorkspaceAsync(workspaceId, graceUndo: true);
        await promptStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await _coordinator.MutateAsync(store => store.RemoveWorkspace(workspaceId));
        releasePrompt.SetResult(true);
        await delete.WaitAsync(TimeSpan.FromSeconds(2));

        var remainingIds = await _coordinator.ReadAsync(store =>
            store.Workspaces.Select(workspace => workspace.Id).ToArray());
        Assert.Equal(new[] { firstSurvivorId, secondSurvivorId }, remainingIds);
        Assert.Null(await _coordinator.ReadAsync(store => store.PendingCloseSummary));
    }

    [Fact]
    public async Task Confirmed_workspace_delete_leaves_the_target_when_it_becomes_the_last_workspace()
    {
        Guid workspaceId = default;
        Guid otherWorkspaceId = default;
        await _coordinator.MutateAsync(store =>
        {
            workspaceId = store.CurrentWorkspaceId!.Value;
            otherWorkspaceId = store.AddWorkspace("temporary", clearFocus: false).Id;
        });
        var promptStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrompt = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = CreateController(_ =>
        {
            promptStarted.SetResult();
            return releasePrompt.Task;
        });

        var delete = controller.DeleteWorkspaceAsync(workspaceId, graceUndo: true);
        await promptStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await _coordinator.MutateAsync(store => store.RemoveWorkspace(otherWorkspaceId));
        releasePrompt.SetResult(true);
        await delete.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(await _coordinator.ReadAsync(store =>
            store.Workspaces.Any(w => w.Id == workspaceId)));
        Assert.Null(await _coordinator.ReadAsync(store => store.PendingCloseSummary));
    }

    [Fact]
    public async Task ClearFlagged_confirms_the_snapshotted_count_and_preserves_new_flags()
    {
        Guid first = default;
        Guid second = default;
        await _coordinator.MutateAsync(store =>
        {
            first = store.SelectedSessionId!.Value;
            second = store.AddSession(store.CurrentWorkspaceId!.Value, _stateDir)!.Id;
            store.SetFlag(true, new[] { first });
        });
        var promptStarted = new TaskCompletionSource<SidebarConfirmationRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrompt = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = CreateController(request =>
        {
            promptStarted.SetResult(request);
            return releasePrompt.Task;
        });

        var clear = controller.ClearFlagsAsync();
        var request = await promptStarted.Task;
        await _coordinator.MutateAsync(store => store.SetFlag(true, new[] { second }));
        releasePrompt.SetResult(true);
        await clear;

        Assert.Equal(SidebarConfirmationKind.ClearFlagged, request.Kind);
        Assert.Equal("Clear flagged sessions?", request.Title);
        Assert.Equal("This unflags 1 session. The session itself is not closed.", request.Message);
        Assert.Equal("Clear", request.PrimaryButtonText);
        var flagged = await _coordinator.ReadAsync(store => store.FlaggedSessions.Select(s => s.Id).ToArray());
        Assert.Equal(new[] { second }, flagged);
    }

    [Fact]
    public async Task Pending_close_is_projected_and_targeted_undo_restores_the_shown_session()
    {
        var snapshots = new ConcurrentQueue<SidebarSnapshot>();
        using var controller = CreateController(_ => Task.FromResult(true));
        controller.SnapshotChanged += snapshots.Enqueue;
        controller.Start();
        var sessionId = await _coordinator.ReadAsync(store => store.SelectedSessionId!.Value);

        await controller.CloseSessionsAsync(
            new[] { sessionId },
            new SidebarCloseOptions(Confirm: false, GraceUndo: true));
        var pending = await WaitForSnapshotAsync(snapshots, snapshot => snapshot.PendingClose is not null);

        Assert.Equal(PendingCloseKind.Session, pending.PendingClose?.Kind);
        var realized = new List<Guid>();
        var selectionChanges = 0;
        await _coordinator.MutateAsync(store =>
        {
            store.SurfaceRealizer = session =>
            {
                realized.Add(session.Id);
                return new TestSurface();
            };
            store.SelectionChanged += () => selectionChanges++;
        });
        // Drop the snapshots published before the close; they also have no pending close.
        snapshots.Clear();
        controller.UndoClose(pending.PendingClose!.Id);
        await WaitForSnapshotAsync(snapshots, snapshot => snapshot.PendingClose is null);
        Assert.True(await _coordinator.ReadAsync(store => store.SessionWithId(sessionId) is not null));
        Assert.Equal(new[] { sessionId }, realized);
        Assert.Equal(1, selectionChanges);
    }

    [Fact]
    public async Task Pending_close_summary_publishes_creation_and_grace_expiry()
    {
        var snapshots = new ConcurrentQueue<SidebarSnapshot>();
        using var controller = CreateController(_ => Task.FromResult(true));
        controller.SnapshotChanged += snapshots.Enqueue;
        controller.Start();
        var sessionId = await _coordinator.ReadAsync(store => store.SelectedSessionId!.Value);

        await _coordinator.MutateAsync(store => store.SoftCloseSession(sessionId, grace: 0.05));

        await WaitForSnapshotAsync(snapshots, snapshot => snapshot.PendingClose is not null);
        await WaitForSnapshotAsync(snapshots, snapshot => snapshot.PendingClose is null);
    }

    [Fact]
    public async Task Pending_close_grace_expiry_uses_the_injected_host_scheduler()
    {
        var scheduledDir = Path.Combine(_stateDir, "scheduled");
        var persistence = new PersistenceStore(scheduledDir);
        var store = new AppStore(persistence: persistence, stateDirectory: scheduledDir);
        var workspace = store.AddWorkspace("workspace");
        var session = store.AddSession(workspace.Id, _stateDir)!;
        var scheduled = new TaskCompletionSource<Action>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        store.PendingCloseFinalizationScheduler = action => scheduled.TrySetResult(action);

        Assert.True(store.SoftCloseSession(session.Id, grace: 0.01));
        var finalize = await scheduled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(store.PendingCloseSummary);
        finalize();
        Assert.Null(store.PendingCloseSummary);
    }

    [Fact]
    public async Task Live_application_reopens_the_shared_latest_recent_item_into_the_target_window()
    {
        var stateDirectory = Directory.CreateTempSubdirectory("agterm-recent-").FullName;
        try
        {
            using var application = new LiveApplicationHost(new LiveApplicationHostOptions
            {
                StateDir = stateDirectory,
                PipeName = "agterm-recent-" + Guid.NewGuid().ToString("N"),
                HomeDirectory = stateDirectory,
            });
            var windowId = application.LaunchWindowIds.Single();
            var window = application.GetOpenWindow(windowId);
            Guid sessionId = default;
            var realized = new List<Guid>();
            var selectionChanges = 0;
            var sidebarChanges = 0;
            window.RunLocked(() =>
            {
                sessionId = window.Store.SelectedSessionId!.Value;
                window.Store.CloseSession(sessionId);
                window.Store.SurfaceRealizer = session =>
                {
                    realized.Add(session.Id);
                    return new TestSurface();
                };
                window.Store.SelectionChanged += () => selectionChanges++;
                window.Store.SidebarChanged += () => sidebarChanges++;
            });

            var recent = Assert.Single(application.RecentClosedItems);
            Assert.Equal(sessionId, recent.Session?.Snapshot.Id);
            Assert.True(await application.ReopenLatestRecentClosedAsync(windowId));

            window.RunLocked(() => Assert.NotNull(window.Store.SessionWithId(sessionId)));
            Assert.Equal(new[] { sessionId }, realized);
            Assert.Equal(1, selectionChanges);
            Assert.True(sidebarChanges > 0);
            Assert.Empty(application.RecentClosedItems);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Live_application_consumes_a_recent_item_only_once_across_two_windows()
    {
        var stateDirectory = Directory.CreateTempSubdirectory("agterm-recent-race-").FullName;
        try
        {
            using var application = new LiveApplicationHost(new LiveApplicationHostOptions
            {
                StateDir = stateDirectory,
                PipeName = "agterm-recent-race-" + Guid.NewGuid().ToString("N"),
                HomeDirectory = stateDirectory,
                WindowController = new ImmediateWindowController(),
            });
            var firstWindowId = application.LaunchWindowIds.Single();
            var create = await application.CreateWindowAsync();
            Assert.True(create.Ok, create.Error);
            var secondWindowId = Guid.Parse(create.Result!.Id!);
            var firstWindow = application.GetOpenWindow(firstWindowId);
            var secondWindow = application.GetOpenWindow(secondWindowId);
            firstWindow.RunLocked(() =>
                firstWindow.Store.CloseSession(firstWindow.Store.SelectedSessionId!.Value));
            var firstRealizerEntered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirstRealizer = new ManualResetEventSlim(false);
            firstWindow.RunLocked(() => firstWindow.Store.SurfaceRealizer = _ =>
            {
                firstRealizerEntered.TrySetResult();
                releaseFirstRealizer.Wait(TimeSpan.FromSeconds(5));
                return new TestSurface();
            });
            secondWindow.RunLocked(() => secondWindow.Store.SurfaceRealizer = _ => new TestSurface());

            var first = application.ReopenLatestRecentClosedAsync(firstWindowId);
            await firstRealizerEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var second = application.ReopenLatestRecentClosedAsync(secondWindowId);
            await Task.Delay(200);
            releaseFirstRealizer.Set();
            var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, results.Count(result => result));
            Assert.Empty(application.RecentClosedItems);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [Fact]
    public void WinUI_routes_close_and_clear_commands_through_the_async_confirmation_workflow()
    {
        var root = FindRepoRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root,
            "src", "AgTerm.App", "MainWindow.xaml.cs"));
        var sidebar = File.ReadAllText(Path.Combine(root,
            "src", "AgTerm.App", "SidebarView.xaml.cs"));
        var sidebarXaml = File.ReadAllText(Path.Combine(root,
            "src", "AgTerm.App", "SidebarView.xaml"));

        Assert.Contains("confirm: ConfirmSidebarActionAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.CloseSession] = Async", mainWindow, StringComparison.Ordinal);
        Assert.Contains("sidebar.CloseSessionsAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_settings.ConfirmCloseSession == true", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_settings.CloseGraceUndoEnabled ?? true", mainWindow, StringComparison.Ordinal);
        Assert.Contains("request.PrimaryButtonText", mainWindow, StringComparison.Ordinal);
        Assert.Contains("RequestCloseSessions", sidebar, StringComparison.Ordinal);
        Assert.Contains("RequestClearFlags", sidebar, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClearFlaggedButton\"", sidebarXaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Clear flagged sessions\"", sidebarXaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WinUI_routes_workspace_rename_and_delete_through_current_snapshot_workflows()
    {
        var root = FindRepoRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root,
            "src", "AgTerm.App", "MainWindow.xaml.cs"));
        var sidebar = File.ReadAllText(Path.Combine(root,
            "src", "AgTerm.App", "SidebarView.xaml.cs"));
        var controller = File.ReadAllText(Path.Combine(root,
            "src", "AgTerm.Xterm", "SidebarController.cs"));

        Assert.Contains("public void BeginRenameCurrentWorkspace()", sidebar, StringComparison.Ordinal);
        Assert.Contains("var snapshot = _snapshot;", sidebar, StringComparison.Ordinal);
        Assert.Contains("i.IsWorkspace && i.Id == snapshot?.CurrentWorkspaceId", sidebar,
            StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.RenameWorkspace] = Sync", mainWindow, StringComparison.Ordinal);
        Assert.Contains(
            "() => _latestSidebarSnapshot is { Mode: SidebarMode.Tree, CurrentWorkspaceId: not null },",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains("Sidebar.BeginRenameCurrentWorkspace", mainWindow, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.DeleteWorkspace] = Async", mainWindow, StringComparison.Ordinal);
        Assert.Contains("CanRemoveWorkspace: true, CurrentWorkspaceId: not null", mainWindow,
            StringComparison.Ordinal);
        Assert.Contains("var currentWorkspaceId = _latestSidebarSnapshot?.CurrentWorkspaceId;", mainWindow,
            StringComparison.Ordinal);
        Assert.Contains("await sidebar.DeleteWorkspaceAsync(", mainWindow, StringComparison.Ordinal);
        Assert.Contains("currentWorkspaceId.Value, CurrentSidebarCloseOptions().GraceUndo", mainWindow,
            StringComparison.Ordinal);
        Assert.Contains("RequestDeleteWorkspace(", sidebar, StringComparison.Ordinal);
        Assert.Contains("CurrentCloseOptions().GraceUndo", sidebar, StringComparison.Ordinal);
        Assert.DoesNotContain("_controller?.DeleteWorkspace(row.Id)", sidebar, StringComparison.Ordinal);
        Assert.Contains("public void RequestDeleteWorkspace(Guid workspaceId, bool graceUndo)", controller,
            StringComparison.Ordinal);
        Assert.Contains("() => DeleteWorkspaceAsync(workspaceId, graceUndo)", controller,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WinUI_projects_pending_close_to_an_undo_bar_and_routes_reopen_recent()
    {
        var root = FindRepoRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root,
            "src", "AgTerm.App", "MainWindow.xaml.cs"));
        var mainWindowXaml = File.ReadAllText(Path.Combine(root,
            "src", "AgTerm.App", "MainWindow.xaml"));
        var router = File.ReadAllText(Path.Combine(root,
            "src", "AgTerm.App", "WindowCommandRouter.cs"));

        Assert.Contains("x:Name=\"CloseUndoBanner\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnUndoCloseClick\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ApplyPendingClose(snapshot.PendingClose)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("sidebar.UndoClose(pendingCloseId)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.ReopenRecent] = Async", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ReopenLatestRecentClosedAsync(_windowId)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.ReopenRecent] = true", router, StringComparison.Ordinal);
    }

    private SidebarController CreateController(Func<SidebarConfirmationRequest, Task<bool>> confirm) =>
        new(_coordinator, runOnUi: work => work(), confirm: confirm);

    private sealed class TestSurface : ITerminalSurface
    {
        public string? PaneToken => null;
        public void Teardown() { }
        public void PromoteToPrimaryPane() { }
    }

    private sealed class ImmediateWindowController : ILiveWindowController
    {
        public Task OpenWindowAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ActivateWindowAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CloseWindowAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<LiveWindowPresentation?> CaptureWindowAsync(
            Guid windowId, CancellationToken cancellationToken) => Task.FromResult<LiveWindowPresentation?>(null);
        public Task<int> GetDisplayCountAsync(CancellationToken cancellationToken) => Task.FromResult(1);
        public Task ResizeWindowAsync(
            Guid windowId, int width, int height, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<LiveWindowMoveResult> MoveWindowAsync(
            Guid windowId, int x, int y, int? display, CancellationToken cancellationToken) =>
            Task.FromResult(new LiveWindowMoveResult(true, 1));
        public Task ToggleZoomAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ToggleFullscreenAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static async Task<SidebarSnapshot> WaitForSnapshotAsync(
        ConcurrentQueue<SidebarSnapshot> snapshots,
        Func<SidebarSnapshot, bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (snapshots.LastOrDefault(predicate) is { } snapshot) return snapshot;
            await Task.Delay(10);
        }
        throw new TimeoutException("Expected sidebar snapshot was not published.");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}