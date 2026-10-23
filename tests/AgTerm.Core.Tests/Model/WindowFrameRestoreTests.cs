using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public sealed class WindowFrameRestoreTests
{
    // a 100% primary display with a 48 px taskbar at the bottom, and a 150% display on its left
    private static readonly FrameDisplay Primary = new(
        new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1.0);
    private static readonly FrameDisplay Left = new(
        new PixelRect(-2560, 0, 2560, 1440), new PixelRect(-2560, 0, 2560, 1392), 1.5);
    private static readonly FrameDisplay[] Displays = [Primary, Left];

    [Fact]
    public void A_frame_inside_a_work_area_restores_unchanged()
    {
        var frame = new PixelRect(200, 100, 1000, 700);

        Assert.Equal(frame, WindowFrameRestore.Constrain(frame, Displays, primaryIndex: 0));
    }

    [Fact]
    public void A_frame_restores_on_the_display_holding_most_of_it()
    {
        var frame = new PixelRect(-2000, 300, 1200, 800);

        Assert.Equal(frame, WindowFrameRestore.Constrain(frame, Displays, primaryIndex: 0));
    }

    [Fact]
    public void A_frame_partly_off_its_display_moves_fully_inside_the_work_area()
    {
        // mostly on the primary display, its bottom under the taskbar and its right edge off the screen
        var frame = new PixelRect(1200, 600, 1000, 600);

        Assert.Equal(
            new PixelRect(920, 432, 1000, 600),
            WindowFrameRestore.Constrain(frame, Displays, primaryIndex: 0));
    }

    [Fact]
    public void A_frame_above_the_display_moves_down_so_its_title_bar_shows()
    {
        var frame = new PixelRect(300, -500, 800, 600);

        Assert.Equal(
            new PixelRect(300, 0, 800, 600),
            WindowFrameRestore.Constrain(frame, Displays, primaryIndex: 0));
    }

    [Fact]
    public void A_frame_larger_than_the_work_area_shrinks_to_it()
    {
        var frame = new PixelRect(-100, -100, 2400, 1400);

        Assert.Equal(
            new PixelRect(0, 0, 1920, 1032),
            WindowFrameRestore.Constrain(frame, Displays, primaryIndex: 0));
    }

    [Fact]
    public void A_frame_below_the_minimum_grows_to_it_in_the_display_scale()
    {
        var frame = new PixelRect(-2000, 300, 100, 100);

        Assert.Equal(
            new PixelRect(-2000, 300, 480, 360),
            WindowFrameRestore.Constrain(frame, Displays, primaryIndex: 0));
    }

    [Fact]
    public void A_frame_on_a_display_that_is_gone_restores_on_the_primary_display()
    {
        var frame = new PixelRect(4000, 200, 1000, 700);

        Assert.Equal(
            new PixelRect(920, 200, 1000, 700),
            WindowFrameRestore.Constrain(frame, Displays, primaryIndex: 0));
    }
}
