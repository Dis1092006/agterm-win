using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// The hover timer of spring-loaded expansion (upstream WorkspaceSidebar+DragDrop
/// scheduleSpringLoadedExpansion, cancelSpringLoadedExpansion and finishDraggingSequence): a drag that
/// rests on a collapsed workspace opens it after 0.65 s; leaving it, moving to another workspace or
/// cancelling the drag folds it back, and a successful drop keeps it open.
/// </summary>
public class SidebarSpringLoadTests
{
    private readonly List<(TimeSpan Delay, Action Fire, Ref Cancelled)> _scheduled = new();
    private readonly List<Guid> _opened = new();
    private readonly List<bool> _ended = new();
    private readonly SidebarSpringLoad _spring;
    private readonly Guid _w1 = Guid.NewGuid();
    private readonly Guid _w2 = Guid.NewGuid();

    private sealed class Ref
    {
        public bool Value;
    }

    public SidebarSpringLoadTests()
    {
        _spring = new SidebarSpringLoad(Schedule, _opened.Add, _ended.Add);
    }

    private Action Schedule(TimeSpan delay, Action fire)
    {
        var cancelled = new Ref();
        _scheduled.Add((delay, fire, cancelled));
        return () => cancelled.Value = true;
    }

    private void FireLast()
    {
        var last = _scheduled[^1];
        if (!last.Cancelled.Value) last.Fire();
    }

    [Fact]
    public void A_drag_resting_on_a_collapsed_workspace_opens_it_after_the_delay()
    {
        _spring.Hover(_w1, isOpen: false);

        Assert.Equal(TimeSpan.FromMilliseconds(650), Assert.Single(_scheduled).Delay);
        Assert.Empty(_opened);

        FireLast();

        Assert.Equal(new[] { _w1 }, _opened);
        Assert.Empty(_ended);
    }

    [Fact]
    public void Moving_over_the_same_workspace_keeps_the_timer_running()
    {
        _spring.Hover(_w1, isOpen: false);
        _spring.Hover(_w1, isOpen: false);

        Assert.Single(_scheduled);
        Assert.False(_scheduled[0].Cancelled.Value);
    }

    [Fact]
    public void Moving_to_another_workspace_restarts_the_timer_for_it()
    {
        _spring.Hover(_w1, isOpen: false);
        _spring.Hover(_w2, isOpen: false);

        Assert.True(_scheduled[0].Cancelled.Value);
        Assert.Equal(2, _scheduled.Count);
        FireLast();
        Assert.Equal(new[] { _w2 }, _opened);
    }

    [Fact]
    public void Moving_to_another_workspace_folds_the_one_the_drag_opened()
    {
        _spring.Hover(_w1, isOpen: false);
        FireLast();

        _spring.Hover(_w2, isOpen: false);

        Assert.Equal(new[] { false }, _ended);
        Assert.Equal(2, _scheduled.Count);
    }

    [Fact]
    public void Moving_inside_the_workspace_the_drag_opened_keeps_it_open()
    {
        _spring.Hover(_w1, isOpen: false);
        FireLast();

        _spring.Hover(_w1, isOpen: false); // the rows may not show the open workspace yet
        _spring.Hover(_w1, isOpen: true);

        Assert.Empty(_ended);
        Assert.Single(_scheduled);
    }

    [Fact]
    public void An_open_workspace_needs_no_timer_and_folds_the_one_the_drag_opened()
    {
        _spring.Hover(_w1, isOpen: false);
        FireLast();

        _spring.Hover(_w2, isOpen: true);

        Assert.Equal(new[] { false }, _ended);
        Assert.Single(_scheduled);
    }

    [Fact]
    public void An_open_workspace_cancels_a_pending_timer()
    {
        _spring.Hover(_w1, isOpen: false);

        _spring.Hover(_w2, isOpen: true);

        Assert.True(_scheduled[0].Cancelled.Value);
        Assert.Empty(_ended);
    }

    [Fact]
    public void No_target_cancels_the_timer_and_folds_the_opened_workspace()
    {
        _spring.Hover(_w1, isOpen: false);
        FireLast();
        _spring.Hover(_w2, isOpen: false);

        _spring.Hover(null, isOpen: false);

        Assert.True(_scheduled[^1].Cancelled.Value);
        Assert.Equal(new[] { false }, _ended);
    }

    [Fact]
    public void A_successful_drop_keeps_the_opened_workspace_open()
    {
        _spring.Hover(_w1, isOpen: false);
        FireLast();

        _spring.Finish(keepOpen: true);
        _spring.Cancel();

        Assert.Equal(new[] { true }, _ended);
    }

    [Fact]
    public void A_cancelled_drag_folds_the_opened_workspace_once()
    {
        _spring.Hover(_w1, isOpen: false);
        FireLast();

        _spring.Cancel();
        _spring.Finish(keepOpen: false);

        Assert.Equal(new[] { false }, _ended);
    }

    [Fact]
    public void Ending_the_drag_before_the_delay_stops_the_timer_and_folds_nothing()
    {
        _spring.Hover(_w1, isOpen: false);

        _spring.Finish(keepOpen: true);

        Assert.True(_scheduled[0].Cancelled.Value);
        Assert.Empty(_opened);
        Assert.Empty(_ended);
    }

    [Fact]
    public void A_new_drag_over_the_same_workspace_starts_a_new_timer()
    {
        _spring.Hover(_w1, isOpen: false);
        _spring.Cancel();

        _spring.Hover(_w1, isOpen: false);

        Assert.Equal(2, _scheduled.Count);
        Assert.False(_scheduled[1].Cancelled.Value);
    }
}
