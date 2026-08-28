namespace AgTerm.Xterm.Tests;

public sealed class TerminalLayoutCoordinatorTests
{
    [Fact]
    public async Task Gen2_submitted_while_gen1_in_flight_becomes_final_applied_layout()
    {
        var gen1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gen1Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new List<TerminalLayout>();

        Task<bool> Sink(TerminalLayout layout)
        {
            if (layout.Generation == 1L)
            {
                gen1Started.TrySetResult();
                return gen1Gate.Task.ContinueWith(_ =>
                {
                    applied.Add(layout);
                    return true;
                }, TaskScheduler.Default);
            }
            applied.Add(layout);
            return Task.FromResult(true);
        }

        using var coordinator = new TerminalLayoutCoordinator(Sink);

        var layout1 = new TerminalLayout.Single(1L, "pane-1", "shell");
        coordinator.Submit(layout1);
        await gen1Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var layout2 = new TerminalLayout.Single(2L, "pane-1", "shell");
        coordinator.Submit(layout2);

        gen1Gate.SetResult();
        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotEmpty(applied);
        Assert.Equal(layout2, applied[^1]);
        Assert.NotEqual(layout1, applied[^1]);
    }

    [Fact]
    public async Task LayoutAccepted_event_fires_only_when_sink_returns_true()
    {
        var accepted = new List<TerminalLayout>();

        Task<bool> Sink(TerminalLayout layout) =>
            Task.FromResult(layout.Generation == 1L);

        using var coordinator = new TerminalLayoutCoordinator(Sink);
        coordinator.LayoutAccepted += (_, layout) => accepted.Add(layout);

        coordinator.Submit(new TerminalLayout.Single(1L, "pane-a", "shell"));
        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        coordinator.Submit(new TerminalLayout.Single(2L, "pane-b", "shell"));
        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(accepted);
        Assert.Equal(1L, accepted[0].Generation);
    }

    [Fact]
    public async Task Stale_failure_followed_by_successful_gen2_completes_idle_without_exception()
    {
        var gen1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gen1Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new List<TerminalLayout>();

        Task<bool> Sink(TerminalLayout layout)
        {
            if (layout.Generation == 1L)
            {
                gen1Started.TrySetResult();
                async Task<bool> FailAfterGate()
                {
                    await gen1Gate.Task;
                    throw new InvalidOperationException("gen1 intentional failure");
                }
                return FailAfterGate();
            }
            return Task.FromResult(true);
        }

        using var coordinator = new TerminalLayoutCoordinator(Sink);
        coordinator.LayoutAccepted += (_, layout) => accepted.Add(layout);

        coordinator.Submit(new TerminalLayout.Single(1L, "pane-1", "shell"));
        await gen1Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        coordinator.Submit(new TerminalLayout.Single(2L, "pane-1", "shell"));

        gen1Gate.SetResult();
        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(accepted);
        Assert.Equal(2L, accepted[0].Generation);
    }

    [Fact]
    public async Task Dispose_while_gen1_in_flight_and_gen2_pending_applies_gen1_only_and_accepts_neither()
    {
        var gen1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gen1Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sinkCalled = new List<long>();
        var accepted = new List<TerminalLayout>();

        Task<bool> Sink(TerminalLayout layout)
        {
            if (layout.Generation == 1L)
            {
                gen1Started.TrySetResult();
                return gen1Gate.Task.ContinueWith(_ =>
                {
                    sinkCalled.Add(layout.Generation);
                    return true;
                }, TaskScheduler.Default);
            }
            sinkCalled.Add(layout.Generation);
            return Task.FromResult(true);
        }

        var coordinator = new TerminalLayoutCoordinator(Sink);
        coordinator.LayoutAccepted += (_, layout) => accepted.Add(layout);

        coordinator.Submit(new TerminalLayout.Single(1L, "pane-1", "shell"));
        await gen1Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        coordinator.Submit(new TerminalLayout.Single(2L, "pane-1", "shell"));
        coordinator.Dispose();

        gen1Gate.SetResult();
        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { 1L }, sinkCalled);
        Assert.Empty(accepted);
    }

    [Fact]
    public async Task Gen20_is_final_accepted_after_20_rapid_submissions_while_gen1_delayed()
    {
        var gen1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gen1Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new List<TerminalLayout>();
        var applied = new List<TerminalLayout>();

        Task<bool> Sink(TerminalLayout layout)
        {
            applied.Add(layout);
            if (layout.Generation == 1L)
            {
                gen1Started.TrySetResult();
                return gen1Gate.Task.ContinueWith(_ => true, TaskScheduler.Default);
            }
            return Task.FromResult(true);
        }

        using var coordinator = new TerminalLayoutCoordinator(Sink);
        coordinator.LayoutAccepted += (_, layout) => accepted.Add(layout);

        coordinator.Submit(new TerminalLayout.Single(1L, "pane-1", "shell"));
        await gen1Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        for (long g = 2; g <= 20; g++)
        {
            coordinator.Submit(new TerminalLayout.Single(g, "pane-1", $"title-{g}"));
        }

        gen1Gate.SetResult();
        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(20L, applied[^1].Generation);
        Assert.Equal(20L, accepted[^1].Generation);

        var intermediateAccepted = accepted.Where(l => l.Generation >= 2 && l.Generation < 20).ToList();
        Assert.Empty(intermediateAccepted);
    }

    [Fact]
    public async Task LayoutAccepted_carries_the_workspace_name_of_the_submitted_layout()
    {
        var accepted = new List<TerminalLayout>();

        Task<bool> Sink(TerminalLayout layout)
        {
            accepted.Add(layout);
            return Task.FromResult(true);
        }

        using var coordinator = new TerminalLayoutCoordinator(Sink);
        coordinator.LayoutAccepted += (_, layout) => { };

        coordinator.Submit(new TerminalLayout.Single(1L, "pane", "session", "workspace"));
        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        var single = Assert.IsType<TerminalLayout.Single>(accepted.Single());
        Assert.Equal("workspace", single.WorkspaceName);
    }
}
