using AgTerm.App;

namespace AgTerm.Xterm.Tests;

public sealed class TaskbarAppIconTests
{
    [Fact]
    public void A_light_taskbar_gets_the_dark_frame_icon()
    {
        Assert.Equal("AppIcon.ico", TaskbarAppIcon.ResourceFor(1));
    }

    [Fact]
    public void A_dark_taskbar_gets_the_light_frame_icon()
    {
        Assert.Equal("AppIcon-dark-taskbar.ico", TaskbarAppIcon.ResourceFor(0));
    }

    [Fact]
    public void A_missing_value_is_a_dark_taskbar()
    {
        Assert.Equal("AppIcon-dark-taskbar.ico", TaskbarAppIcon.ResourceFor(null));
    }

    [Fact]
    public void A_value_that_is_not_a_dword_is_a_dark_taskbar()
    {
        Assert.Equal("AppIcon-dark-taskbar.ico", TaskbarAppIcon.ResourceFor("1"));
    }
}
