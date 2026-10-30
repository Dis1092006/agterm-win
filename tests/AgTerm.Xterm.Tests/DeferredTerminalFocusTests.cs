using AgTerm.App;

namespace AgTerm.Xterm.Tests;

public sealed class DeferredTerminalFocusTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_pane_change_during_a_modal_waits_for_both_release_and_activation(bool active)
    {
        var focus = new DeferredTerminalFocus();
        var focused = 0;
        focus.Request(active, () => focused++, modalOpen: true);
        Assert.True(focus.Pending);
        Assert.Equal(0, focused);
        focus.Activated(modalOpen: true, () => focused++);
        Assert.True(focus.Pending);
        Assert.Equal(0, focused);
        focus.Activated(modalOpen: false, () => focused++);
        Assert.False(focus.Pending);
        Assert.Equal(1, focused);
        focus.Activated(modalOpen: false, () => focused++);
        Assert.Equal(1, focused);
    }

    [Fact]
    public void An_active_window_focuses_at_once_and_drops_a_pending_focus()
    {
        var focus = new DeferredTerminalFocus();
        focus.Request(windowIsActive: false, () => { });
        var focused = 0;

        focus.Request(windowIsActive: true, () => focused++);

        Assert.Equal(1, focused);
        Assert.False(focus.Pending);
    }

    [Fact]
    public void A_background_window_waits_for_the_activation()
    {
        var focus = new DeferredTerminalFocus();
        var focused = 0;

        focus.Request(windowIsActive: false, () => focused++);

        Assert.Equal(0, focused);
        Assert.True(focus.Pending);
    }

    [Fact]
    public void The_activation_runs_a_pending_focus_once()
    {
        var focus = new DeferredTerminalFocus();
        focus.Request(windowIsActive: false, () => { });
        var restored = 0;

        focus.Activated(modalOpen: false, () => restored++);
        focus.Activated(modalOpen: false, () => restored++);

        Assert.Equal(1, restored);
        Assert.False(focus.Pending);
    }

    [Fact]
    public void An_activation_without_a_pending_focus_leaves_focus_alone()
    {
        var focus = new DeferredTerminalFocus();
        var restored = 0;

        focus.Activated(modalOpen: false, () => restored++);

        Assert.Equal(0, restored);
    }

    [Fact]
    public void An_open_modal_keeps_the_activation_focus()
    {
        var focus = new DeferredTerminalFocus();
        focus.Request(windowIsActive: false, () => { });
        var restored = 0;

        focus.Activated(modalOpen: true, () => restored++);

        Assert.Equal(0, restored);
        Assert.True(focus.Pending);
    }

    // a palette open at activation closes later and focuses the terminal itself; the pending focus
    // must not then take focus from a rename box or a dialog on the next activation
    [Fact]
    public void A_focus_done_after_an_activation_kept_by_a_modal_satisfies_the_pending_one()
    {
        var focus = new DeferredTerminalFocus();
        focus.Request(windowIsActive: false, () => { });
        focus.Activated(modalOpen: true, () => { });
        focus.Request(windowIsActive: true, () => { });
        var restored = 0;

        focus.Activated(modalOpen: false, () => restored++);

        Assert.Equal(0, restored);
    }

    [Fact]
    public void A_modal_shown_in_the_background_cancels_the_pending_focus()
    {
        var focus = new DeferredTerminalFocus();
        focus.Request(windowIsActive: false, () => { });
        var restored = 0;

        focus.Cancel();
        focus.Activated(modalOpen: false, () => restored++);

        Assert.Equal(0, restored);
    }
}
