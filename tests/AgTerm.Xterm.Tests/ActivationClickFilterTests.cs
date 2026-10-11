using AgTerm.App;

namespace AgTerm.Xterm.Tests;

public sealed class ActivationClickFilterTests
{
    [Fact]
    public void A_press_right_after_an_activation_is_the_activation_click()
    {
        var filter = new ActivationClickFilter();
        filter.Activated(nowMs: 1_000);
        filter.Pressed(nowMs: 1_005);

        Assert.True(filter.ShouldIgnoreTap());
    }

    [Fact]
    public void A_press_reported_before_the_activation_is_still_the_activation_click()
    {
        var filter = new ActivationClickFilter();
        filter.Pressed(nowMs: 1_000);
        filter.Activated(nowMs: 1_010);

        Assert.True(filter.ShouldIgnoreTap());
    }

    [Fact]
    public void Only_the_activation_click_is_ignored_and_the_next_tap_counts()
    {
        var filter = new ActivationClickFilter();
        filter.Activated(nowMs: 1_000);
        filter.Pressed(nowMs: 1_005);
        Assert.True(filter.ShouldIgnoreTap());

        filter.Pressed(nowMs: 1_200);

        Assert.False(filter.ShouldIgnoreTap());
    }

    [Fact]
    public void A_press_long_after_the_activation_counts()
    {
        var filter = new ActivationClickFilter();
        filter.Activated(nowMs: 1_000);
        filter.Pressed(nowMs: 3_000);

        Assert.False(filter.ShouldIgnoreTap());
    }

    [Fact]
    public void A_press_without_an_activation_counts()
    {
        var filter = new ActivationClickFilter();
        filter.Pressed(nowMs: 1_000);

        Assert.False(filter.ShouldIgnoreTap());
    }

    [Fact]
    public void A_tap_without_a_recorded_press_counts()
    {
        var filter = new ActivationClickFilter();
        filter.Activated(nowMs: 1_000);

        Assert.False(filter.ShouldIgnoreTap());
    }
}
