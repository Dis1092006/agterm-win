using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public sealed class WindowFrameTrackerTests
{
    private static readonly PixelRect Normal = new(100, 80, 1000, 700);
    private static readonly PixelRect Moved = new(300, 200, 900, 600);
    private static readonly PixelRect Screen = new(0, 0, 1920, 1080);
    private static readonly PixelRect Parked = new(-32000, -32000, 160, 28);

    [Fact]
    public void Nothing_observed_saves_no_frame()
    {
        Assert.Null(new WindowFrameTracker().Frame);
    }

    [Fact]
    public void A_normal_window_saves_its_last_bounds()
    {
        var tracker = new WindowFrameTracker();

        tracker.Observe(WindowShowState.Normal, Normal);
        tracker.Observe(WindowShowState.Normal, Moved);

        Assert.Equal(new SavedWindowFrame(Moved, Maximized: false), tracker.Frame);
    }

    [Fact]
    public void A_maximized_window_keeps_the_bounds_it_had_before()
    {
        var tracker = new WindowFrameTracker();

        tracker.Observe(WindowShowState.Normal, Normal);
        tracker.Observe(WindowShowState.Maximized, Screen);

        Assert.Equal(new SavedWindowFrame(Normal, Maximized: true), tracker.Frame);
    }

    [Fact]
    public void A_restore_after_maximize_clears_the_maximized_flag()
    {
        var tracker = new WindowFrameTracker();

        tracker.Observe(WindowShowState.Normal, Normal);
        tracker.Observe(WindowShowState.Maximized, Screen);
        tracker.Observe(WindowShowState.Normal, Moved);

        Assert.Equal(new SavedWindowFrame(Moved, Maximized: false), tracker.Frame);
    }

    [Theory]
    [InlineData(WindowShowState.Minimized)]
    [InlineData(WindowShowState.FullScreen)]
    public void Minimized_and_full_screen_leave_the_last_frame(WindowShowState state)
    {
        var tracker = new WindowFrameTracker();

        tracker.Observe(WindowShowState.Normal, Normal);
        tracker.Observe(WindowShowState.Maximized, Screen);
        tracker.Observe(state, state == WindowShowState.Minimized ? Parked : Screen);

        Assert.Equal(new SavedWindowFrame(Normal, Maximized: true), tracker.Frame);
    }

    // AppWindow reports the full screen size while the presenter is still the overlapped one, before
    // the presenter change arrives
    [Fact]
    public void Entering_full_screen_ignores_the_screen_bounds_reported_as_normal()
    {
        var tracker = new WindowFrameTracker();
        tracker.Observe(WindowShowState.Normal, Normal);

        tracker.EnteringFullScreen();
        tracker.Observe(WindowShowState.Normal, Screen);
        tracker.Observe(WindowShowState.FullScreen, Screen);

        Assert.Equal(new SavedWindowFrame(Normal, Maximized: false), tracker.Frame);
    }

    [Fact]
    public void Entering_full_screen_from_maximized_keeps_the_maximized_frame()
    {
        var tracker = new WindowFrameTracker();
        tracker.Observe(WindowShowState.Normal, Normal);
        tracker.Observe(WindowShowState.Maximized, Screen);

        tracker.EnteringFullScreen();
        tracker.Observe(WindowShowState.Normal, Screen);
        tracker.Observe(WindowShowState.FullScreen, Screen);

        Assert.Equal(new SavedWindowFrame(Normal, Maximized: true), tracker.Frame);
    }

    [Fact]
    public void Leaving_full_screen_follows_the_window_again()
    {
        var tracker = new WindowFrameTracker();
        tracker.Observe(WindowShowState.Normal, Normal);
        tracker.EnteringFullScreen();
        tracker.Observe(WindowShowState.Normal, Screen);
        tracker.Observe(WindowShowState.FullScreen, Screen);

        tracker.Observe(WindowShowState.Normal, Normal);
        tracker.Observe(WindowShowState.Normal, Moved);

        Assert.Equal(new SavedWindowFrame(Moved, Maximized: false), tracker.Frame);
    }

    [Fact]
    public void A_full_screen_that_never_started_follows_the_window_again()
    {
        var tracker = new WindowFrameTracker();
        tracker.Observe(WindowShowState.Normal, Normal);

        tracker.EnteringFullScreen();
        tracker.FullScreenCancelled();
        tracker.Observe(WindowShowState.Normal, Moved);

        Assert.Equal(new SavedWindowFrame(Moved, Maximized: false), tracker.Frame);
    }

    [Fact]
    public void A_window_first_seen_maximized_saves_no_frame_until_it_has_normal_bounds()
    {
        var tracker = new WindowFrameTracker();

        tracker.Observe(WindowShowState.Maximized, Screen);
        Assert.Null(tracker.Frame);

        tracker.Observe(WindowShowState.Normal, Normal);
        Assert.Equal(new SavedWindowFrame(Normal, Maximized: false), tracker.Frame);
    }
}
