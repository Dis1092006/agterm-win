using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// SidebarController — the UI-free control plane of the WinUI sidebar: it snapshots the row
/// projection UNDER the host gate, marshals it to the UI via the injected dispatcher, and turns
/// every sidebar gesture into the same AppStore mutations the control socket performs (the third
/// caller, upstream-style). No WebView2 involved — driven against a HeadlessHost.
/// </summary>
public sealed class SidebarControllerTests : IDisposable
{
    private readonly string _stateDir = Directory.CreateTempSubdirectory("agterm-sbc-").FullName;
    private readonly HeadlessHost _host;
    private readonly LiveStoreCoordinator _coordinator;
    private readonly SidebarController _controller;
    private readonly List<SidebarSnapshot> _snapshots = new();

    public SidebarControllerTests()
    {
        _host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            PipeName = "agterm-sbc-" + Guid.NewGuid().ToString("N"),
            HomeDirectory = _stateDir,
        });
        // inline scheduler — coordinator runs store work synchronously on the calling thread
        _coordinator = new LiveStoreCoordinator(
            _host,
            isUiThread: () => false,
            scheduler: (work, ct) => { ct.ThrowIfCancellationRequested(); work(); return Task.CompletedTask; });
        // the "UI dispatcher" runs inline — tests observe snapshots synchronously
        _controller = new SidebarController(_coordinator, runOnUi: work => work());
        _controller.SnapshotChanged += _snapshots.Add;
        _controller.Start();
    }

    public void Dispose()
    {
        _controller.Dispose();
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

    private SidebarSnapshot Latest => _snapshots[^1];

    private void Mutate(Action<AppStore> action) =>
        _coordinator.MutateAsync(action).GetAwaiter().GetResult();

    [Fact]
    public void Start_broadcasts_the_bootstrap_snapshot()
    {
        Assert.NotEmpty(_snapshots);
        var row = Assert.Single(Latest.Rows, r => r.Kind == SidebarRowKind.Session);
        Assert.Equal(Latest.SelectedSessionId, row.Id);
        Assert.True(Latest.SidebarVisible);
        Assert.Equal(SidebarMode.Tree, Latest.Mode);
        Assert.Single(Latest.Workspaces);
        Assert.False(Latest.CanRemoveWorkspace);
    }

    [Fact]
    public void Store_mutations_rebroadcast_through_the_injected_dispatcher()
    {
        var before = _snapshots.Count;
        Mutate(store => store.AddWorkspace("w2"));
        Assert.True(_snapshots.Count > before);
        Assert.Equal(2, Latest.Workspaces.Count);
        Assert.True(Latest.CanRemoveWorkspace);
    }

    [Fact]
    public void Controller_gestures_mutate_the_store_like_the_socket_would()
    {
        Guid w2 = default, background = default;
        Mutate(store =>
        {
            w2 = store.AddWorkspace("w2", clearFocus: false).Id;
            background = store.AddSession(w2, _stateDir, select: false)!.Id;
        });

        _controller.Select(background);
        Assert.Equal(background, Latest.SelectedSessionId);

        _controller.RenameSession(background, "api");
        Assert.Equal("api", Latest.Rows.Single(r => r.Id == background).Label);

        _controller.SetFlag(new[] { background }, on: true);
        Assert.True(Latest.Rows.Single(r => r.Id == background).Flagged);
        Assert.Equal(1, Latest.FlaggedCount);

        _controller.ToggleFlaggedView();
        Assert.Equal(SidebarMode.Flagged, Latest.Mode);
        Assert.Equal("api : w2", Assert.Single(Latest.Rows).Label);
        _controller.ToggleFlaggedView();

        _controller.ToggleWorkspaceCollapse(w2);
        Assert.True(Latest.Rows.Single(r => r.Id == w2).Collapsed);
        Assert.DoesNotContain(Latest.Rows, r => r.Id == background);
        _controller.ToggleWorkspaceCollapse(w2); // expand it back for the row asserts below
        Assert.False(Latest.Rows.Single(r => r.Id == w2).Collapsed);

        _controller.FocusWorkspaceToggle(w2);
        Assert.Equal(w2, Latest.FocusedWorkspaceId);
        Assert.DoesNotContain(Latest.Rows, r => r.Kind == SidebarRowKind.Workspace && r.Id != w2);
        _controller.ClearFocus();
        Assert.Null(Latest.FocusedWorkspaceId);

        _controller.ClearStatuses(new[] { background });
        Assert.Equal(AgentStatus.Idle, Latest.Rows.Single(r => r.Id == background).Indicator.Status);
    }

    [Fact]
    public void New_duplicate_close_and_delete_follow_the_upstream_paths()
    {
        var w1 = Latest.Workspaces.Single().Id;
        var initial = Latest.SelectedSessionId!.Value;

        _controller.NewSession(w1);
        var created = Latest.SelectedSessionId!.Value;
        Assert.NotEqual(initial, created);

        _controller.DuplicateSession(created);
        var duplicated = Latest.SelectedSessionId!.Value;
        Assert.NotEqual(created, duplicated);
        // the duplicate sits directly after its source, in the same workspace
        var ids = Latest.Rows.Where(r => r.Kind == SidebarRowKind.Session).Select(r => r.Id).ToList();
        Assert.Equal(ids.IndexOf(created) + 1, ids.IndexOf(duplicated));

        _controller.CloseSessions(new[] { duplicated, created });
        Assert.DoesNotContain(Latest.Rows, r => r.Id == created || r.Id == duplicated);

        Guid w2 = default;
        Mutate(store => w2 = store.AddWorkspace("w2", clearFocus: false).Id);
        _controller.DeleteWorkspace(w2);
        Assert.DoesNotContain(Latest.Workspaces, w => w.Id == w2);
    }

    [Fact]
    public void Hotkey_variants_act_on_the_active_session_and_workspace()
    {
        var initial = Latest.SelectedSessionId!.Value;
        _controller.NewSessionInActiveWorkspace();
        var created = Latest.SelectedSessionId!.Value;
        Assert.NotEqual(initial, created);

        _controller.ToggleActiveFlag();
        Assert.True(Latest.Rows.Single(r => r.Id == created).Flagged);
        _controller.ToggleActiveFlag();
        Assert.False(Latest.Rows.Single(r => r.Id == created).Flagged);

        _controller.Navigate(SessionNavigation.First);
        Assert.Equal(initial, Latest.SelectedSessionId);
        _controller.Navigate(SessionNavigation.Last);
        Assert.Equal(created, Latest.SelectedSessionId);

        _controller.FocusActiveWorkspaceToggle();
        Assert.NotNull(Latest.FocusedWorkspaceId);
        _controller.FocusActiveWorkspaceToggle();
        Assert.Null(Latest.FocusedWorkspaceId);

        _controller.DuplicateActiveSession();
        var duplicated = Latest.SelectedSessionId!.Value;
        Assert.NotEqual(created, duplicated);
        _controller.CloseActiveSession();
        Assert.DoesNotContain(Latest.Rows, r => r.Id == duplicated);
    }

    [Fact]
    public async Task Width_and_visibility_persist_through_the_store()
    {
        _controller.SetSidebarWidth(300);
        Assert.Equal(300, Latest.SidebarWidth);
        _controller.SetSidebarVisible(false);
        Assert.False(Latest.SidebarVisible);
        // survives a restart from the same state dir
        _controller.Dispose();
        await _coordinator.MutateAsync(store => store.Save());
        using var second = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            PipeName = "agterm-sbc2-" + Guid.NewGuid().ToString("N"),
            HomeDirectory = _stateDir,
        });
        Assert.Equal(300, second.Store.SidebarWidth);
        Assert.False(second.Store.SidebarVisible);
    }

    [Fact]
    public void Drop_gestures_apply_the_sidebar_drop_math()
    {
        Guid w1 = default, a = default, b = default, c = default;
        Mutate(store =>
        {
            w1 = store.Workspaces[0].Id;
            a = store.Workspaces[0].Sessions[0].Id;
            b = store.AddSession(w1, _stateDir, select: false)!.Id;
            c = store.AddSession(w1, _stateDir, select: false)!.Id;
        });

        // drag a into the slot before c (raw child index 2) → lands between b and c
        _controller.DropSessions(new[] { a },
            new SidebarDrop.SessionDropTarget.WorkspaceRow(w1, 3), rawChildIndex: 2);
        Assert.Equal(new[] { b, a, c },
            Latest.Rows.Where(r => r.Kind == SidebarRowKind.Session).Select(r => r.Id));

        Guid w2 = default;
        Mutate(store => w2 = store.AddWorkspace("w2", clearFocus: false).Id);
        var count = _snapshots.Count;
        _controller.DropWorkspace(w2, rawIndex: 2); // already last → no-op, no reorder broadcastworthy
        Assert.Equal(new[] { w1, w2 }, Latest.Workspaces.Select(w => w.Id));
        _ = count;

        _controller.DropWorkspace(w2, rawIndex: 0);
        Assert.Equal(new[] { w2, w1 }, Latest.Workspaces.Select(w => w.Id));
    }

    // =========================================================================
    // Lifecycle and async snapshot ordering — driven by a fake ILiveStoreCoordinator
    // =========================================================================

    [Fact]
    public void Lifecycle_Start_requests_initial_ReadAsync_and_subscribes_SidebarInvalidated()
    {
        var fake = new FakeCoordinator();
        fake.EnqueuePendingRead(); // consumed by Start
        fake.EnqueuePendingRead(); // consumed by the SidebarInvalidated-triggered read

        using var controller = new SidebarController(fake, runOnUi: work => work());
        controller.Start();
        Assert.Equal(1, fake.ReadAsyncCallCount);

        fake.RaiseSidebarInvalidated();
        Assert.Equal(2, fake.ReadAsyncCallCount);
    }

    [Fact]
    public void Lifecycle_stale_generation_dropped_when_newer_read_completes_first()
    {
        var fake = new FakeCoordinator();
        var snapshots = new List<SidebarSnapshot>();

        using var controller = new SidebarController(fake, runOnUi: work => work());
        controller.SnapshotChanged += snapshots.Add;

        var tcsInit = fake.EnqueuePendingRead(); // generation 0 (Start's initial read)
        var tcs1 = fake.EnqueuePendingRead();    // generation 1 (first SidebarInvalidated)
        var tcs2 = fake.EnqueuePendingRead();    // generation 2 (second SidebarInvalidated)

        controller.Start();
        fake.RaiseSidebarInvalidated();
        fake.RaiseSidebarInvalidated();

        var snap2 = MakeFakeSnapshot();
        var snap1 = MakeFakeSnapshot();

        // Newest (gen 2) completes first; controller uses ContinueWith(ExecuteSynchronously)
        // so SetResult drives the continuation synchronously on this thread.
        tcs2.SetResult(snap2);
        Assert.Single(snapshots);
        Assert.Same(snap2, snapshots[0]);

        // Oldest (gen 1) arrives late — generation check drops it
        tcs1.SetResult(snap1);
        Assert.Single(snapshots);

        // Initial read (gen 0) also dropped
        tcsInit.SetResult(MakeFakeSnapshot());
        Assert.Single(snapshots);
    }

    [Fact]
    public void Lifecycle_Dispose_suppresses_pending_read_delivery_and_unsubscribes()
    {
        var fake = new FakeCoordinator();
        var snapshots = new List<SidebarSnapshot>();

        using var controller = new SidebarController(fake, runOnUi: work => work());
        controller.SnapshotChanged += snapshots.Add;

        var tcsInit = fake.EnqueuePendingRead();
        controller.Start();
        Assert.Equal(1, fake.ReadAsyncCallCount);

        controller.Dispose();

        // Completing the pending read after Dispose must not deliver SnapshotChanged
        tcsInit.SetResult(MakeFakeSnapshot());
        Assert.Empty(snapshots);

        // SidebarInvalidated is unsubscribed; raising it must not trigger ReadAsync
        var callsBefore = fake.ReadAsyncCallCount;
        fake.RaiseSidebarInvalidated();
        Assert.Equal(callsBefore, fake.ReadAsyncCallCount);
    }

    private static SidebarSnapshot MakeFakeSnapshot() =>
        new SidebarSnapshot
        {
            Rows = Array.Empty<SidebarRow>(),
            ShapeKey = string.Empty,
            Workspaces = Array.Empty<SidebarWorkspaceInfo>(),
        };

    /// <summary>
    /// Minimal ILiveStoreCoordinator stub for lifecycle and async-ordering tests.
    /// ReadAsync expects T == SidebarSnapshot and returns pre-staged controllable tasks;
    /// MutateAsync records calls but is a no-op; events are manually raisable; Dispose is idempotent.
    /// </summary>
    private sealed class FakeCoordinator : ILiveStoreCoordinator
    {
        private int _disposed;
        private readonly Queue<TaskCompletionSource<SidebarSnapshot>> _pendingReads = new();

        public int ReadAsyncCallCount { get; private set; }
        public int MutateAsyncCallCount { get; private set; }

        public event Action? SidebarInvalidated;
        public event Action<Guid?>? SelectionChanged;

        public TaskCompletionSource<SidebarSnapshot> EnqueuePendingRead()
        {
            var tcs = new TaskCompletionSource<SidebarSnapshot>();
            _pendingReads.Enqueue(tcs);
            return tcs;
        }

        public Task<T> ReadAsync<T>(Func<AppStore, T> read, CancellationToken cancellationToken = default)
        {
            ReadAsyncCallCount++;
            var tcs = _pendingReads.Dequeue();
            return (Task<T>)(object)tcs.Task;
        }

        public Task MutateAsync(Action<AppStore> mutate, CancellationToken cancellationToken = default)
        {
            MutateAsyncCallCount++;
            return Task.CompletedTask;
        }

        public void RaiseSidebarInvalidated() => SidebarInvalidated?.Invoke();
        public void RaiseSelectionChanged(Guid? id) => SelectionChanged?.Invoke(id);

        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
    }
}
