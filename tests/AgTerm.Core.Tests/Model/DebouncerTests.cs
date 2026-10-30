using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public sealed class DebouncerTests
{
    [Fact]
    public void Cancelled_timer_cannot_take_a_replacement_callback()
    {
        var clock = new ManualClock();
        var debouncer = new Debouncer(clock);
        var calls = new List<int>();
        debouncer.Schedule(5, () => calls.Add(1));
        debouncer.Schedule(10, () => calls.Add(2));
        clock.Timers[0].Fire();
        Assert.Empty(calls);
        clock.Timers[1].Fire();
        clock.Timers[1].Fire();
        Assert.Equal([2], calls);
    }

    [Fact]
    public void Cancel_and_flush_invalidate_already_queued_timer_callbacks()
    {
        var clock = new ManualClock();
        var debouncer = new Debouncer(clock);
        var calls = 0;
        debouncer.Schedule(5, () => calls++);
        debouncer.Cancel();
        clock.Timers[0].Fire();
        Assert.Equal(0, calls);
        debouncer.Schedule(5, () => calls++);
        debouncer.Flush();
        clock.Timers[1].Fire();
        Assert.Equal(1, calls);
    }

    private sealed class ManualClock : TimeProvider
    {
        public List<ManualTimer> Timers { get; } = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(() => callback(state));
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class ManualTimer(Action callback) : ITimer
    {
        public void Fire() => callback();
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
