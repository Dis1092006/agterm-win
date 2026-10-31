using AgTerm.App;
using AgTerm.Core.Keymap;

namespace AgTerm.Xterm.Tests;

// upstream AppActions.uiActionsEnabled at 7df70fe: terminal zoom and the open dashboard disable the
// window's UI actions; close session, zoom, full screen and font size stay enabled, the dashboard
// toggle is disabled only by zoom. The control API is never gated.
public sealed class ModalActionGateTests
{
    private static readonly BuiltinAction[] AlwaysAllowed =
    [
        BuiltinAction.CloseSession,
        BuiltinAction.ToggleTerminalZoom,
        BuiltinAction.ToggleFullscreen,
        BuiltinAction.IncreaseFontSize,
        BuiltinAction.DecreaseFontSize,
        BuiltinAction.ResetFontSize,
    ];

    public static IEnumerable<object[]> AllActions => BuiltinActions.All.Select(a => new object[] { a });

    [Theory]
    [MemberData(nameof(AllActions))]
    public void Every_action_is_allowed_outside_zoom_and_the_dashboard(BuiltinAction action)
    {
        Assert.True(ModalActionGate.Allows(action, zoomActive: false, dashboardOpen: false));
    }

    [Theory]
    [MemberData(nameof(AllActions))]
    public void Zoom_blocks_every_action_except_close_zoom_full_screen_and_font_size(BuiltinAction action)
    {
        Assert.Equal(AlwaysAllowed.Contains(action),
            ModalActionGate.Allows(action, zoomActive: true, dashboardOpen: false));
    }

    [Theory]
    [MemberData(nameof(AllActions))]
    public void The_dashboard_blocks_the_same_actions_but_keeps_its_own_toggle(BuiltinAction action)
    {
        Assert.Equal(AlwaysAllowed.Contains(action) || action == BuiltinAction.Dashboard,
            ModalActionGate.Allows(action, zoomActive: false, dashboardOpen: true));
    }

    [Theory]
    [InlineData(BuiltinAction.NewSession)]
    [InlineData(BuiltinAction.ToggleSidebar)]
    [InlineData(BuiltinAction.CommandPalette)]
    [InlineData(BuiltinAction.SessionPalette)]
    [InlineData(BuiltinAction.Dashboard)]
    [InlineData(BuiltinAction.SelectTheme)]
    [InlineData(BuiltinAction.NextSession)]
    public void A_chord_blocked_in_zoom_passes_to_the_terminal(BuiltinAction action)
    {
        Assert.True(ModalActionGate.PassesThroughInZoom(action));
    }

    [Theory]
    [InlineData(BuiltinAction.CloseSession)]
    [InlineData(BuiltinAction.ToggleTerminalZoom)]
    [InlineData(BuiltinAction.ToggleFullscreen)]
    [InlineData(BuiltinAction.IncreaseFontSize)]
    public void A_chord_still_enabled_in_zoom_does_not_pass_through(BuiltinAction action)
    {
        Assert.False(ModalActionGate.PassesThroughInZoom(action));
    }

    [Fact]
    public void Ui_actions_are_enabled_only_outside_zoom_and_the_dashboard()
    {
        Assert.True(ModalActionGate.UiActionsEnabled(zoomActive: false, dashboardOpen: false));
        Assert.False(ModalActionGate.UiActionsEnabled(zoomActive: true, dashboardOpen: false));
        Assert.False(ModalActionGate.UiActionsEnabled(zoomActive: false, dashboardOpen: true));
    }
}

// upstream WindowContentView: zoomTitlebar and dashboardTitlebar replace customTitlebar, and the zoom
// layer and the dashboard cover the sidebar. On Windows the title bar row stays for the caption buttons.
public sealed class ModalWindowChromeTests
{
    [Fact]
    public void The_layout_decides_the_mode()
    {
        Assert.Equal(ModalChromeMode.Zoom,
            ModalWindowChrome.ModeOf(new TerminalLayout.Zoomed(1, "pane")));
        Assert.Equal(ModalChromeMode.Dashboard, ModalWindowChrome.ModeOf(new TerminalLayout.Dashboard(
            1, [new TerminalDashboardCell("pane", "ref")], "ref")));
        Assert.Equal(ModalChromeMode.None, ModalWindowChrome.ModeOf(new TerminalLayout.Single(1, "pane", "shell")));
        Assert.Equal(ModalChromeMode.None, ModalWindowChrome.ModeOf(new TerminalLayout.Empty(1)));
    }

    [Fact]
    public void Outside_zoom_and_the_dashboard_the_title_bar_and_the_sidebar_are_unchanged()
    {
        var shown = ModalWindowChrome.Present(ModalChromeMode.None, sidebarToggleShown: true, sidebarVisible: true);
        Assert.Equal(new ModalChromePresentation(
            TitleBarActionsVisible: true, PaneToggleVisible: true, ZoomExitVisible: false,
            DashboardExitVisible: false, SidebarPaneOpen: true), shown);

        var hidden = ModalWindowChrome.Present(ModalChromeMode.None, sidebarToggleShown: false, sidebarVisible: false);
        Assert.False(hidden.PaneToggleVisible);
        Assert.False(hidden.SidebarPaneOpen);
        Assert.True(hidden.TitleBarActionsVisible);
    }

    [Fact]
    public void Zoom_keeps_only_the_zoom_exit_button_and_hides_the_sidebar()
    {
        Assert.Equal(new ModalChromePresentation(
                TitleBarActionsVisible: false, PaneToggleVisible: false, ZoomExitVisible: true,
                DashboardExitVisible: false, SidebarPaneOpen: false),
            ModalWindowChrome.Present(ModalChromeMode.Zoom, sidebarToggleShown: true, sidebarVisible: true));
    }

    [Fact]
    public void The_dashboard_keeps_only_the_dashboard_exit_button_and_hides_the_sidebar()
    {
        Assert.Equal(new ModalChromePresentation(
                TitleBarActionsVisible: false, PaneToggleVisible: false, ZoomExitVisible: false,
                DashboardExitVisible: true, SidebarPaneOpen: false),
            ModalWindowChrome.Present(ModalChromeMode.Dashboard, sidebarToggleShown: true, sidebarVisible: true));
    }
}
