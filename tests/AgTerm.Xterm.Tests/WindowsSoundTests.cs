using AgTerm.App;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.ControlServer;

namespace AgTerm.Xterm.Tests;

public sealed class WindowsSoundTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("agterm-audio-").FullName;
    private SoundCatalog Catalog => new(directory, Path.Combine(directory, "media"));
    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Theory]
    [InlineData(NotificationSuppressionState.NotPresent, false)]
    [InlineData(NotificationSuppressionState.Busy, false)]
    [InlineData(NotificationSuppressionState.FullScreen, false)]
    [InlineData(NotificationSuppressionState.Presentation, false)]
    [InlineData(NotificationSuppressionState.AcceptsNotifications, true)]
    [InlineData(NotificationSuppressionState.QuietTime, false)]
    [InlineData(NotificationSuppressionState.App, true)]
    [InlineData(NotificationSuppressionState.Failed, false)]
    [InlineData(NotificationSuppressionState.Disabled, false)]
    [InlineData(NotificationSuppressionState.Restricted, false)]
    public async Task Notify_obeys_suppression_but_status_and_preview_bypass_it(
        NotificationSuppressionState state, bool allowed)
    {
        var played = new List<CatalogSound>();
        var logs = new List<string>();
        var suppression = new FakeSuppression(state);
        var player = new WindowsNotificationSound(Catalog, suppression, sound => { played.Add(sound); return true; }, logs.Add);
        Assert.True(await player.TryPlayAsync("default", NotificationSoundKind.Notify));
        Assert.Equal(allowed ? 1 : 0, played.Count);
        Assert.True(await player.TryPlayAsync("beep"));
        Assert.True(await player.TryPlayAsync("default", NotificationSoundKind.Preview));
        Assert.Equal(allowed ? 3 : 2, played.Count);
        Assert.Equal(1, suppression.Calls);
        if (state == NotificationSuppressionState.Failed) Assert.NotEmpty(logs);
    }

    [Fact]
    public async Task Missing_notify_sound_falls_back_but_missing_status_sound_does_not()
    {
        var played = new List<CatalogSound>();
        var logs = new List<string>();
        var player = new WindowsNotificationSound(Catalog, new FakeSuppression(NotificationSuppressionState.AcceptsNotifications),
            sound => { played.Add(sound); return true; }, logs.Add);
        Assert.False(await player.TryPlayAsync("Deleted"));
        Assert.Empty(played);
        Assert.True(await player.TryPlayAsync("Deleted", NotificationSoundKind.Notify));
        Assert.True(Assert.Single(played).IsSystemDefault);
        Assert.Equal(2, logs.Count);
        Assert.True(await player.TryPlayAsync(""));
        Assert.Single(played);
    }

    [Fact]
    public async Task Unplayable_notify_file_falls_back_once_and_status_does_not()
    {
        Directory.CreateDirectory(Catalog.UserDirectory);
        File.WriteAllBytes(Path.Combine(Catalog.UserDirectory, "Broken.wav"), []);
        var played = new List<CatalogSound>();
        var logs = new List<string>();
        var player = new WindowsNotificationSound(Catalog, new FakeSuppression(NotificationSuppressionState.AcceptsNotifications),
            sound => { played.Add(sound); return sound.IsSystemDefault; }, logs.Add);
        Assert.False(await player.TryPlayAsync("Broken"));
        Assert.Single(played);
        Assert.True(await player.TryPlayAsync("Broken", NotificationSoundKind.Notify));
        Assert.Equal(3, played.Count);
        Assert.False(played[1].IsSystemDefault);
        Assert.True(played[2].IsSystemDefault);
        Assert.NotEmpty(logs);
    }

    [Fact]
    public async Task Playback_is_off_caller_thread_serialized_and_failure_is_reported()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var order = new List<string>();
        var logs = new List<string>();
        var player = new WindowsNotificationSound(Catalog, new FakeSuppression(NotificationSuppressionState.AcceptsNotifications),
            sound =>
            {
                Assert.NotEqual(callerThread, Environment.CurrentManagedThreadId);
                order.Add("start");
                started.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                order.Add("end");
                return false;
            }, logs.Add);
        var first = player.TryPlayAsync("default");
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        var second = player.TryPlayAsync("beep");
        Assert.False(second.IsCompleted);
        release.Set();
        Assert.False(await first);
        Assert.False(await second);
        Assert.Equal(["start", "end", "start", "end"], order);
        Assert.Equal(2, logs.Count);
    }

    [Fact]
    public void Throttle_uses_windows_case_insensitive_names_and_policy_preserves_whitespace()
    {
        var throttle = new SoundThrottle(TimeSpan.FromMilliseconds(200));
        Assert.True(throttle.Allow("Windows Ding", TimeSpan.Zero));
        Assert.False(throttle.Allow("WINDOWS DING", TimeSpan.FromMilliseconds(20)));
        Assert.True(throttle.Allow("Windows Ding", TimeSpan.FromMilliseconds(200)));
        Assert.Equal(" ", LiveNotificationPolicy.StatusSound(
            new(Guid.NewGuid(), AgentStatus.Active, " "), new()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing-window")]
    public async Task Application_pipe_reports_unknown_sound_before_session_or_window_target_errors(string? window)
    {
        using var application = new LiveApplicationHost(new()
        {
            StateDir = directory, PipeName = "agterm-sound-routing-" + Guid.NewGuid().ToString("N"),
        });
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var request = new ControlRequest
        {
            Cmd = Command.SessionStatus, Target = "missing-session",
            Args = new() { Status = "blocked", Sound = "missing-agterm-test-sound", Window = window },
        };
        var response = await Task.Run(() => client.Send(request));
        Assert.False(response.Ok);
        Assert.StartsWith("unknown sound: missing-agterm-test-sound ", response.Error);
        var invalid = await Task.Run(() => client.Send(request with
        {
            Args = request.Args with { Status = "invalid" },
        }));
        Assert.Equal("invalid status", invalid.Error);
    }

    private sealed class FakeSuppression(NotificationSuppressionState state) : INotificationSuppression
    {
        public int Calls { get; private set; }
        public NotificationSuppressionState Query()
        {
            Calls++;
            return state;
        }
    }
}
