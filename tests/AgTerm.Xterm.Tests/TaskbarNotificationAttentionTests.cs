using AgTerm.App;
using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

public sealed class TaskbarNotificationAttentionTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("off", false)]
    [InlineData("future", false)]
    [InlineData("ONCE", false)]
    [InlineData("once", true)]
    [InlineData("untilFocused", true)]
    public void Stored_mode_controls_one_taskbar_only_flash_without_timeout_override(string? mode, bool enabled)
    {
        var calls = new List<(nint, uint, uint, uint)>();
        using var attention = new TaskbarNotificationAttention(() => false,
            (hwnd, flags, count, timeout) => calls.Add((hwnd, flags, count, timeout)));
        attention.ApplySettings(new AppSettings { DockBounce = mode, NotificationsEnabled = false });
        attention.Request(12);
        Assert.Equal(enabled, calls.Count == 1);
        if (enabled) Assert.Equal(((nint)12, 2u, 1u, 0u), Assert.Single(calls));
    }

    [Fact]
    public void First_target_is_kept_until_real_activation_and_repeats_do_not_restart_the_flash()
    {
        var calls = new List<(nint, uint, uint, uint)>();
        using var attention = new TaskbarNotificationAttention(() => false,
            (hwnd, flags, count, timeout) => calls.Add((hwnd, flags, count, timeout)));
        attention.ApplySettings(new AppSettings { DockBounce = "once" });
        attention.Request(12);
        attention.Request(12);
        attention.Request(24);
        attention.WindowClosed(24);
        attention.ApplySettings(new AppSettings { DockBounce = "untilFocused", FontSize = 20 });
        Assert.Equal(((nint)12, 2u, 1u, 0u), Assert.Single(calls));
        attention.Activated(24);
        Assert.Equal(((nint)12, 0u, 0u, 0u), calls[1]);
        attention.Request(24);
        Assert.Equal(((nint)24, 2u, 1u, 0u), calls[2]);
    }

    [Fact]
    public void Any_application_foreground_window_blocks_requests_and_clears_old_attention()
    {
        var foreground = true;
        var calls = new List<(nint, uint, uint, uint)>();
        using var attention = new TaskbarNotificationAttention(() => foreground,
            (hwnd, flags, count, timeout) => calls.Add((hwnd, flags, count, timeout)));
        attention.ApplySettings(new AppSettings { DockBounce = "once" });
        attention.Request(12);
        Assert.Empty(calls);
        foreground = false;
        attention.Request(12);
        foreground = true;
        attention.Request(24);
        Assert.Equal(new[] { ((nint)12, 2u, 1u, 0u), ((nint)12, 0u, 0u, 0u) }, calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("off")]
    [InlineData("future")]
    public void Off_or_unknown_reload_stops_pending_attention_and_prevents_late_delivery(string? mode)
    {
        var calls = new List<(nint, uint, uint, uint)>();
        using var attention = new TaskbarNotificationAttention(() => false,
            (hwnd, flags, count, timeout) => calls.Add((hwnd, flags, count, timeout)));
        attention.ApplySettings(new AppSettings { DockBounce = "once" });
        attention.Request(12);
        attention.ApplySettings(new AppSettings { DockBounce = mode });
        attention.Request(24);
        Assert.Equal(new[] { ((nint)12, 2u, 1u, 0u), ((nint)12, 0u, 0u, 0u) }, calls);
    }

    [Fact]
    public void Target_close_and_shutdown_stop_only_the_owned_target_and_are_idempotent()
    {
        var calls = new List<(nint, uint, uint, uint)>();
        var attention = new TaskbarNotificationAttention(() => false,
            (hwnd, flags, count, timeout) => calls.Add((hwnd, flags, count, timeout)));
        attention.ApplySettings(new AppSettings { DockBounce = "once" });
        attention.Request(12);
        attention.WindowClosed(12);
        attention.WindowClosed(12);
        attention.Request(24);
        attention.Dispose();
        attention.Dispose();
        attention.ApplySettings(new AppSettings { DockBounce = "once" });
        attention.Request(48);
        Assert.Equal(new[]
        {
            ((nint)12, 2u, 1u, 0u), ((nint)12, 0u, 0u, 0u),
            ((nint)24, 2u, 1u, 0u), ((nint)24, 0u, 0u, 0u),
        }, calls);
    }

    [Fact]
    public void Notification_does_not_replace_an_existing_refused_raise_flash_on_the_same_window()
    {
        var calls = new List<(nint, uint, uint, uint)>();
        using var attention = new TaskbarNotificationAttention(() => false,
            (hwnd, flags, count, timeout) => calls.Add((hwnd, flags, count, timeout)));
        attention.ApplySettings(new AppSettings { DockBounce = "once" });
        attention.RelinquishForRaise(12);
        calls.Add(((nint)12, 0xEu, 0u, 0u));
        attention.Request(12);
        attention.ApplySettings(new AppSettings());
        Assert.Equal(((nint)12, 0xEu, 0u, 0u), Assert.Single(calls));
        attention.Activated(24);
        attention.ApplySettings(new AppSettings { DockBounce = "once" });
        attention.Request(12);
        Assert.Single(calls);
        attention.Activated(12);
        attention.Request(12);
        Assert.Equal(((nint)12, 2u, 1u, 0u), calls[1]);
    }

    [Fact]
    public void Explicit_window_raise_takes_over_its_flash_without_off_cancelling_refused_raise_feedback()
    {
        var calls = new List<(nint, uint, uint, uint)>();
        using var attention = new TaskbarNotificationAttention(() => false,
            (hwnd, flags, count, timeout) => calls.Add((hwnd, flags, count, timeout)));
        attention.ApplySettings(new AppSettings { DockBounce = "once" });
        attention.Request(12);
        attention.RelinquishForRaise(24);
        attention.Request(24);
        attention.RelinquishForRaise(12);
        attention.ApplySettings(new AppSettings());
        attention.Activated(24);
        Assert.Equal(((nint)12, 2u, 1u, 0u), Assert.Single(calls));
    }
}
