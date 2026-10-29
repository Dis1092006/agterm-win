using System.Runtime.InteropServices;
using AgTerm.App;

namespace AgTerm.Xterm.Tests;

public sealed class NotificationSuppressionTests
{
    [Theory]
    [InlineData(false, 0, "setting")]
    [InlineData(true, 1, "setting,available,mode")]
    [InlineData(true, 2, "setting,available,mode")]
    [InlineData(true, 99, "setting,available,mode")]
    public void App_disabled_or_restricted_mode_stops_before_later_sources(bool enabled, int mode, string expected)
    {
        var sources = new Sources { Enabled = enabled, Mode = mode };
        var suppression = new NotificationSuppression(sources);
        Assert.NotEqual(NotificationSuppressionState.AcceptsNotifications, suppression.Query());
        Assert.Equal(expected, string.Join(",", sources.Calls));
    }

    [Theory]
    [InlineData(true, 0u, "setting,available,mode,shell")]
    [InlineData(false, 0u, "setting,available,wnf,shell")]
    [InlineData(false, null, "setting,available,wnf,shell")]
    public void Unrestricted_or_failed_wnf_continues_to_shell(bool available, uint? profile, string expected)
    {
        var sources = new Sources { Available = available, Profile = profile };
        var logs = new List<string>();
        Assert.Equal(NotificationSuppressionState.AcceptsNotifications, new NotificationSuppression(sources, logs.Add).Query());
        Assert.Equal(expected, string.Join(",", sources.Calls));
        Assert.Equal(!available && profile is null, logs.Count > 0);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(uint.MaxValue)]
    public void Any_nonzero_wnf_profile_suppresses_without_shell(uint profile)
    {
        var sources = new Sources { Available = false, Profile = profile };
        Assert.NotEqual(NotificationSuppressionState.AcceptsNotifications, new NotificationSuppression(sources).Query());
        Assert.Equal(["setting", "available", "wnf"], sources.Calls);
    }

    [Theory]
    [InlineData("setting")]
    [InlineData("available")]
    [InlineData("mode")]
    [InlineData("shell")]
    public void Supported_source_failure_is_logged_and_suppresses(string failing)
    {
        var sources = new Sources { Failing = failing };
        var logs = new List<string>();
        Assert.Equal(NotificationSuppressionState.Failed, new NotificationSuppression(sources, logs.Add).Query());
        Assert.Single(logs);
    }

    [Fact]
    public void Undocumented_wnf_exception_is_logged_and_fails_open_to_shell()
    {
        var sources = new Sources { Available = false, Failing = "wnf" };
        var logs = new List<string>();
        Assert.Equal(NotificationSuppressionState.AcceptsNotifications, new NotificationSuppression(sources, logs.Add).Query());
        Assert.Equal(["setting", "available", "wnf", "shell"], sources.Calls);
        Assert.Single(logs);
    }

    [Theory]
    [InlineData(NotificationSuppressionState.NotPresent)]
    [InlineData(NotificationSuppressionState.Busy)]
    [InlineData(NotificationSuppressionState.FullScreen)]
    [InlineData(NotificationSuppressionState.Presentation)]
    [InlineData(NotificationSuppressionState.AcceptsNotifications)]
    [InlineData(NotificationSuppressionState.QuietTime)]
    [InlineData(NotificationSuppressionState.App)]
    [InlineData(NotificationSuppressionState.Failed)]
    public void Shell_state_is_preserved(NotificationSuppressionState state)
    {
        var sources = new Sources { Shell = state };
        Assert.Equal(state, new NotificationSuppression(sources).Query());
    }

    [Theory]
    [InlineData("default", true, false)]
    [InlineData("BEEP", true, false)]
    [InlineData("Windows Ding", true, true)]
    [InlineData("default", false, true)]
    [InlineData("beep", false, true)]
    [InlineData("Windows Ding", false, true)]
    [InlineData(null, true, false)]
    [InlineData("", false, false)]
    public void Only_alias_toast_audio_is_owned_by_windows(string? name, bool shown, bool playFromApp)
    {
        Assert.Equal(playFromApp, NotificationAudioPolicy.PlayFromApp(name, shown));
    }

    [Theory]
    [InlineData("default", true)]
    [InlineData("DEFAULT", true)]
    [InlineData("beep", true)]
    [InlineData("BeEp", true)]
    [InlineData("Windows Ding", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    public void Native_toast_default_is_selected_only_for_aliases(string? name, bool native)
    {
        Assert.Equal(native, NotificationAudioPolicy.UsesToastDefault(name));
    }

    private sealed class Sources : INotificationSuppressionSources
    {
        public bool Enabled { get; init; } = true;
        public bool Available { get; init; } = true;
        public int Mode { get; init; }
        public uint? Profile { get; init; }
        public NotificationSuppressionState Shell { get; init; } = NotificationSuppressionState.AcceptsNotifications;
        public string? Failing { get; init; }
        public List<string> Calls { get; } = [];
        private T Read<T>(string name, T value)
        {
            Calls.Add(name);
            if (Failing == name) throw new COMException("test failure");
            return value;
        }
        public bool NotificationsEnabled() => Read("setting", Enabled);
        public bool HasNotificationMode() => Read("available", Available);
        public int NotificationMode() => Read("mode", Mode);
        public uint? QuietHoursProfile() => Read("wnf", Profile);
        public NotificationSuppressionState ShellState() => Read("shell", Shell);
    }
}
