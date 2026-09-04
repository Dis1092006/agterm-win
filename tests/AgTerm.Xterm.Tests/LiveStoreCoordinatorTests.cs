using AgTerm.ControlServer;
using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// RED phase (O4.1 / O4.4): contracts for ILiveStoreCoordinator and LiveStoreCoordinator.
/// Neither type exists yet; the file is expected to fail to compile until the production code
/// is added in the following Green phase.
///
/// Test groups:
///   1. ReadAsync schedules the store delegate off the caller's SynchronizationContext.
///   2. MutateAsync runs off context; subsequent ReadAsync observes the mutation.
///   3. Serialization: concurrent reads/mutations queue behind the gate with no sleeps.
///   4. Pre-canceled token: delegates never run; tasks return canceled.
///   5. Dispose idempotent; subsequent Read/Mutate throw ObjectDisposedException; delegates
///      do not run.
///   6. Inline-scheduler + isUiThread=true faults Read and Mutate with
///      InvalidOperationException before delegates run; isUiThread=false succeeds.
///   7. SelectionChanged forwards the selected Guid?; SidebarInvalidated fires for
///      AddWorkspace and NotifySidebar; after Dispose neither event fires.
/// </summary>
public sealed class LiveStoreCoordinatorTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;

    public LiveStoreCoordinatorTests()
    {
        _stateDir = Directory.CreateTempSubdirectory("agterm-o4-").FullName;
        _host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-o4-" + Guid.NewGuid().ToString("N"),
        });
    }

    public void Dispose()
    {
        _host.Dispose();
        try { Directory.Delete(_stateDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private LiveStoreCoordinator BuildCoordinator(Func<bool>? isUiThread = null) =>
        new LiveStoreCoordinator(_host, isUiThread ?? (() => false));

    // Runs the scheduled work synchronously on the calling thread; no background threads.
    private static Task InlineScheduler(Action work, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        work();
        return Task.CompletedTask;
    }

    // A distinct SynchronizationContext subclass used only for identity checks.
    private sealed class CapturingSynchronizationContext : SynchronizationContext { }

    // =========================================================================
    // Contract: type implements the interface
    // =========================================================================

    [Fact]
    public void LiveStoreCoordinator_implements_ILiveStoreCoordinator()
    {
        using var coordinator = BuildCoordinator();
        Assert.IsAssignableFrom<ILiveStoreCoordinator>(coordinator);
    }

    // =========================================================================
    // Group 1 — ReadAsync runs delegate off the caller's SynchronizationContext
    // =========================================================================

    [Fact]
    public async Task ReadAsync_delegate_does_not_run_inside_caller_SynchronizationContext()
    {
        var customCtx = new CapturingSynchronizationContext();
        using var coordinator = BuildCoordinator(
            isUiThread: () => ReferenceEquals(SynchronizationContext.Current, customCtx));
        SynchronizationContext? delegateCtx = null;

        // Capture the task while the custom context is installed, then restore.
        var prior = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(customCtx);
        Task<int> task;
        try
        {
            task = coordinator.ReadAsync(store =>
            {
                delegateCtx = SynchronizationContext.Current;
                return 42;
            });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prior);
        }

        var result = await task;

        Assert.Equal(42, result);
        Assert.NotSame(customCtx, delegateCtx);
    }

    [Fact]
    public async Task ReadAsync_external_snapshot_capture_runs_before_entering_the_host_gate()
    {
        using var coordinator = BuildCoordinator();

        var result = await coordinator.ReadAsync(
            captureOutsideGate: () =>
            {
                using var gateEntered = new ManualResetEventSlim();
                var workspaceCount = 0;
                var competingGateEntry = new Thread(() =>
                {
                    _host.RunLocked(() => workspaceCount = _host.Store.Workspaces.Count);
                    gateEntered.Set();
                });
                competingGateEntry.Start();
                if (!gateEntered.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("External capture ran while the host gate was held.");
                competingGateEntry.Join();
                return workspaceCount;
            },
            readUnderGate: (store, capturedWorkspaceCount) =>
                (Captured: capturedWorkspaceCount, UnderGate: store.Workspaces.Count));

        Assert.Equal(result.UnderGate, result.Captured);
    }

    // =========================================================================
    // Group 2 — MutateAsync runs off context; ReadAsync observes mutation
    // =========================================================================

    [Fact]
    public async Task MutateAsync_runs_off_context_and_ReadAsync_observes_mutation()
    {
        var customCtx = new CapturingSynchronizationContext();
        using var coordinator = BuildCoordinator(
            isUiThread: () => ReferenceEquals(SynchronizationContext.Current, customCtx));
        SynchronizationContext? mutateCtx = null;
        Guid addedId = default;

        // Capture the mutate task while the custom context is installed, then restore.
        var prior = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(customCtx);
        Task mutateTask;
        try
        {
            mutateTask = coordinator.MutateAsync(store =>
            {
                mutateCtx = SynchronizationContext.Current;
                addedId = store.AddWorkspace("observed-ws").Id;
            });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prior);
        }

        await mutateTask;

        Assert.NotSame(customCtx, mutateCtx);

        var found = await coordinator.ReadAsync(store =>
            store.Workspaces.Any(w => w.Id == addedId));

        Assert.True(found);
    }

    // =========================================================================
    // Group 3 — Serialization: second operation queues behind gate
    // =========================================================================

    [Fact]
    public async Task Operations_serialize_under_gate_without_sleeps()
    {
        // gate1 keeps the first mutate blocked inside RunLocked.
        var gate1 = new ManualResetEventSlim(false);
        var tcsFirstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsSecondPreGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // isUiThread is called from within each scheduled work item, before RunLocked.
        // The second call signals that the second worker has reached the pre-gate guard.
        var workerCount = 0;
        bool IsUiThread()
        {
            if (Interlocked.Increment(ref workerCount) == 2)
                tcsSecondPreGate.TrySetResult();
            return false;
        }

        using var coordinator = new LiveStoreCoordinator(_host, IsUiThread);

        // Start first mutation; it signals entry then blocks inside the gate.
        var task1 = coordinator.MutateAsync(store =>
        {
            tcsFirstEntered.TrySetResult();
            gate1.Wait(); // holds the gate until we release it
        });

        await tcsFirstEntered.Task;

        // Start second read after we know first has the gate.
        bool secondRan = false;
        var task2 = coordinator.ReadAsync(store =>
        {
            secondRan = true;
            return 0;
        });

        // Wait for the second worker to call isUiThread (it is now blocked on the gate).
        await tcsSecondPreGate.Task;

        // First still holds the gate; second's store delegate has not run.
        Assert.False(secondRan);

        // Release first; both complete in order.
        gate1.Set();

        await Task.WhenAll(task1, task2);

        Assert.True(secondRan);
    }

    // =========================================================================
    // Group 4 — Pre-canceled token: delegates never run
    // =========================================================================

    [Fact]
    public async Task PreCanceled_ReadAsync_returns_canceled_without_running_delegate()
    {
        using var coordinator = BuildCoordinator();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        bool delegateRan = false;
        var task = coordinator.ReadAsync(store =>
        {
            delegateRan = true;
            return 0;
        }, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(delegateRan);
    }

    [Fact]
    public async Task PreCanceled_MutateAsync_returns_canceled_without_running_delegate()
    {
        using var coordinator = BuildCoordinator();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        bool delegateRan = false;
        var task = coordinator.MutateAsync(store =>
        {
            delegateRan = true;
        }, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(delegateRan);
    }

    // =========================================================================
    // Group 5 — Dispose idempotent; subsequent calls throw ObjectDisposedException
    // =========================================================================

    [Fact]
    public async Task Dispose_is_idempotent_and_subsequent_operations_throw_ObjectDisposedException()
    {
        var coordinator = BuildCoordinator();

        // Double-dispose must not throw.
        coordinator.Dispose();
        coordinator.Dispose();

        bool readDelegateRan = false;
        bool mutateDelegateRan = false;

        var readTask = coordinator.ReadAsync(store =>
        {
            readDelegateRan = true;
            return 0;
        });

        var mutateTask = coordinator.MutateAsync(store =>
        {
            mutateDelegateRan = true;
        });

        await Assert.ThrowsAsync<ObjectDisposedException>(() => readTask);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => mutateTask);

        Assert.False(readDelegateRan);
        Assert.False(mutateDelegateRan);
    }

    [Fact]
    public async Task Dispose_after_worker_guard_but_before_gate_prevents_delegate()
    {
        var guardEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseGuard = new ManualResetEventSlim(false);
        var coordinator = new LiveStoreCoordinator(_host, () =>
        {
            guardEntered.TrySetResult();
            releaseGuard.Wait();
            return false;
        });
        var delegateRan = false;
        var task = coordinator.ReadAsync(store =>
        {
            delegateRan = true;
            return 0;
        });

        await guardEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.Dispose();
        releaseGuard.Set();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => task);
        Assert.False(delegateRan);
    }

    // =========================================================================
    // Group 6 — Inline scheduler + isUiThread guard
    // =========================================================================

    [Fact]
    public async Task Inline_scheduler_isUiThread_true_faults_ReadAsync_with_InvalidOperationException()
    {
        using var coordinator = new LiveStoreCoordinator(_host, () => true, InlineScheduler);

        bool delegateRan = false;
        var task = coordinator.ReadAsync(store =>
        {
            delegateRan = true;
            return 0;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.False(delegateRan);
    }

    [Fact]
    public async Task Inline_scheduler_isUiThread_true_faults_MutateAsync_with_InvalidOperationException()
    {
        using var coordinator = new LiveStoreCoordinator(_host, () => true, InlineScheduler);

        bool delegateRan = false;
        var task = coordinator.MutateAsync(store =>
        {
            delegateRan = true;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.False(delegateRan);
    }

    [Fact]
    public async Task Inline_scheduler_isUiThread_false_ReadAsync_succeeds_and_returns_value()
    {
        using var coordinator = new LiveStoreCoordinator(_host, () => false, InlineScheduler);

        bool delegateRan = false;
        var result = await coordinator.ReadAsync(store =>
        {
            delegateRan = true;
            return 99;
        });

        Assert.Equal(99, result);
        Assert.True(delegateRan);
    }

    [Fact]
    public async Task Inline_scheduler_isUiThread_false_MutateAsync_succeeds()
    {
        using var coordinator = new LiveStoreCoordinator(_host, () => false, InlineScheduler);

        bool delegateRan = false;
        await coordinator.MutateAsync(store =>
        {
            delegateRan = true;
        });

        Assert.True(delegateRan);
    }

    // =========================================================================
    // Group 7 — Events: SelectionChanged and SidebarInvalidated
    // =========================================================================

    [Fact]
    public async Task SelectionChanged_event_forwards_the_newly_selected_session_id()
    {
        using var coordinator = BuildCoordinator();
        var received = new TaskCompletionSource<Guid?>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.SelectionChanged += id => received.TrySetResult(id);

        Guid newId = default;
        _host.RunLocked(() =>
        {
            var ws = _host.Store.Workspaces[0];
            // Add without select so SelectionChanged fires exactly once on SelectSession below.
            newId = _host.Store.AddSession(ws.Id, _stateDir, select: false)!.Id;
            _host.Store.SelectSession(newId);
        });

        Assert.Equal(newId, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task SelectionSnapshotChanged_event_carries_session_id_and_display_name()
    {
        using var coordinator = BuildCoordinator();
        var received = new TaskCompletionSource<LiveSelectionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid newId = default;
        coordinator.SelectionSnapshotChanged += snap =>
        {
            if (snap.SessionId == newId)
                received.TrySetResult(snap);
        };

        string workspaceName = "";
        _host.RunLocked(() =>
        {
            var ws = _host.Store.Workspaces[0];
            workspaceName = ws.Name;
            newId = _host.Store.AddSession(ws.Id, _stateDir, name: "selected-title", select: false)!.Id;
            _host.Store.SelectSession(newId);
        });

        var snapshot = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(newId, snapshot.SessionId);
        Assert.Equal("selected-title", snapshot.DisplayName);
        Assert.Equal(workspaceName, snapshot.WorkspaceName);
    }

    [Fact]
    public async Task Selection_snapshot_refreshes_after_sidebar_invalidation()
    {
        using var coordinator = BuildCoordinator();
        var received = new TaskCompletionSource<LiveSelectionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.SelectionSnapshotChanged += snap => received.TrySetResult(snap);

        Guid capturedId = default;
        _host.RunLocked(() =>
        {
            var session = _host.Store.ActiveSession ?? throw new InvalidOperationException("No selected session");
            session.CustomName = null;
            session.OscTitle = "osc-layout-title";
            capturedId = session.Id;
            _host.Store.NotifySidebar();
        });

        var snapshot = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(capturedId, snapshot.SessionId);
        Assert.Equal("osc-layout-title", snapshot.DisplayName);
    }

    [Fact]
    public async Task SidebarInvalidated_event_fires_when_workspace_is_added()
    {
        using var coordinator = BuildCoordinator();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.SidebarInvalidated += () => received.TrySetResult();

        _host.RunLocked(() => _host.Store.AddWorkspace("invalidation-ws"));

        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SidebarInvalidated_event_fires_for_explicit_NotifySidebar()
    {
        using var coordinator = BuildCoordinator();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.SidebarInvalidated += () => received.TrySetResult();

        _host.RunLocked(() => _host.Store.NotifySidebar());

        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void After_Dispose_neither_SelectionChanged_nor_SidebarInvalidated_fires()
    {
        var coordinator = BuildCoordinator();
        int selCount = 0;
        int sidebarCount = 0;
        coordinator.SelectionChanged += _ => selCount++;
        coordinator.SidebarInvalidated += () => sidebarCount++;

        coordinator.Dispose();

        _host.RunLocked(() =>
        {
            _host.Store.AddWorkspace("after-dispose-ws");
            _host.Store.NotifySidebar();
        });

        Assert.Equal(0, selCount);
        Assert.Equal(0, sidebarCount);
    }

    // =========================================================================
    // Group 8 — Late-subscriber race: historical queued event must not be delivered
    // =========================================================================

    [Fact]
    public void Subscriber_added_after_event_was_queued_does_not_receive_historical_selection()
    {
        // Holding scheduler: enqueues each Action + CancellationToken, returns a TCS task.
        // Work is not executed until the test manually drains the queue.
        var queue = new List<(Action work, CancellationToken ct, TaskCompletionSource tcs)>();
        Task HoldingScheduler(Action work, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Add((work, ct, tcs));
            return tcs.Task;
        }

        using var coordinator = new LiveStoreCoordinator(_host, () => false, HoldingScheduler);

        // Trigger a selection change BEFORE subscribing to SelectionChanged.
        // OnSelectionChanged calls QueueEvent which calls HoldingScheduler, so delivery
        // closures are added to the queue but not yet executed.
        Guid firstId = default;
        _host.RunLocked(() =>
        {
            var ws = _host.Store.Workspaces[0];
            firstId = _host.Store.AddSession(ws.Id, _stateDir, select: false)!.Id;
            _host.Store.SelectSession(firstId);
        });
        int historicalCount = queue.Count;

        // Subscribe AFTER the historical event was already queued.
        var receivedIds = new List<Guid?>();
        coordinator.SelectionChanged += id => receivedIds.Add(id);

        // Manually execute all scheduler items queued by the historical selection change.
        // Selection change queues at least a SelectionChanged delivery and a SelectionSnapshotChanged
        // delivery; drain all of them in FIFO order.
        var historicalItems = queue.GetRange(0, historicalCount);
        queue.RemoveRange(0, historicalCount);
        foreach (var (work, _, tcs) in historicalItems)
        {
            try { work(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        }

        // Late subscriber must not receive the historical selection event.
        Assert.Empty(receivedIds);

        // Trigger a second selection change AFTER subscription using a distinct new session ID.
        Guid secondId = default;
        _host.RunLocked(() =>
        {
            var ws = _host.Store.Workspaces[0];
            secondId = _host.Store.AddSession(ws.Id, _stateDir, select: false)!.Id;
            _host.Store.SelectSession(secondId);
        });

        // Drain all scheduler items queued by the post-subscription event.
        var postItems = queue.ToList();
        queue.Clear();
        foreach (var (work, _, tcs) in postItems)
        {
            try { work(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        }

        // Subscriber must receive exactly the second selection ID once.
        Assert.Equal(secondId, Assert.Single(receivedIds));
    }

    // =========================================================================
    // Group 9 — Snapshot atomicity: payload captured when event is produced
    // =========================================================================

    [Fact]
    public void Selection_snapshot_captures_title_and_workspace_when_event_is_produced()
    {
        // Holding scheduler: enqueues each Action + CancellationToken, returns a TCS task.
        // Work is not executed until the test manually drains the queue.
        var queue = new List<(Action work, CancellationToken ct, TaskCompletionSource tcs)>();
        Task HoldingScheduler(Action work, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Add((work, ct, tcs));
            return tcs.Task;
        }

        using var coordinator = new LiveStoreCoordinator(_host, () => false, HoldingScheduler);

        // Subscribe BEFORE producing the event.
        var receivedSnapshots = new List<LiveSelectionSnapshot>();
        coordinator.SelectionSnapshotChanged += snap => receivedSnapshots.Add(snap);

        // Under RunLocked: add workspace "event-workspace", add session "event-title", select it.
        // This queues event deliveries but does NOT execute them yet.
        Guid sessionId = default;
        _host.RunLocked(() =>
        {
            var ws = _host.Store.AddWorkspace("event-workspace");
            var session = _host.Store.AddSession(ws.Id, _stateDir, name: "event-title", select: false)!;
            sessionId = session.Id;
            _host.Store.SelectSession(sessionId);
        });

        // Still before draining: directly mutate CustomName and workspace Name without calling
        // Save/NotifySidebar/selection methods, so no second public event is produced.
        _host.RunLocked(() =>
        {
            _host.Store.SessionWithId(sessionId)!.CustomName = "later-title";
            _host.Store.WorkspaceForSession(sessionId)!.Name = "later-workspace";
        });

        // Drain all queued scheduler items in FIFO order.
        var items = queue.ToList();
        queue.Clear();
        foreach (var (work, _, tcs) in items)
        {
            try { work(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        }

        // The snapshot must reflect state AT event-production time, not the later-mutated state.
        Assert.NotEmpty(receivedSnapshots);
        var snapshot = receivedSnapshots.Last();
        Assert.Equal("event-title", snapshot.DisplayName);
        Assert.Equal("event-workspace", snapshot.WorkspaceName);
    }
}
