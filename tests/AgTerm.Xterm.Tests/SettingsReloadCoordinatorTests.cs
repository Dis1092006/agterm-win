using AgTerm.App;
using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

// RED: AgTerm.App.SettingsReloadCoordinator does not exist yet.
// Tests define the required constructor and Request() contract.
public sealed class SettingsReloadCoordinatorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static AppSettings MakeSettings(string? theme = null) =>
        new() { Theme = theme };

    // -----------------------------------------------------------------------
    // Test 1: Request() returns immediately while load is pending; no apply
    // before the load completes; completing the load dispatches exactly once.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Request_returns_immediately_and_applies_exactly_once_after_load_completes()
    {
        var loadGate = new TaskCompletionSource<AppSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        var applyCalls = new List<AppSettings>();
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<AppSettings> Load(CancellationToken _) => loadGate.Task;
        Task Apply(AppSettings s) { applyCalls.Add(s); applied.TrySetResult(); return Task.CompletedTask; }

        using var coordinator = new SettingsReloadCoordinator(Load, a => a(), Apply);

        // Request() must not block.
        coordinator.Request();

        // No apply before load finishes.
        Assert.Empty(applyCalls);

        // Complete the load.
        var expected = MakeSettings("agterm");
        loadGate.SetResult(expected);
        await applied.Task.WaitAsync(Timeout);
        await Task.Delay(100); // a second apply, if any, would arrive here

        Assert.Single(applyCalls);
        Assert.Equal(expected, applyCalls[0]);
    }

    // -----------------------------------------------------------------------
    // Test 2: Two requests; second load completes first → only second settings
    // applied; the late-arriving first load result is dropped (latest wins).
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Second_load_completing_first_wins_and_late_first_load_is_dropped()
    {
        var load1Gate = new TaskCompletionSource<AppSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        var load2Gate = new TaskCompletionSource<AppSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callIndex = 0;
        var applyCalls = new List<AppSettings>();
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<AppSettings> Load(CancellationToken _)
        {
            var index = Interlocked.Increment(ref callIndex);
            return index == 1 ? load1Gate.Task : load2Gate.Task;
        }

        Task Apply(AppSettings s) { applyCalls.Add(s); applied.TrySetResult(); return Task.CompletedTask; }

        using var coordinator = new SettingsReloadCoordinator(Load, a => a(), Apply);

        coordinator.Request(); // starts load1
        coordinator.Request(); // starts load2 (newest)

        var settings2 = MakeSettings("dark");
        load2Gate.SetResult(settings2); // second completes first
        await applied.Task.WaitAsync(Timeout);

        var settings1 = MakeSettings("light");
        load1Gate.SetResult(settings1); // first arrives late
        await Task.Delay(100);

        // Only settings2 must have been applied; settings1 is stale and dropped.
        Assert.Single(applyCalls);
        Assert.Equal(settings2, applyCalls[0]);
    }

    // -----------------------------------------------------------------------
    // Test 3: First apply blocks; second loaded result queues; both eventually
    // apply in order (max 1 concurrent apply, serialised).
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Concurrent_apply_is_bounded_to_one_and_second_apply_runs_after_first_completes()
    {
        // Loads complete instantly.
        var callIndex = 0;
        Task<AppSettings> Load(CancellationToken _) =>
            Task.FromResult(MakeSettings($"theme-{Interlocked.Increment(ref callIndex)}"));

        var apply1Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var apply1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var apply2Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applyCallCount = 0;
        var applyCalls = new List<AppSettings>();

        async Task Apply(AppSettings s)
        {
            var n = Interlocked.Increment(ref applyCallCount);
            applyCalls.Add(s);
            if (n == 1)
            {
                apply1Started.TrySetResult();
                await apply1Gate.Task;
            }
            else
            {
                apply2Started.TrySetResult();
            }
        }

        using var coordinator = new SettingsReloadCoordinator(Load, a => a(), Apply);

        coordinator.Request(); // load1 completes immediately, apply1 starts + blocks
        await apply1Started.Task.WaitAsync(Timeout);

        coordinator.Request(); // load2 completes immediately, must wait for apply1

        // apply1 is still blocking; only one apply call so far.
        Assert.Equal(1, applyCallCount);

        // Release apply1.
        apply1Gate.SetResult();
        await apply2Started.Task.WaitAsync(Timeout);

        // Both settings were applied, in load order.
        Assert.Equal(2, applyCallCount);
    }

    // -----------------------------------------------------------------------
    // Test 4: Dispose while load is pending suppresses later UI dispatch and
    // apply; double-dispose is idempotent.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Dispose_while_load_pending_suppresses_apply_and_is_idempotent()
    {
        var loadGate = new TaskCompletionSource<AppSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiDispatchCount = 0;
        var applyCount = 0;

        Task<AppSettings> Load(CancellationToken ct)
        {
            // Honour cancellation so dispose can signal the load to stop early,
            // but we also complete it manually to exercise the suppress path.
            ct.Register(() => loadGate.TrySetCanceled(ct));
            return loadGate.Task;
        }

        void RunOnUi(Action a) { Interlocked.Increment(ref uiDispatchCount); a(); }

        Task Apply(AppSettings _) { Interlocked.Increment(ref applyCount); return Task.CompletedTask; }

        var coordinator = new SettingsReloadCoordinator(Load, RunOnUi, Apply);
        coordinator.Request();

        coordinator.Dispose(); // dispose before load result arrives

        // Complete the load after dispose.
        try { loadGate.TrySetResult(MakeSettings()); } catch { /* already cancelled */ }

        await Task.Delay(100); // let any pending continuations run

        Assert.Equal(0, uiDispatchCount);
        Assert.Equal(0, applyCount);

        // Second dispose must not throw.
        coordinator.Dispose();
    }

    // -----------------------------------------------------------------------
    // Test 5: Load or apply exception reaches the error sink and does not
    // prevent a subsequent Request() from applying.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Load_or_apply_exception_reaches_error_sink_and_later_request_still_applies()
    {
        var failLoad = true;
        Task<AppSettings> Load(CancellationToken _)
        {
            if (failLoad)
                throw new InvalidOperationException("load failure");
            return Task.FromResult(MakeSettings("recovered"));
        }

        var errors = new List<Exception>();
        var errorReported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applyCalls = new List<AppSettings>();
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Apply(AppSettings s) { applyCalls.Add(s); applied.TrySetResult(); return Task.CompletedTask; }

        using var coordinator = new SettingsReloadCoordinator(
            Load,
            a => a(),
            Apply,
            error: e => { errors.Add(e); errorReported.TrySetResult(); });

        // First request — load throws.
        coordinator.Request();
        await errorReported.Task.WaitAsync(Timeout);

        Assert.Single(errors);
        Assert.Empty(applyCalls);

        // Second request — load succeeds.
        failLoad = false;
        coordinator.Request();
        await applied.Task.WaitAsync(Timeout);

        Assert.Single(applyCalls);
        Assert.Equal("recovered", applyCalls[0].Theme);
    }

    // -----------------------------------------------------------------------
    // Apply must always be invoked inside runOnUi, including a second request
    // that finishes loading while the first apply is still blocked.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Apply_is_always_invoked_inside_runOnUi_when_second_request_queues_behind_blocked_first()
    {
        var apply1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var apply1Gate    = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var apply2Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Interlocked increment/decrement gives full memory fences, so a read
        // inside Apply on any thread will see the current depth correctly.
        var uiDepth = 0;
        var apply2WasInsideUi = 0; // written before apply2Started fires; read after

        var callIndex = 0;
        Task<AppSettings> Load(CancellationToken _) =>
            Task.FromResult(MakeSettings($"theme-{Interlocked.Increment(ref callIndex)}"));

        var applyCount = 0;
        async Task Apply(AppSettings _)
        {
            var n = Interlocked.Increment(ref applyCount);
            if (n == 1)
            {
                apply1Started.TrySetResult();
                await apply1Gate.Task;
            }
            else
            {
                // Record affinity before signalling; apply2Started acts as the
                // happens-before fence for the read below.
                Volatile.Write(ref apply2WasInsideUi, Volatile.Read(ref uiDepth) > 0 ? 1 : 0);
                apply2Started.TrySetResult();
            }
        }

        void RunOnUi(Action action)
        {
            Interlocked.Increment(ref uiDepth);
            try { action(); }
            finally { Interlocked.Decrement(ref uiDepth); }
        }

        using var coordinator = new SettingsReloadCoordinator(Load, RunOnUi, Apply);

        // First request: load returns immediately; apply1 is invoked and blocks.
        coordinator.Request();
        await apply1Started.Task.WaitAsync(Timeout);

        // Second request: load returns immediately; apply2 must wait for the semaphore.
        coordinator.Request();
        // Let ProcessRequestAsync deliver its runOnUi call and park apply2 on the gate.
        await Task.Delay(100);

        // Unblock apply1; apply2 should then proceed.
        apply1Gate.SetResult();
        await apply2Started.Task.WaitAsync(Timeout);

        Assert.True(apply2WasInsideUi == 1,
            "second apply was invoked outside runOnUi (UI-thread affinity violated)");
    }
}
