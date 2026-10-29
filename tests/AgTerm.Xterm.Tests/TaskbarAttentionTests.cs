using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm.Tests;

public sealed class TaskbarAttentionTests
{
    [Theory]
    [InlineData("once")]
    [InlineData("untilFocused")]
    public void Explicit_notify_queues_attention_even_with_banners_disabled(string mode)
    {
        var directory = Directory.CreateTempSubdirectory("agterm-taskbar-notify-").FullName;
        Action? scheduled = null;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = directory,
                ConfigDir = directory,
                PipeName = "agterm-taskbar-" + Guid.NewGuid().ToString("N"),
                NotificationSettings = new AppSettings { DockBounce = mode, NotificationsEnabled = false },
                OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
                NotificationScheduler = action => { scheduled = action; return completion.Task; },
            });
            var delivered = 0;
            host.AttentionRequested += () => delivered++;
            host.NotificationRequested += _ => Assert.Fail("Disabled banners must not be delivered.");
            host.Host.RunLocked(() =>
            {
                var response = host.Host.Actions.SendNotification("active", null, "Build", "Finished");
                Assert.True(response.Ok);
                Assert.Equal(ControlNotify.BannersOffNote, response.Result?.Text);
                Assert.Equal(1, host.Host.Store.ActiveSession?.UnseenCount);
            });
            Assert.NotNull(scheduled);
            Assert.Equal(0, delivered);
            scheduled();
            completion.SetResult();
            Assert.Equal(1, delivered);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Status_and_invalid_notify_never_request_taskbar_attention()
    {
        var directory = Directory.CreateTempSubdirectory("agterm-taskbar-rejected-").FullName;
        try
        {
            using var host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = directory,
                ConfigDir = directory,
                PipeName = "agterm-taskbar-" + Guid.NewGuid().ToString("N"),
                NotificationSettings = new AppSettings { DockBounce = "once", NotificationsEnabled = false },
                OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
                NotificationScheduler = _ => throw new InvalidOperationException("No delivery should be scheduled."),
                Log = message => Assert.Fail(message),
            });
            host.Host.RunLocked(() =>
            {
                Assert.False(host.Host.Actions.SendNotification("missing-session", null, "Build", "Finished").Ok);
                Assert.True(host.Host.Actions.SetSessionStatus("active", null,
                    new ControlSessionStatusUpdate(AgentStatus.Blocked, null, null, null)).Ok);
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("off")]
    [InlineData("future")]
    public void Disabled_attention_does_not_schedule_work_when_banners_are_off(string? mode)
    {
        var directory = Directory.CreateTempSubdirectory("agterm-taskbar-disabled-").FullName;
        try
        {
            using var host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = directory,
                ConfigDir = directory,
                PipeName = "agterm-taskbar-" + Guid.NewGuid().ToString("N"),
                NotificationSettings = new AppSettings { DockBounce = mode, NotificationsEnabled = false },
                OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
                NotificationScheduler = _ => throw new InvalidOperationException("No delivery should be scheduled."),
                Log = message => Assert.Fail(message),
            });
            host.Host.RunLocked(() => Assert.True(
                host.Host.Actions.SendNotification("active", null, "Build", "Finished").Ok));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void App_wires_notification_attention_independently_of_toasts_and_logical_window_selection()
    {
        var source = Source("App.xaml.cs");
        Assert.Contains("RequestTaskbarAttentionAsync(", source);
        Assert.Contains("WindowsTaskbarFlash.IsApplicationForeground", source);
        Assert.Contains("WindowsTaskbarFlash.Apply", source);
        Assert.Contains("SubscribeAsync(", source);
        Assert.Contains("_taskbarAttention.ApplySettings(", source);
        Assert.Contains("_taskbarAttention.WindowClosed(", source);
        Assert.Contains("_taskbarAttention.Dispose();", source);
        Assert.Contains("_taskbarAttention.Activated(", source);
        Assert.Contains("WindowsTaskbarFlash.IsForegroundWindow(hwnd)", source);
        Assert.Contains("_quickWindow.Activated += OnAttentionWindowActivated;", source);
        Assert.Contains("_settingsWindow.Activated += OnAttentionWindowActivated;", source);
        Assert.Contains("window.Activated += OnAttentionWindowActivated;", source);
        Assert.DoesNotContain("NotificationSuppression", source);
        Assert.Contains("host.AttentionRequested +=", Source("MainWindow.xaml.cs"));
    }

    private static string Source(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
                return File.ReadAllText(Path.Combine(directory.FullName, "src", "AgTerm.App", name));
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class FakeSurface : IHeadlessSurface
    {
        public string? PaneToken => "fake";
        public bool Inject(string text) => true;
        public string? ReadScreenText(bool all, int? lines) => "";
        public string? SelectedText => null;
        public void SelectAll() { }
        public bool Paste() => true;
        public bool PerformBindingAction(string action) => true;
        public void PromoteToPrimaryPane() { }
        public void Teardown() { }
    }
}
