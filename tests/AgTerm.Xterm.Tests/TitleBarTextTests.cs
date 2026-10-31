using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

// upstream WindowContentView titleText / windowTitle: the title bar shows "session — window" when the
// window has a custom name, each part gated by its interface element; the OS title is never gated
public sealed class TitleBarTextTests
{
    [Fact]
    public void An_auto_window_name_is_not_shown()
    {
        Assert.Null(TitleBarText.CustomWindowName("window 2"));
        Assert.Equal("dev", TitleBarText.CustomWindowName("dev"));
    }

    [Fact]
    public void Visible_title_joins_the_session_and_the_custom_window_name()
    {
        var settings = new AppSettings();

        Assert.Equal("shell — dev", TitleBarText.Visible("shell", "dev", settings));
        Assert.Equal("shell", TitleBarText.Visible("shell", null, settings));
    }

    [Fact]
    public void Each_part_hides_with_its_interface_element()
    {
        Assert.Equal("dev", TitleBarText.Visible("shell", "dev",
            new AppSettings { HiddenInterfaceElements = ["sessionName"] }));
        Assert.Equal("shell", TitleBarText.Visible("shell", "dev",
            new AppSettings { HiddenInterfaceElements = ["windowName"] }));
        Assert.Equal("", TitleBarText.Visible("shell", "dev",
            new AppSettings { HiddenInterfaceElements = ["sessionName", "windowName"] }));
    }

    [Fact]
    public void An_empty_session_title_counts_as_none()
    {
        Assert.Equal("dev", TitleBarText.Visible("", "dev", new AppSettings()));
        Assert.Equal("dev — agterm-win", TitleBarText.OsTitle("", "dev"));
    }

    [Fact]
    public void Os_title_always_carries_both_parts_and_the_product_name()
    {
        Assert.Equal("shell — dev — agterm-win", TitleBarText.OsTitle("shell", "dev"));
        Assert.Equal("shell — agterm-win", TitleBarText.OsTitle("shell", null));
        Assert.Equal("dev — agterm-win", TitleBarText.OsTitle(null, "dev"));
        Assert.Equal("agterm-win", TitleBarText.OsTitle(null, null));
    }

    // upstream dashboardWindowTitle: "Dashboard", plus "— <window name>" for a custom window name,
    // not gated by the interface elements
    [Fact]
    public void Dashboard_title_adds_only_a_custom_window_name()
    {
        Assert.Equal("Dashboard", TitleBarText.Dashboard(null));
        Assert.Equal("Dashboard — dev", TitleBarText.Dashboard("dev"));
    }
}
