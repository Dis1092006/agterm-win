namespace AgTerm.Xterm.Tests;

// Terminal zoom and the dashboard strip the title bar to the title and one exit button, hide the
// sidebar and disable the window's UI actions (upstream WindowContentView zoomTitlebar /
// dashboardTitlebar and AppActions.uiActionsEnabled at 7df70fe). The WinUI types are not compiled by
// the test project, so the wiring is pinned in source.
public sealed class ModalChromeSourceTests
{
    [Fact]
    public void The_title_bar_actions_sit_in_one_panel_next_to_the_two_exit_buttons()
    {
        var xaml = Source("MainWindow.xaml");

        Assert.Contains("<StackPanel x:Name=\"TitleBarActions\"", xaml, StringComparison.Ordinal);
        var actions = Element(xaml, "<StackPanel x:Name=\"TitleBarActions\"", "</StackPanel>\n                    <Button x:Name=\"TerminalZoomExitButton\"");
        Assert.Contains("settings-button", actions, StringComparison.Ordinal);
        Assert.Contains("help-button", actions, StringComparison.Ordinal);
        Assert.Contains("recent-sessions-button", actions, StringComparison.Ordinal);
    }

    [Fact]
    public void The_zoom_exit_button_has_the_upstream_id_and_the_back_to_window_glyph()
    {
        var button = Element(Source("MainWindow.xaml"), "<Button x:Name=\"TerminalZoomExitButton\"", "</Button>");

        Assert.Contains("Visibility=\"Collapsed\"", button, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnTerminalZoomExitClick\"", button, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Exit Terminal Zoom\"", button, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"terminal-zoom-exit\"", button, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE73F;\"", button, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dashboard_exit_button_has_the_upstream_id_tooltip_and_the_same_glyph()
    {
        var button = Element(Source("MainWindow.xaml"), "<Button x:Name=\"DashboardExitButton\"", "</Button>");

        Assert.Contains("Visibility=\"Collapsed\"", button, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnDashboardExitClick\"", button, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Exit Dashboard\"", button, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"dashboard-exit\"", button, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ToolTip=\"Exit Dashboard (Esc)\"", button, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE73F;\"", button, StringComparison.Ordinal);
    }

    [Fact]
    public void The_exit_buttons_run_the_zoom_exit_and_the_same_close_as_esc()
    {
        var code = Source("MainWindow.xaml.cs");

        Assert.Contains("host.ExitTerminalZoomAsync()",
            MethodBody(code, "private void OnTerminalZoomExitClick("), StringComparison.Ordinal);
        Assert.Contains("OnPageDashboardClosed();",
            MethodBody(code, "private void OnDashboardExitClick("), StringComparison.Ordinal);
        Assert.Contains("WindowsShortcutText.Hint(\"Exit Terminal Zoom\", _keymap, BuiltinAction.ToggleTerminalZoom)",
            code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_accepted_layout_sets_the_mode_and_applies_the_chrome()
    {
        var code = Source("MainWindow.xaml.cs");

        Assert.Contains("_modalChrome = ModalWindowChrome.ModeOf(layout);", code, StringComparison.Ordinal);
        var apply = MethodBody(code, "private void ApplyModalChrome()");
        Assert.Contains("ModalWindowChrome.Present(", apply, StringComparison.Ordinal);
        Assert.Contains("AppTitleBar.IsPaneToggleButtonVisible = chrome.PaneToggleVisible;", apply, StringComparison.Ordinal);
        Assert.Contains("TitleBarActions.Visibility = VisibleIf(chrome.TitleBarActionsVisible);", apply, StringComparison.Ordinal);
        Assert.Contains("TerminalZoomExitButton.Visibility = VisibleIf(chrome.ZoomExitVisible);", apply, StringComparison.Ordinal);
        Assert.Contains("DashboardExitButton.Visibility = VisibleIf(chrome.DashboardExitVisible);", apply, StringComparison.Ordinal);
        Assert.Contains("ShellSplitView.IsPaneOpen = chrome.SidebarPaneOpen;", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sidebar_snapshot_and_the_interface_elements_go_through_the_modal_chrome()
    {
        var code = Source("MainWindow.xaml.cs");

        var sidebar = MethodBody(code, "private void ApplySidebarChrome(SidebarSnapshot snapshot)");
        Assert.Contains("_sidebarVisible = snapshot.SidebarVisible;", sidebar, StringComparison.Ordinal);
        Assert.Contains("ApplyModalChrome();", sidebar, StringComparison.Ordinal);
        Assert.DoesNotContain("IsPaneOpen", sidebar, StringComparison.Ordinal);

        var elements = MethodBody(code, "private void ApplyInterfaceElements(AppSettings settings)");
        Assert.Contains("ApplyModalChrome();", elements, StringComparison.Ordinal);
        Assert.DoesNotContain("IsPaneToggleButtonVisible", elements, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dashboard_title_carries_the_custom_window_name()
    {
        var titles = MethodBody(Source("MainWindow.xaml.cs"), "private void RefreshTitles(AppSettings settings)");

        Assert.Contains("TitleBarText.Dashboard(_customWindowName)", titles, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Dashboard\"", titles, StringComparison.Ordinal);
    }

    [Fact]
    public void The_command_router_is_gated_by_zoom_and_the_dashboard()
    {
        var code = Source("MainWindow.xaml.cs");

        Assert.Contains("action => ModalActionGate.Allows(action, host.ZoomActive, _selection.DashboardOpen)",
            code, StringComparison.Ordinal);
        Assert.DoesNotContain("() => !host.ZoomActive", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Chords_blocked_in_zoom_pass_to_the_terminal()
    {
        var code = Source("MainWindow.xaml.cs");

        Assert.Contains("ModalActionGate.PassesThroughInZoom(action)",
            MethodBody(code, "private async Task PublishHotkeyManifestAsync("), StringComparison.Ordinal);
        Assert.DoesNotContain("IsPaletteAction", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_palette_and_the_switcher_follow_ui_actions_enabled()
    {
        var code = Source("MainWindow.xaml.cs");

        Assert.Contains("ModalActionGate.UiActionsEnabled(_host?.ZoomActive == true, _selection.DashboardOpen)",
            MethodBody(code, "private bool UiActionsEnabled()"), StringComparison.Ordinal);
        Assert.Contains("UiActionsEnabled()", MethodBody(code, "private bool CanSwitchSessions()"),
            StringComparison.Ordinal);
        Assert.Contains("uiActionsEnabled: UiActionsEnabled()", code, StringComparison.Ordinal);
    }

    private static string Element(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"{start} not found");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to >= 0, $"{end} not found after {start}");
        return source[from..to];
    }

    // from the signature to the first line that closes a member at class indentation
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        return source[start..end];
    }

    private static string Source(string appFile) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", appFile)).Replace("\r\n", "\n");

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("AgTerm.slnx not found above the test output");
    }
}
