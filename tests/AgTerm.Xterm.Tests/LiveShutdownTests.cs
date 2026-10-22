using AgTerm.ControlServer;
using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

public sealed class LiveShutdownTests : IDisposable
{
    private readonly string _stateDir;

    public LiveShutdownTests()
    {
        _stateDir = Directory.CreateTempSubdirectory("agterm-l3-").FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_stateDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private HeadlessHost BuildHost() =>
        new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-l3-" + Guid.NewGuid().ToString("N"),
        });

    private static XtermSurfaceOptions DefaultOptions(string cwd) =>
        new XtermSurfaceOptions { Argv = new[] { "cmd.exe" }, Cwd = cwd };

    private static LiveSurfaceCoordinator BuildCoordinator(
        HeadlessHost host,
        LiveDispatchScopeFactory scopes,
        Func<SurfaceOpenRequest, CancellationToken, Task<IHeadlessSurface>> opener,
        TimeSpan? realizeBudget = null,
        Action<Guid, IHeadlessSurface>? committed = null) =>
        new LiveSurfaceCoordinator(
            host, scopes, opener,
            realizeBudget ?? TimeSpan.FromSeconds(15),
            committed);

    [Fact]
    public async Task Shutdown_during_surface_bootstrap_cancels_and_awaits_the_opener()
    {
        using var host = BuildHost();
        var scopes = new LiveDispatchScopeFactory();
        var sessionId = host.Store.ActiveSession!.Id;

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken openerToken = default;
        var committedCount = 0;

        var coordinator = BuildCoordinator(
            host, scopes,
            async (_, ct) =>
            {
                openerToken = ct;
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                finally
                {
                    returned.TrySetResult();
                }
                return null!; // unreachable — cancellation throws before this line
            },
            committed: (_, _) => committedCount++);

        try
        {
            PendingTerminalSurface? pending = null;

            using var scope = scopes.Begin();
            host.RunLocked(() =>
            {
                pending = coordinator.Reserve(new SurfaceOpenRequest(
                    sessionId, LiveSurfaceKind.Main, DefaultOptions(_stateDir)));
            });

            var completeTask = scope.CompleteAsync(CancellationToken.None);

            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await coordinator.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(openerToken.IsCancellationRequested,
                "ShutdownAsync must cancel the token passed to the opener");

            Assert.True(returned.Task.IsCompleted,
                "opener finally block must have run before ShutdownAsync returns");

            Assert.Equal(0, committedCount);

            var ready = await pending!.Ready.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(ready);

            await completeTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            coordinator.Dispose();
        }
    }

    [Fact]
    public async Task Shutdown_waits_for_synchronously_blocking_opener()
    {
        using var host = BuildHost();
        var scopes = new LiveDispatchScopeFactory();
        var sessionId = host.Store.ActiveSession!.Id;

        var release = new ManualResetEventSlim(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken openerToken = default;
        var committedCount = 0;

        // Non-async delegate: blocks synchronously before returning a Task.
        var coordinator = BuildCoordinator(
            host, scopes,
            (_, ct) =>
            {
                openerToken = ct;
                entered.TrySetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(30)),
                    "blocked opener was not released");
                return Task.FromCanceled<IHeadlessSurface>(ct);
            },
            committed: (_, _) => committedCount++);

        Task? completeTask = null;
        Task? shutdownTask = null;
        IControlDispatchScope? scope = null;
        try
        {
            scope = scopes.Begin();
            host.RunLocked(() =>
            {
                coordinator.Reserve(new SurfaceOpenRequest(
                    sessionId, LiveSurfaceKind.Main, DefaultOptions(_stateDir)));
            });

            // Run CompleteAsync on a thread pool thread so the synchronously blocking opener
            // does not freeze the test thread.
            completeTask = Task.Run(() => scope.CompleteAsync(CancellationToken.None));

            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            shutdownTask = coordinator.ShutdownAsync();
            await Task.Yield();

            Assert.False(shutdownTask.IsCompleted,
                "ShutdownAsync must not complete while the synchronously blocking opener holds the thread");
        }
        finally
        {
            release.Set();
            if (shutdownTask is not null)
                await shutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
            if (completeTask is not null)
                await completeTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(openerToken.IsCancellationRequested);
            Assert.Equal(0, committedCount);
            scope?.Dispose();
            coordinator.Dispose();
            release.Dispose();
        }
    }

    [Fact]
    public async Task Shutdown_waits_for_queued_unscoped_realization_registration()
    {
        using var host = BuildHost();
        var scopes = new LiveDispatchScopeFactory();
        var sessionId = host.Store.ActiveSession!.Id;

        Func<Task>? queuedWork = null;
        var scheduled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduledCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task Schedule(Func<Task> work)
        {
            queuedWork = work;
            scheduled.TrySetResult();
            return scheduledCompletion.Task;
        }

        var openerCount = 0;

        var coordinator = new LiveSurfaceCoordinator(
            host, scopes,
            (_, _) =>
            {
                Interlocked.Increment(ref openerCount);
                return Task.FromResult<IHeadlessSurface>(new GenericRecordingSurface());
            },
            TimeSpan.FromSeconds(15),
            schedule: Schedule);

        PendingTerminalSurface? pending = null;
        try
        {
            // Reserve with no ambient scope — triggers unscoped scheduling
            host.RunLocked(() =>
            {
                pending = coordinator.Reserve(new SurfaceOpenRequest(
                    sessionId, LiveSurfaceKind.Main, DefaultOptions(_stateDir)));
            });

            await scheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(queuedWork);

            var shutdownTask = coordinator.ShutdownAsync();
            await Task.Yield();

            Assert.False(shutdownTask.IsCompleted,
                "ShutdownAsync must not complete: the queued work registered as active before being scheduled");

            // Run the captured work — short-circuits before the opener because shutdown is in progress
            await queuedWork!();
            scheduledCompletion.TrySetResult();

            await shutdownTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, openerCount);
            var ready = await pending!.Ready.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(ready);
        }
        finally
        {
            scheduledCompletion.TrySetResult();
            coordinator.Dispose();
        }
    }

    // ─── RecordingSurface (local test fake) ───────────────────────────────────

    private sealed class RecordingSurface : IHeadlessSurface, ILiveShutdownSurface
    {
        private int _teardownCount;
        private int _stopOutputCount;
        private int _shutdownAsyncCount;

        public int TeardownCount => Volatile.Read(ref _teardownCount);
        public int StopOutputCount => Volatile.Read(ref _stopOutputCount);
        public int ShutdownAsyncCount => Volatile.Read(ref _shutdownAsyncCount);

        public Action? OnStopOutput { get; set; }
        public Action? OnShutdownAsync { get; set; } = () => { };

        public string? PaneToken => null;
        public bool Inject(string text) => false;
        public string? ReadScreenText(bool all, int? lines) => null;
        public string? SelectedText => null;
        public void SelectAll() { }
        public bool Paste() => false;
        public bool PerformBindingAction(string action) => false;
        public void PromoteToPrimaryPane() { }

        public void Teardown()
        {
            Interlocked.Increment(ref _teardownCount);
        }

        public void StopOutput()
        {
            if (Interlocked.CompareExchange(ref _stopOutputCount, 1, 0) == 0)
                OnStopOutput?.Invoke();
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _shutdownAsyncCount, 1, 0) == 0)
                OnShutdownAsync?.Invoke();
            return Task.CompletedTask;
        }
    }

    private static LiveControlHost BuildLiveHost(
        string dir,
        List<RecordingSurface> surfaces,
        Func<RecordingSurface>? surfaceFactory = null) =>
        new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = dir,
            ConfigDir = dir,
            PipeName = "agterm-l3-host-" + Guid.NewGuid().ToString("N"),
            DefaultShell = "cmd.exe",
            HomeDirectory = dir,
            OpenSurface = (_, _) =>
            {
                var s = surfaceFactory?.Invoke() ?? new RecordingSurface();
                lock (surfaces) surfaces.Add(s);
                return Task.FromResult<IHeadlessSurface>(s);
            },
        });

    // ─── Host-level helpers ─────────────────────────────────────────────────────

    private static async Task WaitForCommittedRecordingSurfaceCountAsync(
        LiveControlHost host, int expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var count = await host.StoreCoordinator.ReadAsync(
                store => store.Workspaces.SelectMany(w => w.Sessions)
                               .Count(s => s.Surface is RecordingSurface));
            if (count >= expected) return;
            await Task.Delay(10);
        }

        Assert.Fail($"Expected {expected} committed recording surfaces before timeout.");
    }

    // ─── Host-level shutdown tests ────────────────────────────────────────────

    [Fact]
    public async Task ShutdownAsync_is_idempotent_and_tears_down_five_surfaces_once()
    {
        var dir = Directory.CreateTempSubdirectory("agterm-l3-h1-").FullName;
        try
        {
            var surfaces = new List<RecordingSurface>();
            var host = BuildLiveHost(dir, surfaces);

            await host.StoreCoordinator.MutateAsync(store =>
            {
                var wsId = store.CurrentWorkspaceId!.Value;
                for (var i = 0; i < 4; i++)
                {
                    var cwd = Directory.CreateTempSubdirectory("agterm-l3-s-").FullName;
                    store.AddSession(wsId, cwd, select: false);
                }
            });

            await WaitForCommittedRecordingSurfaceCountAsync(host, 5);

            RecordingSurface[] snapshot;
            lock (surfaces) snapshot = surfaces.ToArray();
            Assert.Equal(5, snapshot.Length);

            var first = host.ShutdownAsync();
            var second = host.ShutdownAsync();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.All(snapshot, s => Assert.Equal(1, s.StopOutputCount));
            Assert.All(snapshot, s => Assert.Equal(1, s.ShutdownAsyncCount));
            Assert.All(snapshot, s => Assert.Equal(0, s.TeardownCount));
            Assert.IsAssignableFrom<IAsyncDisposable>(host);

            await host.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.All(snapshot, s => Assert.Equal(1, s.StopOutputCount));
            Assert.All(snapshot, s => Assert.Equal(1, s.ShutdownAsyncCount));
            Assert.All(snapshot, s => Assert.Equal(0, s.TeardownCount));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
        }
    }

    [Fact]
    public async Task ShutdownAsync_awaits_surfaces_held_by_pending_close_records()
    {
        var surfaces = new List<RecordingSurface>();
        using var host = BuildLiveHost(_stateDir, surfaces);
        await WaitForCommittedRecordingSurfaceCountAsync(host, 1);
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);

        await host.StoreCoordinator.MutateAsync(store =>
        {
            var session = store.SessionWithId(sessionId)!;
            session.SplitSurface = session.Surface;
            Assert.True(store.SoftCloseSession(sessionId, grace: 60));
        });
        await host.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));

        var surface = Assert.Single(surfaces);
        Assert.Equal(1, surface.StopOutputCount);
        Assert.Equal(1, surface.ShutdownAsyncCount);
        Assert.Equal(0, surface.TeardownCount);
        host.Host.RunLocked(() => Assert.Null(host.Host.Store.PendingCloseSummary));
    }

    [Fact]
    public async Task ShutdownAsync_saves_snapshot_before_surface_teardown()
    {
        var dir = Directory.CreateTempSubdirectory("agterm-l3-h2-").FullName;
        try
        {
            var markerObserved = false;
            var snapshotPath = Path.Combine(dir, "workspaces.json");
            var surfaces = new List<RecordingSurface>();

            var host = BuildLiveHost(dir, surfaces, () => new RecordingSurface
            {
                OnShutdownAsync = () =>
                {
                    if (File.Exists(snapshotPath))
                    {
                        var text = File.ReadAllText(snapshotPath);
                        if (text.Contains("shutdown-snapshot-marker"))
                            markerObserved = true;
                    }
                },
            });

            await host.StoreCoordinator.MutateAsync(store =>
            {
                store.ActiveSession!.CustomName = "shutdown-snapshot-marker";
            });

            await host.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(markerObserved,
                "snapshot must be written with the marker before surfaces are torn down");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
        }
    }

    [Fact]
    public async Task Dispose_is_a_bounded_compatibility_wrapper()
    {
        var dir = Directory.CreateTempSubdirectory("agterm-l3-h3-").FullName;
        try
        {
            var surfaces = new List<RecordingSurface>();
            var host = BuildLiveHost(dir, surfaces);

            await WaitForCommittedRecordingSurfaceCountAsync(host, 1);

            await Task.Run(() => host.Dispose()).WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Run(() => host.Dispose()).WaitAsync(TimeSpan.FromSeconds(5));

            RecordingSurface[] snapshot;
            lock (surfaces) snapshot = surfaces.ToArray();
            Assert.All(snapshot, s => Assert.Equal(1, s.StopOutputCount));
            Assert.All(snapshot, s => Assert.Equal(1, s.ShutdownAsyncCount));
            Assert.All(snapshot, s => Assert.Equal(0, s.TeardownCount));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
        }
    }

    [Fact]
    public async Task ShutdownAsync_stops_live_output_before_save_then_awaits_live_shutdown()
    {
        var dir = Directory.CreateTempSubdirectory("agterm-l3-h4-").FullName;
        try
        {
            const string marker = "phased-shutdown-marker";
            var snapshotPath = Path.Combine(dir, "workspaces.json");
            var phases = new List<string>();
            var surfaces = new List<RecordingSurface>();
            var markerPresentBeforeSave = false;
            var markerPresentAfterSave = false;

            var host = BuildLiveHost(dir, surfaces);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                lock (surfaces) { if (surfaces.Count >= 1) break; }
                await Task.Delay(10);
            }

            RecordingSurface surface;
            lock (surfaces) surface = surfaces[0];

            surface.OnStopOutput = () =>
            {
                var text = File.Exists(snapshotPath) ? File.ReadAllText(snapshotPath) : "";
                markerPresentBeforeSave = text.Contains(marker);
                lock (phases) phases.Add("stop-output");
            };

            await host.StoreCoordinator.MutateAsync(store =>
            {
                store.ActiveSession!.CustomName = marker;
            });

            surface.OnShutdownAsync = () =>
            {
                var text = File.Exists(snapshotPath) ? File.ReadAllText(snapshotPath) : "";
                markerPresentAfterSave = text.Contains(marker);
                lock (phases) phases.Add("shutdown");
            };

            await host.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, surface.StopOutputCount);
            Assert.Equal(1, surface.ShutdownAsyncCount);
            Assert.Equal(0, surface.TeardownCount);
            Assert.False(markerPresentBeforeSave);
            Assert.True(markerPresentAfterSave);

            string[] phaseOrder;
            lock (phases) phaseOrder = phases.ToArray();
            Assert.Equal(new[] { "stop-output", "shutdown" }, phaseOrder);

            // Idempotent: second call must not increment any counter
            await host.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, surface.StopOutputCount);
            Assert.Equal(1, surface.ShutdownAsyncCount);
            Assert.Equal(0, surface.TeardownCount);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
        }
    }

    // ─── BlockedOutputSurface (local test fake) ─────────────────────────────────

    private sealed class BlockedOutputSurface : IHeadlessSurface, ILiveShutdownSurface, IAsyncDisposable
    {
        private readonly TerminalOutputPump _pump;
        private readonly TaskCompletionSource _sinkStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _phaseOrder = new();
        private Task? _blockedWrite;
        private int _stopOutputStarted;

        public IReadOnlyList<string> PhaseOrder
        {
            get { lock (_phaseOrder) return _phaseOrder.ToArray(); }
        }

        public BlockedOutputSurface()
        {
            _pump = new TerminalOutputPump(
                new TerminalOutputPumpOptions
                {
                    QueueMaxBytes = 4,
                    QueueMaxItems = 4,
                    BatchMaxBytes = 4,
                    FlushInterval = TimeSpan.Zero,
                },
                data =>
                {
                    _sinkStarted.TrySetResult();
                    return new TaskCompletionSource().Task; // never completes — keeps consumer blocked
                });
        }

        public async Task<Task> PrepareBlockedProducerAsync()
        {
            await _pump.WriteAsync(new byte[] { 1, 2, 3, 4 });
            await _sinkStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await _pump.WriteAsync(new byte[] { 5, 6, 7, 8 });
            var blockedTask = _pump.WriteAsync(new byte[] { 9, 10, 11, 12 }).AsTask();
            Assert.False(blockedTask.IsCompleted,
                "third write must be blocking: budget is full while consumer is stuck in the sink");
            _blockedWrite = blockedTask;
            return blockedTask;
        }

        public void StopOutput()
        {
            if (Interlocked.CompareExchange(ref _stopOutputStarted, 1, 0) != 0)
                return;
            lock (_phaseOrder) _phaseOrder.Add("StopOutput");
            _pump.Dispose();
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            lock (_phaseOrder) _phaseOrder.Add("ShutdownAsync");
            if (_blockedWrite is not null)
            {
                try { await _blockedWrite.ConfigureAwait(false); }
                catch (ObjectDisposedException) { }
                catch (OperationCanceledException) { }
            }
            await _pump.DisposeAsync();
        }

        public string? PaneToken => null;
        public bool Inject(string text) => false;
        public string? ReadScreenText(bool all, int? lines) => null;
        public string? SelectedText => null;
        public void SelectAll() { }
        public bool Paste() => false;
        public bool PerformBindingAction(string action) => false;
        public void PromoteToPrimaryPane() { }
        public void Teardown() => StopOutput();

        public ValueTask DisposeAsync() => _pump.DisposeAsync();
    }

    // ─── GenericRecordingSurface (local test fake: IHeadlessSurface only) ────────

    private sealed class GenericRecordingSurface : IHeadlessSurface
    {
        private int _teardownCount;

        public int TeardownCount => Volatile.Read(ref _teardownCount);

        public string? PaneToken => null;
        public bool Inject(string text) => false;
        public string? ReadScreenText(bool all, int? lines) => null;
        public string? SelectedText => null;
        public void SelectAll() { }
        public bool Paste() => false;
        public bool PerformBindingAction(string action) => false;
        public void PromoteToPrimaryPane() { }

        public void Teardown()
        {
            Interlocked.CompareExchange(ref _teardownCount, 1, 0);
        }
    }

    // ─── Blocked-producer host test ───────────────────────────────────────────

    [Fact]
    public async Task Host_shutdown_releases_a_blocked_output_producer()
    {
        var dir = Directory.CreateTempSubdirectory("agterm-l3-h5-").FullName;
        BlockedOutputSurface? surface = null;
        try
        {
            var host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = dir,
                ConfigDir = dir,
                PipeName = "agterm-l3-host-" + Guid.NewGuid().ToString("N"),
                DefaultShell = "cmd.exe",
                HomeDirectory = dir,
                OpenSurface = (_, _) =>
                {
                    var s = new BlockedOutputSurface();
                    Volatile.Write(ref surface, s);
                    return Task.FromResult<IHeadlessSurface>(s);
                },
            });

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline && Volatile.Read(ref surface) is null)
                await Task.Delay(10);
            Assert.NotNull(surface);

            var blockedWrite = await surface!.PrepareBlockedProducerAsync();

            await host.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Exception? writeEx = null;
            try { await blockedWrite; }
            catch (Exception ex) { writeEx = ex; }
            Assert.True(writeEx is ObjectDisposedException or OperationCanceledException,
                $"blocked write must throw ODE or OCE, got: {writeEx?.GetType().Name ?? "none"}");

            Assert.Equal(new[] { "StopOutput", "ShutdownAsync" }, surface!.PhaseOrder);
        }
        finally
        {
            if (surface is not null)
                await surface.DisposeAsync();
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
        }
    }

    [Fact]
    public async Task ShutdownAsync_uses_Teardown_for_generic_surface()
    {
        var dir = Directory.CreateTempSubdirectory("agterm-l3-h7-").FullName;
        try
        {
            GenericRecordingSurface? surface = null;
            var host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = dir,
                ConfigDir = dir,
                PipeName = "agterm-l3-host-" + Guid.NewGuid().ToString("N"),
                DefaultShell = "cmd.exe",
                HomeDirectory = dir,
                OpenSurface = (_, _) =>
                {
                    var s = new GenericRecordingSurface();
                    Volatile.Write(ref surface, s);
                    return Task.FromResult<IHeadlessSurface>(s);
                },
            });

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var committed = await host.StoreCoordinator.ReadAsync(
                    store => store.Workspaces.SelectMany(w => w.Sessions)
                                   .Any(s => s.Surface is GenericRecordingSurface));
                if (committed) break;
                await Task.Delay(10);
            }

            var isCommitted = await host.StoreCoordinator.ReadAsync(
                store => store.Workspaces.SelectMany(w => w.Sessions)
                               .Any(s => s.Surface is GenericRecordingSurface));
            Assert.True(isCommitted, "GenericRecordingSurface must be committed within 5s");

            await host.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, surface!.TeardownCount);

            await host.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, surface!.TeardownCount);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
        }
    }

    [Fact]
    public async Task Bootstrap_cancellation_token_releases_live_host_constructor()
    {
        var dir = Directory.CreateTempSubdirectory("agterm-l3-h6-").FullName;
        var cts = new CancellationTokenSource();
        LiveControlHost? host = null;
        try
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var constructTask = Task.Run(() => new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = dir,
                ConfigDir = dir,
                PipeName = "agterm-l3-host-" + Guid.NewGuid().ToString("N"),
                DefaultShell = "cmd.exe",
                HomeDirectory = dir,
                BootstrapCancellationToken = cts.Token,
                OpenSurface = async (_, token) =>
                {
                    entered.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    }
                    finally
                    {
                        returned.TrySetResult();
                    }
                    return null!;
                },
            }));

            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(constructTask.IsCompleted,
                "constructor must still be blocked inside bootstrap while opener awaits");

            cts.Cancel();

            host = await constructTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(host);

            Assert.True(returned.Task.IsCompleted,
                "opener finally block must have run before the constructor returned");

            await host.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            cts.Cancel();
            host?.Dispose();
            cts.Dispose();
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
        }
    }

    // ─── L3.3 window close state machine ─────────────────────────────────────

    [Fact]
    public async Task Window_close_is_cancelled_until_shutdown_then_allows_one_reentry()
    {
        var shutdownCount = 0;
        var shutdownStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shutdownGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalCloseCount = 0;
        bool? reentryDecision = null;
        Action? capturedUiAction = null;

        WindowCloseCoordinator? coordinator = null;
        coordinator = new WindowCloseCoordinator(
            shutdown: async () =>
            {
                Interlocked.Increment(ref shutdownCount);
                shutdownStarted.TrySetResult();
                await shutdownGate.Task;
            },
            runOnUi: action => { capturedUiAction = action; },
            requestFinalClose: () =>
            {
                Interlocked.Increment(ref finalCloseCount);
                reentryDecision = coordinator!.ShouldCancelClose();
            });

        // First ShouldCancelClose triggers shutdown and returns true
        Assert.True(coordinator.ShouldCancelClose());
        await shutdownStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, shutdownCount);

        // Second call while gate is blocked: still true, no duplicate shutdown
        Assert.True(coordinator.ShouldCancelClose());
        Assert.Equal(1, shutdownCount);

        // Release shutdown
        shutdownGate.TrySetResult();
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // runOnUi captured the action but did not execute it yet
        Assert.Equal(0, finalCloseCount);
        Assert.NotNull(capturedUiAction);

        // Invoke the captured UI action
        capturedUiAction!();
        Assert.Equal(1, finalCloseCount);
        Assert.False(reentryDecision,
            "ShouldCancelClose called inside requestFinalClose must return false");

        // Further calls return false without triggering another requestFinalClose
        Assert.False(coordinator.ShouldCancelClose());
        Assert.False(coordinator.ShouldCancelClose());
        Assert.Equal(1, finalCloseCount);
    }

    // ─── ShutdownUiSynchronizationContext ─────────────────────────────────────

    private sealed class ShutdownUiSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            ThreadPool.QueueUserWorkItem(_ => d(state), null);

        public override void Send(SendOrPostCallback d, object? state) => d(state);
    }

    // ─── L3.4 shutdown thread-affinity ───────────────────────────────────────

    [Fact]
    public async Task ShutdownAsync_called_from_UI_context_runs_shutdown_phases_off_that_context()
    {
        var dir = Directory.CreateTempSubdirectory("agterm-l3-h8-").FullName;
        var uiContext = new ShutdownUiSynchronizationContext();
        var prior = SynchronizationContext.Current;
        var surfaces = new List<RecordingSurface>();
        SynchronizationContext? stopOutputContext = null;
        var stopOutputCalled = 0;
        LiveControlHost? host = null;

        SynchronizationContext.SetSynchronizationContext(uiContext);
        try
        {
            host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = dir,
                ConfigDir = dir,
                PipeName = "agterm-l3-h8-" + Guid.NewGuid().ToString("N"),
                DefaultShell = "cmd.exe",
                HomeDirectory = dir,
                IsUiThread = () => SynchronizationContext.Current is ShutdownUiSynchronizationContext,
                OpenSurface = (_, _) =>
                {
                    var s = new RecordingSurface
                    {
                        OnStopOutput = () =>
                        {
                            Interlocked.Increment(ref stopOutputCalled);
                            stopOutputContext = SynchronizationContext.Current;
                        },
                    };
                    lock (surfaces) surfaces.Add(s);
                    return Task.FromResult<IHeadlessSurface>(s);
                },
            });

            // Wait for the surface commit off the UI context to avoid StoreCoordinator UI-thread guard.
            await Task.Run(() => WaitForCommittedRecordingSurfaceCountAsync(host, 1))
                      .WaitAsync(TimeSpan.FromSeconds(10));

            // Re-install the UI context on the continuation thread before calling ShutdownAsync.
            SynchronizationContext.SetSynchronizationContext(uiContext);

            // Call ShutdownAsync from the UI context without ConfigureAwait(false) at the call site.
            await host.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));

            RecordingSurface[] snapshot;
            lock (surfaces) snapshot = surfaces.ToArray();
            Assert.NotEmpty(snapshot);
            Assert.Equal(1, stopOutputCalled);
            Assert.False(stopOutputContext is ShutdownUiSynchronizationContext,
                "StopOutput must not execute on the UI synchronization context");
        }
        finally
        {
            host?.Dispose();
            SynchronizationContext.SetSynchronizationContext(prior);
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
        }
    }
}

// ─── L3 real surface teardown (WebView2) ─────────────────────────────────────

[Collection("webview2")]
public sealed class LiveRealSurfaceShutdownTests
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _fixture;

    public LiveRealSurfaceShutdownTests(WebView2Fixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(XtermPage Page, Microsoft.Web.WebView2.WinForms.WebView2 View)> BootPageAsync()
    {
        var view = await _fixture.CreateWebViewAsync();
        var page = await _fixture.RunAsync(async _ =>
        {
            var p = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await p.InitializeAsync();
            return p;
        });
        return (page, view);
    }

    [Fact]
    public async Task Surface_shutdown_waits_for_child_exit_and_renderer_removal_and_is_idempotent()
    {
        var (page, view) = await BootPageAsync();
        var surface = await _fixture.RunAsync(async _ =>
            await XtermPaneSurface.OpenAsync(page, new XtermSurfaceOptions
            {
                Argv = new[] { Cmd },
            }));

        XtermPaneSurface? surfaceForCleanup = surface;
        try
        {
            var token = surface.PaneToken!;

            var first = surface.ShutdownAsync();
            var second = surface.ShutdownAsync();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(surface.Pane.Exited.IsCompleted);
            await surface.Pane.Exited.WaitAsync(TimeSpan.FromSeconds(5));

            var raw = await _fixture.RunAsync(async _ =>
                await view.CoreWebView2.ExecuteScriptAsync($"agterm.text(\"{token}\", true, null)"));
            Assert.Equal("null", raw);

            surfaceForCleanup = null;

            await surface.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
            raw = await _fixture.RunAsync(async _ =>
                await view.CoreWebView2.ExecuteScriptAsync($"agterm.text(\"{token}\", true, null)"));
            Assert.Equal("null", raw);
        }
        finally
        {
            surfaceForCleanup?.Teardown();
        }
    }

    [Fact]
    public async Task Five_live_sessions_shutdown_children_and_remove_renderers_within_budget()
    {
        var dir = Directory.CreateTempSubdirectory("agterm-l3-live5-").FullName;
        var surfaces = new List<XtermPaneSurface>();
        LiveControlHost? host = null;

        try
        {
            var (page, view) = await BootPageAsync();

            host = await Task.Run(() => new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = dir,
                ConfigDir = dir,
                PipeName = "agterm-l3-live5-" + Guid.NewGuid().ToString("N"),
                DefaultShell = Cmd,
                HomeDirectory = dir,
                OpenSurface = async (request, ct) =>
                {
                    var s = await _fixture.RunAsync(async _ =>
                        await XtermPaneSurface.OpenAsync(page, request.Options, ct));
                    lock (surfaces) surfaces.Add(s);
                    return s;
                },
            }));

            await host.StoreCoordinator.MutateAsync(store =>
            {
                var wsId = store.CurrentWorkspaceId!.Value;
                for (var i = 0; i < 4; i++)
                    store.AddSession(wsId, dir, select: false);
            });

            var pollDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < pollDeadline)
            {
                int count;
                lock (surfaces) count = surfaces.Count;
                if (count >= 5) break;
                await Task.Delay(50);
            }

            XtermPaneSurface[] snapshot;
            lock (surfaces) snapshot = surfaces.ToArray();
            Assert.Equal(5, snapshot.Length);

            var tokens = snapshot.Select(s => s.PaneToken!).ToArray();

            await host.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));

            foreach (var s in snapshot)
            {
                Assert.True(s.Pane.Exited.IsCompleted,
                    $"pane {s.PaneToken} must be exited after host shutdown");
                await s.Pane.Exited.WaitAsync(TimeSpan.FromSeconds(5));
            }

            foreach (var token in tokens)
            {
                var jsonToken = System.Text.Json.JsonSerializer.Serialize(token);
                var raw = await _fixture.RunAsync(async _ =>
                    await view.CoreWebView2.ExecuteScriptAsync(
                        $"agterm.text({jsonToken}, true, null)"));
                Assert.Equal("null", raw);
            }

            // Give CloseOnExitAsync continuations a chance to enter the host gate (up to 2 s).
            // Stop early as soon as the count diverges from 5 — this is the bug to catch.
            int inMemoryCount = 5;
            var bugDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (DateTime.UtcNow < bugDeadline)
            {
                host.Host.RunLocked(() =>
                {
                    inMemoryCount = host.Host.Store.Workspaces.SelectMany(w => w.Sessions).Count();
                });
                if (inMemoryCount != 5) break;
                await Task.Delay(10);
            }

            Assert.Equal(5, inMemoryCount);

            var persisted = new PersistenceStore(dir).Load();
            Assert.Equal(5, persisted.Workspaces.SelectMany(w => w.Sessions).Count());
        }
        finally
        {
            if (host is not null)
                try { await host.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            lock (surfaces)
            {
                foreach (var s in surfaces)
                    try { s.Teardown(); } catch (Exception) { }
            }
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
        }
    }
}
