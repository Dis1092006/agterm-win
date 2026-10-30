namespace AgTerm.Xterm.Tests;

public sealed class TerminalNotificationAppSourceTests
{
    [Fact]
    public void One_app_presenter_handles_running_redirected_and_cold_start_activations()
    {
        var app = Source("App.xaml.cs");
        var window = Source("MainWindow.xaml.cs");
        var presenter = Source("WindowsAppNotificationPresenter.cs");
        Assert.Contains("new WindowsAppNotificationPresenter(", app, StringComparison.Ordinal);
        Assert.DoesNotContain("new WindowsAppNotificationPresenter(", window, StringComparison.Ordinal);
        Assert.DoesNotContain("_notificationPresenter.Dispose()", window, StringComparison.Ordinal);
        Assert.Contains("NotificationInvoked += OnNotificationInvoked", presenter, StringComparison.Ordinal);
        Assert.Contains("NotificationInvoked -= OnNotificationInvoked", presenter, StringComparison.Ordinal);
        Assert.Contains("AppInstance.GetCurrent().GetActivatedEventArgs()", app, StringComparison.Ordinal);
        Assert.Contains("await _launchReady.Task", app, StringComparison.Ordinal);
        Assert.Contains("RevealNotificationAsync(target)", app, StringComparison.Ordinal);
        Assert.Contains("HandleActivation(activation)", Source("Program.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Fallback_banner_has_the_same_target_and_clears_when_its_session_is_focused()
    {
        var source = Source("MainWindow.xaml.cs");
        Assert.Contains("NotificationBanner.ActionButton", source, StringComparison.Ordinal);
        Assert.Contains("ClearNotificationSessionAsync(sessionId)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Toasts_replace_by_pane_and_clear_by_session_with_no_content_in_activation_arguments()
    {
        var source = Source("WindowsAppNotificationPresenter.cs");
        Assert.Contains("notification.Target", source, StringComparison.Ordinal);
        Assert.Contains(".ToastTag", source, StringComparison.Ordinal);
        Assert.Contains(".ToastGroup", source, StringComparison.Ordinal);
        Assert.Contains(".Identity", source, StringComparison.Ordinal);
        Assert.Contains("RemoveByGroupAsync", source, StringComparison.Ordinal);
        Assert.Contains("SessionNotificationsCleared", Source("MainWindow.xaml.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain(".AddArgument(\"body\"", source, StringComparison.Ordinal);
        Assert.Contains("_deliveryGate.WaitAsync()", source, StringComparison.Ordinal);
        Assert.Contains("Task<bool> TryShowAsync(", source, StringComparison.Ordinal);
    }

    private static string Source(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "AgTerm.App", file));
    }
}
