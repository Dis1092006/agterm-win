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

    // upstream TitlebarComposition: the context replaces the cwd detail on line two of a normal bar and
    // trails the identity after " · " on a compact one; the sessionContext element gates only itself
    [Fact]
    public void Normal_subtitle_shows_the_context_instead_of_the_detail()
    {
        var normal = new AppSettings { ToolbarMode = "normal" };

        Assert.Equal("PR #517", TitleBarText.Subtitle(@"C:\main", "PR #517", normal));
        Assert.Equal(@"C:\main", TitleBarText.Subtitle(@"C:\main", null, normal));
        Assert.Equal("PR #517", TitleBarText.Subtitle(null, "PR #517", normal));
    }

    [Fact]
    public void Hidden_context_element_gives_the_detail_back()
    {
        var settings = new AppSettings { ToolbarMode = "normal", HiddenInterfaceElements = ["sessionContext"] };

        Assert.Equal(@"C:\main", TitleBarText.Subtitle(@"C:\main", "PR #517", settings));
    }

    [Fact]
    public void Compact_subtitle_stays_empty_with_a_context()
    {
        Assert.Equal("", TitleBarText.Subtitle(@"C:\main", "PR #517", new AppSettings { ToolbarMode = "compact" }));
    }

    [Fact]
    public void Compact_title_appends_the_context_after_the_identity()
    {
        var compact = new AppSettings { ToolbarMode = "compact" };

        Assert.Equal("shell — dev · PR #517", TitleBarText.Visible("shell", "dev", "PR #517", compact));
        Assert.Equal("shell · PR #517", TitleBarText.Visible("shell", null, "PR #517", compact));
        Assert.Equal("shell", TitleBarText.Visible("shell", null, null, compact));
    }

    [Fact]
    public void Compact_title_is_just_the_context_when_the_identity_is_empty()
    {
        var settings = new AppSettings { ToolbarMode = "compact", HiddenInterfaceElements = ["sessionName"] };

        Assert.Equal("PR #517", TitleBarText.Visible("shell", null, "PR #517", settings));
    }

    [Fact]
    public void Normal_title_does_not_carry_the_context()
    {
        Assert.Equal("shell", TitleBarText.Visible("shell", null, "PR #517", new AppSettings { ToolbarMode = "normal" }));
    }

    [Fact]
    public void Hidden_context_element_leaves_the_compact_title_alone()
    {
        var settings = new AppSettings { ToolbarMode = "compact", HiddenInterfaceElements = ["sessionContext"] };

        Assert.Equal("shell", TitleBarText.Visible("shell", null, "PR #517", settings));
    }

    [Fact]
    public void Os_and_dashboard_titles_never_carry_the_context()
    {
        Assert.Equal("shell — agterm-win", TitleBarText.OsTitle("shell", null));
        Assert.Equal("Dashboard", TitleBarText.Dashboard(null));
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
