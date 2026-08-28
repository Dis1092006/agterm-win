using AgTerm.ControlServer;
using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// RED phase (O3.1): contracts for PendingTerminalSurface, LiveDispatchScopeFactory, and
/// LiveSurfaceCoordinator. None of these types exist yet; the file is expected to fail to compile
/// until O3.2 / O3.3 add the production code.
///
/// Test groups:
///   1. PendingTerminalSurface IHeadlessSurface contract: unrealized shapes, null PaneToken,
///      no-op Promote/SelectAll, idempotent Teardown, ReservationToken, Ready.
///   2. LiveDispatchScopeFactory scope mechanics: ambient lifecycle, TryRegister, CompleteAsync
///      ordering, cancellation forwarding, scope isolation.
///   3. LiveSurfaceCoordinator scenarios: successful commit, close-before-commit,
///      replacement-before-commit, creation failure, timeout, and shutdown cancellation.
/// </summary>
public sealed class LiveSurfaceCoordinatorTests : IDisposable
{
    private readonly string _stateDir;

    public LiveSurfaceCoordinatorTests()
    {
        _stateDir = Directory.CreateTempSubdirectory("agterm-o3-").FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_stateDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // =========================================================================
    // Infrastructure helpers
    // =========================================================================

    private HeadlessHost BuildHost() =>
        new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-o3-" + Guid.NewGuid().ToString("N"),
        });

    private static XtermSurfaceOptions DefaultOptions(string cwd) =>
        new XtermSurfaceOptions { Argv = new[] { "cmd.exe" }, Cwd = cwd };

    private static LiveSurfaceCoordinator BuildCoordinator(
        HeadlessHost host,
        LiveDispatchScopeFactory scopes,
        Func<SurfaceOpenRequest, CancellationToken, Task<IHeadlessSurface>> opener,
        TimeSpan? realizeBudget = null,
        Action<Guid, IHeadlessSurface>? committed = null,
        Action<string>? log = null) =>
        new LiveSurfaceCoordinator(
            host, scopes, opener,
            realizeBudget ?? TimeSpan.FromSeconds(15),
            committed, log);

    /// <summary>Polls condition every 10 ms until it returns true or budget elapses.</summary>
    private static async Task SpinUntilAsync(Func<bool> condition, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("SpinUntilAsync: condition not met within budget");
            await Task.Delay(10);
        }
    }

    // =========================================================================
    // Group 1 — PendingTerminalSurface IHeadlessSurface contract
    // =========================================================================

    [Fact]
    public void Pending_Inject_returns_false()
    {
        var pending = new PendingTerminalSurface();
        Assert.False(pending.Inject("hello"));
    }

    [Fact]
    public void Pending_ReadScreenText_returns_null_for_all_overloads()
    {
        var pending = new PendingTerminalSurface();
        Assert.Null(pending.ReadScreenText(all: false, lines: null));
        Assert.Null(pending.ReadScreenText(all: true, lines: null));
        Assert.Null(pending.ReadScreenText(all: false, lines: 10));
    }

    [Fact]
    public void Pending_SelectedText_is_null()
    {
        var pending = new PendingTerminalSurface();
        Assert.Null(pending.SelectedText);
    }

    [Fact]
    public void Pending_SelectAll_is_noop()
    {
        var pending = new PendingTerminalSurface();
        pending.SelectAll();    // must not throw
        Assert.Null(pending.SelectedText); // still null after no-op select
    }

    [Fact]
    public void Pending_Paste_returns_false()
    {
        var pending = new PendingTerminalSurface();
        Assert.False(pending.Paste());
    }

    [Fact]
    public void Pending_PerformBindingAction_returns_false()
    {
        var pending = new PendingTerminalSurface();
        Assert.False(pending.PerformBindingAction("increase_font_size:2"));
        Assert.False(pending.PerformBindingAction("reset_font_size"));
        Assert.False(pending.PerformBindingAction("select_all"));
    }

    [Fact]
    public void Pending_PaneToken_is_null()
    {
        var pending = new PendingTerminalSurface();
        Assert.Null(pending.PaneToken);
    }

    [Fact]
    public void Pending_PromoteToPrimaryPane_is_noop()
    {
        var pending = new PendingTerminalSurface();
        pending.PromoteToPrimaryPane();    // must not throw
    }

    [Fact]
    public async Task Pending_Teardown_cancels_ReservationToken_and_Ready_completes_null()
    {
        var pending = new PendingTerminalSurface();
        Assert.False(pending.ReservationToken.IsCancellationRequested,
            "token must not be pre-canceled before teardown");

        pending.Teardown();

        Assert.True(pending.ReservationToken.IsCancellationRequested,
            "Teardown must cancel the reservation token");

        var result = await pending.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(result);
    }

    [Fact]
    public async Task Pending_Teardown_twice_does_not_throw_and_Ready_stays_null()
    {
        var pending = new PendingTerminalSurface();
        pending.Teardown();
        pending.Teardown();     // idempotent — must not throw

        var result = await pending.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(result);
    }

    // =========================================================================
    // Group 2 — LiveDispatchScopeFactory scope mechanics
    // =========================================================================

    [Fact]
    public async Task Scope_operation_does_not_run_before_CompleteAsync()
    {
        var factory = new LiveDispatchScopeFactory();
        var ran = false;
        using var scope = factory.Begin();

        Assert.True(factory.TryRegister(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        }), "TryRegister must return true when a scope is active");

        Assert.False(ran, "operation must not run before CompleteAsync");

        await scope.CompleteAsync(CancellationToken.None);

        Assert.True(ran, "operation must have run during CompleteAsync");
    }

    [Fact]
    public async Task Scope_CompleteAsync_awaits_the_registered_async_operation()
    {
        var factory = new LiveDispatchScopeFactory();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scope = factory.Begin();

        Assert.True(factory.TryRegister(async _ =>
        {
            await Task.Yield();
            reached.TrySetResult();
        }));

        var complete = scope.CompleteAsync(CancellationToken.None);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await complete.WaitAsync(TimeSpan.FromSeconds(5));
        // CompleteAsync resolves only after the async operation finishes
        Assert.True(complete.IsCompleted);
    }

    [Fact]
    public async Task Scope_CompleteAsync_starts_registered_operations_concurrently()
    {
        var factory = new LiveDispatchScopeFactory();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scope = factory.Begin();

        Assert.True(factory.TryRegister(async _ =>
        {
            firstEntered.TrySetResult();
            await release.Task;
        }));
        Assert.True(factory.TryRegister(async _ =>
        {
            secondEntered.TrySetResult();
            await release.Task;
        }));

        var complete = scope.CompleteAsync(CancellationToken.None);
        await Task.WhenAll(firstEntered.Task, secondEntered.Task)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(complete.IsCompleted);
        release.TrySetResult();
        await complete.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Scope_cancellation_token_reaches_operation()
    {
        var factory = new LiveDispatchScopeFactory();
        var receivedToken = CancellationToken.None;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scope = factory.Begin();

        Assert.True(factory.TryRegister(ct =>
        {
            receivedToken = ct;
            entered.TrySetResult();
            return Task.CompletedTask;
        }));

        using var cts = new CancellationTokenSource();
        var complete = scope.CompleteAsync(cts.Token);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(receivedToken.CanBeCanceled,
            "the token forwarded to the operation must be cancelable");

        await complete.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Scope_TryRegister_returns_false_after_CompleteAsync_clears_ambient()
    {
        var factory = new LiveDispatchScopeFactory();
        using (var scope = factory.Begin())
        {
            await scope.CompleteAsync(CancellationToken.None);
        }

        Assert.False(factory.TryRegister(_ => Task.CompletedTask),
            "TryRegister must return false once the ambient scope is cleared by CompleteAsync");
    }

    [Fact]
    public void Scope_TryRegister_returns_false_after_scope_Dispose_clears_ambient()
    {
        var factory = new LiveDispatchScopeFactory();
        var scope = factory.Begin();
        scope.Dispose();

        Assert.False(factory.TryRegister(_ => Task.CompletedTask),
            "TryRegister must return false once the ambient scope is cleared by Dispose");
    }

    [Fact]
    public void Second_factory_instance_does_not_see_first_factory_scope()
    {
        var factory1 = new LiveDispatchScopeFactory();
        var factory2 = new LiveDispatchScopeFactory();
        using var scope1 = factory1.Begin();

        Assert.False(factory2.TryRegister(_ => Task.CompletedTask),
            "a separate factory must not share the ambient of another factory");
    }

    [Fact]
    public async Task Second_sequential_scope_does_not_inherit_operations_from_first()
    {
        var factory = new LiveDispatchScopeFactory();
        var runCount = 0;

        using (var scope1 = factory.Begin())
        {
            Assert.True(factory.TryRegister(_ => { runCount++; return Task.CompletedTask; }));
            await scope1.CompleteAsync(CancellationToken.None);
        }

        Assert.Equal(1, runCount);

        using (var scope2 = factory.Begin())
        {
            await scope2.CompleteAsync(CancellationToken.None);
        }

        Assert.True(runCount == 1,
            "second scope must not re-run operations from the first scope");
    }

    // =========================================================================
    // Group 3 — LiveSurfaceCoordinator scenarios
    // =========================================================================

    // -------------------------------------------------------------------------
    // Scenario 1: Successful commit
    // slot holds pending while RunLocked is held; same FakeSurface committed; callback once.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Coordinator_successful_commit_replaces_slot_and_fires_callback_once()
    {
        using var host = BuildHost();
        var scopes = new LiveDispatchScopeFactory();
        var sessionId = host.Store.ActiveSession!.Id;
        var openerTcs = new TaskCompletionSource<IHeadlessSurface>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var committedCount = 0;
        Guid committedId = default;
        IHeadlessSurface? committedSurface = null;

        using var coordinator = BuildCoordinator(
            host, scopes,
            (_, _) => openerTcs.Task,
            committed: (id, s) => { committedId = id; committedSurface = s; committedCount++; });

        PendingTerminalSurface? pending = null;

        using var scope = scopes.Begin();
        host.RunLocked(() =>
        {
            pending = coordinator.Reserve(new SurfaceOpenRequest(
                sessionId, LiveSurfaceKind.Main, DefaultOptions(_stateDir)));

            // While still under RunLocked, the slot must already hold pending
            Assert.True(ReferenceEquals(host.Store.SessionWithId(sessionId)!.Surface, pending),
                "slot must hold the pending surface while RunLocked is held");
        });

        var completeTask = scope.CompleteAsync(CancellationToken.None);

        var fake = new FakeSurface { PaneToken = "tok-" + sessionId.ToString("N")[..8] };
        openerTcs.TrySetResult(fake);

        await completeTask.WaitAsync(TimeSpan.FromSeconds(10));

        // Committed callback fires exactly once
        Assert.Equal(1, committedCount);
        Assert.Equal(sessionId, committedId);
        Assert.Same(fake, committedSurface);

        // Slot now holds the real surface
        ITerminalSurface? slotAfter = null;
        host.RunLocked(() => slotAfter = host.Store.SessionWithId(sessionId)!.Surface);
        Assert.Same(fake, slotAfter);

        // Ready resolves to the committed surface
        var ready = await pending!.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(fake, ready);
    }

    [Fact]
    public async Task Coordinator_successful_commit_notifies_sidebar_after_surface_transition()
    {
        using var host = BuildHost();
        var scopes = new LiveDispatchScopeFactory();
        var sessionId = host.Store.ActiveSession!.Id;
        var openerTcs = new TaskCompletionSource<IHeadlessSurface>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var coordinator = BuildCoordinator(host, scopes, (_, _) => openerTcs.Task);

        var notifications = 0;
        using var scope = scopes.Begin();
        host.RunLocked(() =>
        {
            coordinator.Reserve(new SurfaceOpenRequest(
                sessionId, LiveSurfaceKind.Main, DefaultOptions(_stateDir)));
        });

        // Subscribe AFTER Reserve returns (not before)
        host.Store.SidebarChanged += () => notifications++;

        var completeTask = scope.CompleteAsync(CancellationToken.None);

        var fake = new FakeSurface { PaneToken = "sidebar-commit" };
        openerTcs.TrySetResult(fake);

        await completeTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task Coordinator_commit_callback_failure_does_not_fault_completion()
    {
        using var host = BuildHost();
        var scopes = new LiveDispatchScopeFactory();
        var sessionId = host.Store.ActiveSession!.Id;
        var fake = new FakeSurface { PaneToken = "callback-failure" };
        using var coordinator = BuildCoordinator(
            host,
            scopes,
            (_, _) => Task.FromResult<IHeadlessSurface>(fake),
            committed: (_, _) => throw new InvalidOperationException("test callback failure"));

        PendingTerminalSurface? pending = null;
        using var scope = scopes.Begin();
        host.RunLocked(() => pending = coordinator.Reserve(new SurfaceOpenRequest(
            sessionId, LiveSurfaceKind.Main, DefaultOptions(_stateDir))));

        await scope.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(fake, await pending!.Ready.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(fake, host.Store.SessionWithId(sessionId)!.Surface);
    }

    [Fact]
    public async Task Coordinator_unscoped_reservation_commits_after_caller_releases_host_gate()
    {
        using var host = BuildHost();
        var scopes = new LiveDispatchScopeFactory();
        var sessionId = host.Store.ActiveSession!.Id;
        var openerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeSurface { PaneToken = "unscoped" };

        using var coordinator = BuildCoordinator(host, scopes, (_, _) =>
        {
            openerEntered.TrySetResult();
            return Task.FromResult<IHeadlessSurface>(fake);
        });

        PendingTerminalSurface? pending = null;
        host.RunLocked(() =>
        {
            pending = coordinator.Reserve(new SurfaceOpenRequest(
                sessionId, LiveSurfaceKind.Main, DefaultOptions(_stateDir)));
            Assert.False(openerEntered.Task.IsCompleted,
                "unscoped opener must not run inside the caller's host-gate mutation");
        });

        await openerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var ready = await pending!.Ready.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(fake, ready);
        ITerminalSurface? slotAfter = null;
        host.RunLocked(() => slotAfter = host.Store.SessionWithId(sessionId)!.Surface);
        Assert.Same(fake, slotAfter);
    }

    // -------------------------------------------------------------------------
    // Scenario 2: Close-before-commit
    // session closed before opener returns; late surface must be torn down exactly once.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Coordinator_close_before_commit_tears_down_late_surface_once()
    {
        using var host = BuildHost();
        var scopes = new LiveDispatchScopeFactory();
        var sessionId = host.Store.ActiveSession!.Id;
        var openerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var openerTcs = new TaskCompletionSource<IHeadlessSurface>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var coordinator = BuildCoordinator(host, scopes, (_, _) =>
        {
            openerEntered.TrySetResult();
            return openerTcs.Task;
        });

        PendingTerminalSurface? pending = null;
        using var scope = scopes.Begin();

        host.RunLocked(() =>
        {
            pending = coordinator.Reserve(new SurfaceOpenRequest(
                sessionId, LiveSurfaceKind.Main, DefaultOptions(_stateDir)));
        });

        // Start CompleteAsync — this kicks off the opener asynchronously
        var completeTask = scope.CompleteAsync(CancellationToken.None);

        // Wait until the opener has been entered (opener is now in-flight)
        await openerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Close the session while the opener is in-flight
        host.RunLocked(() => host.Store.CloseSession(sessionId));

        // Resolve the opener — coordinator must detect the slot is stale and discard
        var fake = new FakeSurface { PaneToken = "late-close" };
        openerTcs.TrySetResult(fake);

        await completeTask.WaitAsync(TimeSpan.FromSeconds(10));

        // Cancellation may let CompleteAsync return before an ignoring opener resolves; its late
        // result must still be observed and torn down.
        await SpinUntilAsync(() => fake.TeardownCount > 0, TimeSpan.FromSeconds(5));
        Assert.Equal(1, fake.TeardownCount);

        // Ready completes null (session gone, never committed)
        var ready = await pending!.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(ready);
    }

    // -------------------------------------------------------------------------
    // Scenario 3: Replacement-before-commit
    // slot replaced before opener returns; arriving surface torn down once; replacement remains.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Coordinator_replacement_before_commit_tears_down_arriving_surface_once()
    {
        using var host = BuildHost();
        var scopes = new LiveDispatchScopeFactory();
        var sessionId = host.Store.ActiveSession!.Id;
        var openerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var openerTcs = new TaskCompletionSource<IHeadlessSurface>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var coordinator = BuildCoordinator(host, scopes, (_, _) =>
        {
            openerEntered.TrySetResult();
            return openerTcs.Task;
        });

        var replacement = new FakeSurface { PaneToken = "replacement" };
        PendingTerminalSurface? pending = null;
        using var scope = scopes.Begin();

        host.RunLocked(() =>
        {
            pending = coordinator.Reserve(new SurfaceOpenRequest(
                sessionId, LiveSurfaceKind.Main, DefaultOptions(_stateDir)));
        });

        // Start CompleteAsync — this kicks off the opener asynchronously
        var completeTask = scope.CompleteAsync(CancellationToken.None);

        // Wait until the opener has been entered (opener is now in-flight)
        await openerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Replace the slot while the opener is in-flight — arriving surface will find a different occupant
        host.RunLocked(() => host.Store.SessionWithId(sessionId)!.Surface = replacement);

        // Resolve the opener — coordinator must discard arriving and leave replacement in place
        var arriving = new FakeSurface { PaneToken = "arriving" };
        openerTcs.TrySetResult(arriving);

        await completeTask.WaitAsync(TimeSpan.FromSeconds(10));

        // Arriving surface must be torn down exactly once (slot had a different occupant)
        Assert.Equal(1, arriving.TeardownCount);

        // Replacement surface must still be in the slot
        ITerminalSurface? slotAfter = null;
        host.RunLocked(() => slotAfter = host.Store.SessionWithId(sessionId)!.Surface);
        Assert.Same(replacement, slotAfter);

        // Replacement is untouched
        Assert.Equal(0, replacement.TeardownCount);

        // Ready completes null (not committed)
        var ready = await pending!.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(ready);
    }

    // -------------------------------------------------------------------------
    // Scenario 4: Creation failure
    // opener throws; Ready completes null; current pending slot clears back to unrealized-null.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Coordinator_creation_failure_clears_current_slot_and_Ready_is_null()
    {
        using var host = BuildHost();
        var scopes = new LiveDispatchScopeFactory();
        var sessionId = host.Store.ActiveSession!.Id;

        using var coordinator = BuildCoordinator(
            host, scopes,
            (_, _) => Task.FromException<IHeadlessSurface>(
                new InvalidOperationException("test: opener always fails")));

        PendingTerminalSurface? pending = null;
        using var scope = scopes.Begin();

        host.RunLocked(() =>
        {
            pending = coordinator.Reserve(new SurfaceOpenRequest(
                sessionId, LiveSurfaceKind.Main, DefaultOptions(_stateDir)));
        });

        await scope.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        // Ready completes null (creation failed)
        var ready = await pending!.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(ready);

        // A settled failure is no longer mounted, preserving the pinned session-not-realized shape.
        ITerminalSurface? slotAfter = null;
        host.RunLocked(() => slotAfter = host.Store.SessionWithId(sessionId)!.Surface);
        Assert.Null(slotAfter);

        // Pending still reports unrealized shapes
        Assert.False(pending.Inject("x"));
        Assert.Null(pending.ReadScreenText(false, null));
    }

    [Fact]
    public async Task Coordinator_creation_failure_notifies_sidebar_after_surface_transition()
    {
        using var host = BuildHost();
        var scopes = new LiveDispatchScopeFactory();
        var sessionId = host.Store.ActiveSession!.Id;

        using var coordinator = BuildCoordinator(
            host, scopes,
            (_, _) => Task.FromException<IHeadlessSurface>(
                new InvalidOperationException("test: opener always fails")));

        var notifications = 0;
        PendingTerminalSurface? pending = null;
        using var scope = scopes.Begin();

        host.RunLocked(() =>
        {
            pending = coordinator.Reserve(new SurfaceOpenRequest(
                sessionId, LiveSurfaceKind.Main, DefaultOptions(_stateDir)));
        });

        // Subscribe AFTER Reserve returns (not before)
        host.Store.SidebarChanged += () => notifications++;

        await scope.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, notifications);
    }

    // -------------------------------------------------------------------------
    // Scenario 5: Timeout — opener ignores CancellationToken
    // Ready completes null promptly after budget; token passed to opener is canceled;
    // late surface (arriving after timeout) is torn down exactly once.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Coordinator_timeout_cancels_opener_token_and_tears_down_late_surface()
    {
        using var host = BuildHost();
        var scopes = new LiveDispatchScopeFactory();
        var sessionId = host.Store.ActiveSession!.Id;
        var openerTcs = new TaskCompletionSource<IHeadlessSurface>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken openerToken = default;
        var openerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var coordinator = BuildCoordinator(
            host, scopes,
            (_, ct) =>
            {
                openerToken = ct;
                openerStarted.TrySetResult();
                return openerTcs.Task;   // ignores ct intentionally
            },
            realizeBudget: TimeSpan.FromMilliseconds(150));

        PendingTerminalSurface? pending = null;
        using var scope = scopes.Begin();

        host.RunLocked(() =>
        {
            pending = coordinator.Reserve(new SurfaceOpenRequest(
                sessionId, LiveSurfaceKind.Main, DefaultOptions(_stateDir)));
        });

        // CompleteAsync triggers the opener; coordinator times out after 150 ms and returns
        await scope.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        // Wait for the opener to have started so we can inspect its token
        await openerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Ready must be null after timeout
        var ready = await pending!.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(ready);

        ITerminalSurface? slotAfter = null;
        host.RunLocked(() => slotAfter = host.Store.SessionWithId(sessionId)!.Surface);
        Assert.Null(slotAfter);

        // Token passed to opener must have been canceled
        Assert.True(openerToken.IsCancellationRequested,
            "coordinator must cancel the token passed to the opener after the realize budget elapses");

        // Resolve the late opener: coordinator must tear it down exactly once
        var late = new FakeSurface { PaneToken = "late-timeout" };
        openerTcs.TrySetResult(late);
        await SpinUntilAsync(() => late.TeardownCount > 0, TimeSpan.FromSeconds(5));
        Assert.Equal(1, late.TeardownCount);
    }

    // -------------------------------------------------------------------------
    // Scenario 6: Coordinator Dispose / shutdown cancellation
    // Dispose cancels pending operations; opener token is canceled; Ready null promptly;
    // late surface (arriving after dispose) is torn down exactly once.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Coordinator_dispose_cancels_pending_operation_and_tears_down_late_surface()
    {
        using var host = BuildHost();
        var scopes = new LiveDispatchScopeFactory();
        var sessionId = host.Store.ActiveSession!.Id;
        var openerTcs = new TaskCompletionSource<IHeadlessSurface>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken openerToken = default;
        var openerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var coordinator = BuildCoordinator(
            host, scopes,
            (_, ct) =>
            {
                openerToken = ct;
                openerStarted.TrySetResult();
                return openerTcs.Task;   // ignores ct intentionally
            });

        PendingTerminalSurface? pending = null;
        using var scope = scopes.Begin();

        host.RunLocked(() =>
        {
            pending = coordinator.Reserve(new SurfaceOpenRequest(
                sessionId, LiveSurfaceKind.Main, DefaultOptions(_stateDir)));
        });

        // Start CompleteAsync (kicks off the registered async operation)
        var completeTask = scope.CompleteAsync(CancellationToken.None);

        // Wait for opener to start so the operation is blocked waiting for the TCS
        await openerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Dispose the coordinator → must cancel its shutdown token
        coordinator.Dispose();

        // CompleteAsync must return promptly after shutdown cancellation
        await completeTask.WaitAsync(TimeSpan.FromSeconds(10));

        // Opener token must have been canceled
        Assert.True(openerToken.IsCancellationRequested,
            "coordinator Dispose must cancel the token passed to the opener");

        // Ready must complete null
        var ready = await pending!.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(ready);

        // Resolve the late opener: coordinator must tear it down exactly once
        var late = new FakeSurface { PaneToken = "late-dispose" };
        openerTcs.TrySetResult(late);
        await SpinUntilAsync(() => late.TeardownCount > 0, TimeSpan.FromSeconds(5));
        Assert.Equal(1, late.TeardownCount);
    }
}
