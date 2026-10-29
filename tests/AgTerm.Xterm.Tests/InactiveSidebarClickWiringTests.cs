namespace AgTerm.Xterm.Tests;

public sealed class InactiveSidebarClickWiringTests
{
    [Fact]
    public void Application_startup_applies_sidebar_policy_without_waiting_for_a_thread_pool_work_item()
    {
        var source = Source("LiveApplicationHost.cs", "AgTerm.Xterm");
        var start = source.IndexOf("public LiveApplicationHost(", StringComparison.Ordinal);
        var end = source.IndexOf("private Task ApplyApplicationSettingsAsync(", start, StringComparison.Ordinal);
        var constructor = source[start..end];
        Assert.DoesNotContain("GetAwaiter().GetResult()", constructor);
        Assert.Contains("_autoHideInactiveSidebars = Settings.State.Snapshot.Settings.AutoHideSidebarInactiveWindows == true;", constructor);
    }

    [Fact]
    public void Inactive_terminal_wheel_scrolls_history_without_forwarding_pointer_input()
    {
        Assert.Contains("PointerWheelChanged=\"OnActivationShieldWheelChanged\"", Source("MainWindow.xaml"));
        var source = Source("MainWindow.xaml.cs");
        var start = source.IndexOf("private void OnActivationShieldWheelChanged(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("private void ", start + 1, StringComparison.Ordinal);
        var handler = source[start..end];
        Assert.Contains("e.Handled = true;", handler);
        Assert.Contains("GetCurrentPoint(TerminalView)", handler);
        Assert.Contains("IsHorizontalMouseWheel", handler);
        Assert.Contains("ScrollInactiveAtAsync(", handler);
        Assert.DoesNotContain("Focus", handler);
    }

    [Fact]
    public void Terminal_activation_click_is_eaten_before_the_sidebar_can_resize_the_surface()
    {
        var source = Source("MainWindow.xaml.cs");
        Assert.Contains("RefreshActivationShield();", source);
        Assert.Contains("_settings.AutoHideSidebarInactiveWindows == true", source);
        Assert.Contains("_activationClickReleaseTimer.Stop();", source);
        var xaml = Source("MainWindow.xaml");
        Assert.Contains("x:Name=\"ActivationShield\"", xaml);
        Assert.Contains("PointerPressed=\"OnActivationShieldPressed\"", xaml);
        Assert.Contains("PointerReleased=\"OnActivationShieldReleased\"", xaml);
        Assert.Contains("TerminalContent.Children[0] = replacement;", source);
    }

    [Fact]
    public void Deactivating_to_another_app_does_not_reconcile_sidebars()
    {
        var source = Source("MainWindow.xaml.cs");
        var start = source.IndexOf("private void OnWindowActivated(", StringComparison.Ordinal);
        var end = source.IndexOf("private void OnAppWindowClosing(", start, StringComparison.Ordinal);
        var handler = source[start..end].Replace("\r\n", "\n");
        var guard = handler.IndexOf("WindowActivationState.Deactivated)\n            return;", StringComparison.Ordinal);
        Assert.True(guard >= 0);
        Assert.True(guard < handler.IndexOf("_applicationHost.ReportActivatedAsync(", StringComparison.Ordinal));
    }

    private static string Source(string name, string project = "AgTerm.App")
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
                return File.ReadAllText(Path.Combine(directory.FullName, "src", project, name));
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
