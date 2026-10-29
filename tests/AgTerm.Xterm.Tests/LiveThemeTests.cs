using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.ControlServer;
using AgTerm.App;
using System.Text.Json;

namespace AgTerm.Xterm.Tests;

public sealed class LiveThemeTests
{
    [Theory]
    [InlineData("Dracula", null, false, false, true)]
    [InlineData("Builtin Light", null, false, true, false)]
    [InlineData("Dracula", "Builtin Light", true, false, true)]
    [InlineData("Dracula", "Builtin Light", true, true, false)]
    public void Global_chrome_follows_the_active_theme_not_windows_appearance(
        string light, string? dark, bool follow, bool systemDark, bool expectedDark)
    {
        var settings = new AppSettings { Theme = light, DarkTheme = dark, FollowSystemAppearance = follow };
        var presentation = LiveThemeSettings.Resolve(settings, ThemeCatalog.Default, systemDark);
        Assert.Equal(expectedDark, presentation.UseDarkChrome);
    }

    [Fact]
    public void Default_catalog_resolves_bundled_agterm_palette()
    {
        var theme = ThemeCatalog.Default.Resolve("agterm");

        Assert.NotNull(theme);
        Assert.Equal("agterm", theme.Name);
        Assert.Equal("#ffffff", theme.ForegroundColor);
        Assert.Equal("#303030", theme.BackgroundColor);
        Assert.Equal("#d6d6d6", theme.CursorColor);
        Assert.Equal("#5b5b5b", theme.SelectionBackgroundColor);
        Assert.Equal("#999999", theme.CursorTextColor);
        Assert.Equal("#dfdfff", theme.SelectionForegroundColor);
        Assert.Equal(
            [
                "#353535", "#d25252", "#a5c261", "#ffc66d",
                "#2774c7", "#d197d9", "#bed6ff", "#eeeeec",
                "#6a6a6a", "#c96363", "#c2e075", "#e1e48b",
                "#8ab7d9", "#efb5f7", "#dcf4ff", "#ffffff",
            ],
            theme.AnsiColors);
    }

    [Fact]
    public void Renderer_snapshot_applies_theme_without_replacing_other_settings()
    {
        var theme = Assert.IsType<TerminalTheme>(ThemeCatalog.Default.Resolve("agterm"));

        var snapshot = RendererSettingsSnapshot.From(
            new AppSettings
            {
                FontFamily = "Fira Code",
                FontSize = 18,
                BackgroundOpacity = 0.75,
            },
            theme);

        Assert.Equal("Fira Code", snapshot.FontFamily);
        Assert.Equal(18, snapshot.FontSize);
        Assert.Equal(0.75, snapshot.BackgroundOpacity);
        Assert.Equal(theme.ForegroundColor, snapshot.ForegroundColor);
        Assert.Equal(theme.BackgroundColor, snapshot.BackgroundColor);
        Assert.Equal(theme.CursorColor, snapshot.CursorColor);
        Assert.Equal(theme.SelectionBackgroundColor, snapshot.SelectionBackgroundColor);
        Assert.Equal(theme.AnsiColors, snapshot.AnsiColors);
    }

    [Fact]
    public void Catalog_resolves_light_dark_sync_and_chrome_appearance()
    {
        var light = ThemeCatalog.Default.ResolveActive(
            light: "Builtin Light",
            dark: "agterm",
            sync: true,
            systemDark: false);
        var dark = ThemeCatalog.Default.ResolveActive(
            light: "Builtin Light",
            dark: "agterm",
            sync: true,
            systemDark: true);
        var single = ThemeCatalog.Default.ResolveActive(
            light: "Builtin Light",
            dark: "agterm",
            sync: false,
            systemDark: true);

        Assert.Equal("Builtin Light", light.Theme?.Name);
        Assert.False(light.UseDarkChrome);
        Assert.Equal("agterm", dark.Theme?.Name);
        Assert.True(dark.UseDarkChrome);
        Assert.Equal("Builtin Light", single.Theme?.Name);
        Assert.False(single.UseDarkChrome);
    }

    [Fact]
    public void Theme_settings_survive_restart_without_replacing_unrelated_values()
    {
        var stateDirectory = Directory.CreateTempSubdirectory("agterm-theme-settings-").FullName;
        try
        {
            var store = new SettingsStore(stateDirectory);
            var state = new HeadlessThemeSnapshot("Builtin Light", "agterm", Sync: true);
            var updated = LiveThemeSettings.Apply(
                new AppSettings { FontFamily = "Fira Code", FontSize = 19 },
                state);

            store.Save(updated);
            var reloaded = new SettingsStore(stateDirectory).Load();

            Assert.Equal("Fira Code", reloaded.FontFamily);
            Assert.Equal(19, reloaded.FontSize);
            Assert.Equal(state, LiveThemeSettings.From(reloaded));
        }
        finally
        {
            try { Directory.Delete(stateDirectory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Theme_presentation_keeps_renderer_and_window_appearance_on_the_same_active_slot()
    {
        var settings = new AppSettings
        {
            Theme = "Builtin Light",
            DarkTheme = "Dracula",
            FollowSystemAppearance = true,
            FontSize = 17,
        };

        var presentation = LiveThemeSettings.Resolve(
            settings,
            ThemeCatalog.Default,
            systemDark: true);

        Assert.Equal("Dracula", presentation.Theme?.Name);
        Assert.True(presentation.UseDarkChrome);
        Assert.Equal(17, presentation.RendererSettings.FontSize);
        Assert.Equal("#282a36", presentation.RendererSettings.BackgroundColor);
        Assert.Equal(presentation.Theme?.AnsiColors, presentation.RendererSettings.AnsiColors);
    }

    [Fact]
    public void Default_terminal_uses_the_same_dark_fallback_for_renderer_and_chrome()
    {
        var presentation = LiveThemeSettings.Resolve(
            new AppSettings { Theme = null },
            ThemeCatalog.Default,
            systemDark: false);

        Assert.Null(presentation.Theme);
        Assert.Equal("#0C0C0C", presentation.RendererSettings.BackgroundColor);
        Assert.True(presentation.UseDarkChrome);
    }

    [Fact]
    public void Theme_control_publishes_validated_immutable_state_and_ignores_unknown_names()
    {
        var stateDirectory = Path.Combine(
            Path.GetTempPath(),
            $"agterm-theme-{Guid.NewGuid():N}");
        var changes = new List<HeadlessThemeSnapshot>();
        try
        {
            using var host = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDirectory,
                ConfigDir = stateDirectory,
                AvailableThemes = ["agterm", "Dracula"],
                ThemeChanged = changes.Add,
            });

            var accepted = host.Actions.SetTheme(new ControlArgs
            {
                Light = "agterm",
                Dark = "Dracula",
            });

            Assert.True(accepted.Ok);
            Assert.Equal(
                new HeadlessThemeSnapshot("agterm", "Dracula", Sync: true),
                Assert.Single(changes));

            changes.Clear();
            var rejected = host.Actions.SetTheme(new ControlArgs { Name = "Missing" });

            Assert.False(rejected.Ok);
            Assert.Equal("unknown theme: Missing", rejected.Error);
            Assert.Empty(changes);
            Assert.Equal("agterm", host.Actions.Theme.Light);
            Assert.Equal("Dracula", host.Actions.Theme.Dark);
        }
        finally
        {
            if (Directory.Exists(stateDirectory))
                Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [Fact]
    public void Live_host_delivers_rapid_theme_changes_in_order_after_the_gate_callback_returns()
    {
        var stateDirectory = Directory.CreateTempSubdirectory("agterm-live-theme-").FullName;
        Action? scheduled = null;
        var scheduledCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = stateDirectory,
                ConfigDir = stateDirectory,
                PipeName = "agterm-live-theme-" + Guid.NewGuid().ToString("N"),
                AvailableThemes = ["agterm", "Dracula"],
                OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
                NotificationScheduler = action =>
                {
                    Assert.Null(scheduled);
                    scheduled = action;
                    return scheduledCompletion.Task;
                },
            });
            var observed = new List<HeadlessThemeSnapshot>();
            host.ThemeChanged += observed.Add;

            host.Host.RunLocked(() =>
            {
                host.Host.Actions.SetTheme(new ControlArgs { Name = "agterm" });
                host.Host.Actions.SetTheme(new ControlArgs { Name = "Dracula" });
            });

            Assert.NotNull(scheduled);
            Assert.Empty(observed);
            scheduled();
            scheduledCompletion.TrySetResult();

            Assert.Equal(
                [
                    new HeadlessThemeSnapshot("agterm", null, Sync: false),
                    new HeadlessThemeSnapshot("Dracula", null, Sync: false),
                ],
                observed);
        }
        finally
        {
            try { Directory.Delete(stateDirectory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Live_host_starts_with_persisted_theme_slots_without_emitting_a_change()
    {
        var stateDirectory = Directory.CreateTempSubdirectory("agterm-initial-theme-").FullName;
        try
        {
            var initial = new HeadlessThemeSnapshot("Builtin Light", "agterm", Sync: true);
            using var host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = stateDirectory,
                ConfigDir = stateDirectory,
                PipeName = "agterm-initial-theme-" + Guid.NewGuid().ToString("N"),
                AvailableThemes = ThemeCatalog.Default.Names,
                InitialTheme = initial,
                OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
            });
            var changes = new List<HeadlessThemeSnapshot>();
            host.ThemeChanged += changes.Add;

            Assert.Equal(initial.Light, host.Host.Actions.Theme.Light);
            Assert.Equal(initial.Dark, host.Host.Actions.Theme.Dark);
            Assert.True(host.Host.Actions.Theme.Sync);
            Assert.Empty(changes);
        }
        finally
        {
            try { Directory.Delete(stateDirectory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Main_window_routes_theme_intent_through_the_shared_settings_writer()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));
        var start = source.IndexOf("private async Task ApplyLiveThemeAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task ApplySystemAppearanceAsync", start, StringComparison.Ordinal);
        var method = source[start..end];

        Assert.Contains("_applicationHost.Settings.UpdateCurrentAsync(", method, StringComparison.Ordinal);
        Assert.Contains("LiveThemeSettings.ApplyChange(settings, change)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("_settingsStore.Save", method, StringComparison.Ordinal);
        Assert.Contains("Theme could not be applied", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_shows_the_named_theme_over_the_saved_settings_and_ignores_the_slots()
    {
        var settings = new AppSettings
        {
            Theme = "agterm",
            DarkTheme = "Builtin Light",
            FollowSystemAppearance = true,
            FontSize = 17,
        };

        var preview = LiveThemeSettings.Preview(settings, ThemeCatalog.Default, "Dracula");

        Assert.Equal("Dracula", preview.Theme?.Name);
        Assert.True(preview.UseDarkChrome);
        Assert.Equal("#282a36", preview.RendererSettings.BackgroundColor);
        Assert.Equal(17, preview.RendererSettings.FontSize);
        Assert.True(preview.RendererSettings.PreservePaneFontSizes);
    }

    [Fact]
    public void Preview_of_the_default_row_shows_the_default_terminal()
    {
        var preview = LiveThemeSettings.Preview(new AppSettings { Theme = "Dracula" }, ThemeCatalog.Default, null);

        Assert.Null(preview.Theme);
        Assert.Equal("#0C0C0C", preview.RendererSettings.BackgroundColor);
        Assert.True(preview.UseDarkChrome);
    }

    [Fact]
    public void Preview_of_a_light_theme_asks_for_light_chrome()
    {
        var preview = LiveThemeSettings.Preview(new AppSettings(), ThemeCatalog.Default, "Builtin Light");

        Assert.False(preview.UseDarkChrome);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class FakeSurface : IHeadlessSurface
    {
        public string? PaneToken => "theme-fake";
        public bool Inject(string text) => true;
        public string? ReadScreenText(bool all, int? lines) => string.Empty;
        public string? SelectedText => null;
        public void SelectAll() { }
        public bool Paste() => true;
        public bool PerformBindingAction(string action) => true;
        public void PromoteToPrimaryPane() { }
        public void Teardown() { }
    }
}

public sealed class FullscreenControllerTests
{
    [Fact]
    public void Toggle_applies_fullscreen_then_restores_and_commits_state_after_presenter_success()
    {
        var transitions = new List<bool>();
        var controller = new FullscreenController(transitions.Add);

        controller.Toggle();
        Assert.True(controller.IsFullscreen);
        controller.Toggle();

        Assert.False(controller.IsFullscreen);
        Assert.Equal([true, false], transitions);

        var failing = new FullscreenController(_ => throw new InvalidOperationException("presenter failed"));
        Assert.Throws<InvalidOperationException>(() => failing.Toggle());
        Assert.False(failing.IsFullscreen);
    }

    [Fact]
    public void External_presenter_change_resynchronizes_the_next_toggle_without_applying_again()
    {
        var transitions = new List<bool>();
        var controller = new FullscreenController(transitions.Add);

        controller.Toggle();
        controller.Synchronize(isFullscreen: false);
        controller.Toggle();

        Assert.True(controller.IsFullscreen);
        Assert.Equal([true, true], transitions);
    }
}

public sealed class LiveNotificationTests
{
    [Fact]
    public void Notify_publishes_resolved_immutable_request_after_recording_unseen()
    {
        var stateDirectory = Directory.CreateTempSubdirectory("agterm-notify-").FullName;
        var requests = new List<LiveNotificationRequest>();
        try
        {
            using var host = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDirectory,
                ConfigDir = stateDirectory,
                NotificationRequested = request =>
                {
                    requests.Add(request);
                    return true;
                },
            });
            var sessionId = Assert.IsType<Guid>(host.Store.SelectedSessionId);

            var response = host.Actions.SendNotification(
                sessionId.ToString("D"),
                window: null,
                title: "Build",
                body: "Finished");

            Assert.True(response.Ok);
            Assert.Null(response.Result?.Text);
            Assert.Equal(1, host.Store.SessionWithId(sessionId)?.UnseenCount);
            Assert.Equal(
                new LiveNotificationRequest(sessionId, "Build", "Finished"),
                Assert.Single(requests));
        }
        finally
        {
            try { Directory.Delete(stateDirectory, recursive: true); } catch (IOException) { }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Notify_without_title_uses_the_resolved_session_name(string? title)
    {
        var stateDirectory = Directory.CreateTempSubdirectory("agterm-notify-title-").FullName;
        LiveNotificationRequest? request = null;
        try
        {
            using var host = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDirectory,
                ConfigDir = stateDirectory,
                NotificationRequested = value =>
                {
                    request = value;
                    return true;
                },
            });
            var sessionId = Assert.IsType<Guid>(host.Store.SelectedSessionId);
            var expectedTitle = host.Store.SessionWithId(sessionId)?.DisplayName;

            var response = host.Actions.SendNotification(
                sessionId.ToString("D"), null, title, body: "Finished");

            Assert.True(response.Ok);
            Assert.Equal(expectedTitle, request?.Title);
        }
        finally
        {
            try { Directory.Delete(stateDirectory, recursive: true); } catch (IOException) { }
        }
    }


    [Fact]
    public void Session_status_publishes_sound_request_only_after_the_status_mutation()
    {
        var stateDirectory = Directory.CreateTempSubdirectory("agterm-status-sound-").FullName;
        var requests = new List<LiveStatusNotificationRequest>();
        HeadlessHost? owner = null;
        try
        {
            using var host = owner = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDirectory,
                ConfigDir = stateDirectory,
                StatusNotificationRequested = request =>
                {
                    Assert.Equal(
                        request.Status,
                        owner!.Store.SessionWithId(request.SessionId)?.AgentIndicator.Status);
                    requests.Add(request);
                },
            });
            var sessionId = Assert.IsType<Guid>(host.Store.SelectedSessionId);

            var response = host.Actions.SetSessionStatus(
                sessionId.ToString("D"),
                window: null,
                new ControlSessionStatusUpdate(
                    AgentStatus.Blocked,
                    Blink: null,
                    AutoReset: true,
                    Sound: "default"));

            Assert.True(response.Ok);
            Assert.Equal(
                new LiveStatusNotificationRequest(sessionId, AgentStatus.Blocked, "default"),
                Assert.Single(requests));
        }
        finally
        {
            try { Directory.Delete(stateDirectory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Session_status_without_per_call_sound_does_not_queue_live_work()
    {
        var stateDirectory = Directory.CreateTempSubdirectory("agterm-status-silent-").FullName;
        var requests = new List<LiveStatusNotificationRequest>();
        try
        {
            using var host = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDirectory,
                ConfigDir = stateDirectory,
                StatusNotificationRequested = requests.Add,
            });
            var sessionId = Assert.IsType<Guid>(host.Store.SelectedSessionId);

            var response = host.Actions.SetSessionStatus(
                sessionId.ToString("D"),
                window: null,
                new ControlSessionStatusUpdate(
                    AgentStatus.Active,
                    Blink: null,
                    AutoReset: null,
                    Sound: null));

            Assert.True(response.Ok);
            Assert.Empty(requests);
        }
        finally
        {
            try { Directory.Delete(stateDirectory, recursive: true); } catch (IOException) { }
        }
    }


    [Fact]
    public void Notification_policy_gates_banners_and_resolves_notify_and_status_sounds()
    {
        var request = new LiveNotificationRequest(Guid.NewGuid(), "Build", "Finished");

        Assert.Null(LiveNotificationPolicy.ForNotify(
            request,
            new AppSettings { NotificationsEnabled = false, NotificationBadgeEnabled = true }));

        var banner = LiveNotificationPolicy.ForNotify(
            request,
            new AppSettings
            {
                NotificationsEnabled = true,
                NotificationBadgeEnabled = false,
                NotificationSoundName = "default",
            });
        Assert.NotNull(banner);
        Assert.Equal("default", banner.SoundName);

        Assert.Equal(
            "blocked-default",
            LiveNotificationPolicy.StatusSound(
                new LiveStatusNotificationRequest(request.SessionId, AgentStatus.Blocked, null),
                new AppSettings { BlockedStatusSoundName = "blocked-default" }));
        Assert.Equal(
            "per-call",
            LiveNotificationPolicy.StatusSound(
                new LiveStatusNotificationRequest(request.SessionId, AgentStatus.Active, "per-call"),
                new AppSettings { BlockedStatusSoundName = "blocked-default" }));
        Assert.Null(LiveNotificationPolicy.StatusSound(
            new LiveStatusNotificationRequest(request.SessionId, AgentStatus.Active, null),
            new AppSettings { BlockedStatusSoundName = "blocked-default" }));

        var catalog = new SoundCatalog(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.True(catalog.Resolve("default")!.IsSystemDefault);
        Assert.True(catalog.Resolve("beep")!.IsSystemDefault);
        Assert.Null(catalog.Resolve("Glass"));
    }

    [Fact]
    public void Sound_throttle_suppresses_only_same_name_inside_200ms_without_advancing_on_reject()
    {
        var throttle = new SoundThrottle(TimeSpan.FromMilliseconds(200));

        Assert.True(throttle.Allow("default", TimeSpan.Zero));
        Assert.True(throttle.Allow("beep", TimeSpan.FromMilliseconds(10)));
        Assert.False(throttle.Allow("default", TimeSpan.FromMilliseconds(150)));
        Assert.False(throttle.Allow("beep", TimeSpan.FromMilliseconds(199)));
        Assert.True(throttle.Allow("default", TimeSpan.FromMilliseconds(200)));
        Assert.True(throttle.Allow("beep", TimeSpan.FromMilliseconds(210)));
    }

    [Fact]
    public void Live_host_throttles_same_status_sound_before_delivery()
    {
        var stateDirectory = Directory.CreateTempSubdirectory("agterm-sound-throttle-").FullName;
        var now = TimeSpan.Zero;
        try
        {
            using var host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = stateDirectory,
                ConfigDir = stateDirectory,
                PipeName = "agterm-sound-throttle-" + Guid.NewGuid().ToString("N"),
                OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new NotificationFakeSurface()),
                NotificationClock = () => now,
            });
            var sounds = new List<string>();
            using var delivered = new CountdownEvent(2);
            host.StatusNotificationRequested += sound =>
            {
                sounds.Add(sound);
                delivered.Signal();
            };
            var sessionId = Assert.IsType<Guid>(host.Host.Store.SelectedSessionId);

            void SetSound(string sound)
            {
                host.Host.RunLocked(() => host.Host.Actions.SetSessionStatus(
                    sessionId.ToString("D"),
                    null,
                    new ControlSessionStatusUpdate(
                        AgentStatus.Active,
                        Blink: null,
                        AutoReset: null,
                        Sound: sound)));
            }

            SetSound("default");
            now = TimeSpan.FromMilliseconds(199);
            SetSound("default");
            SetSound("beep");
            now = TimeSpan.FromMilliseconds(200);
            SetSound("default");

            Assert.True(delivered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(["default", "default"], sounds);
        }
        finally
        {
            try { Directory.Delete(stateDirectory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Live_host_delivers_notification_work_after_the_gate_and_rejects_disabled_banners()
    {
        var stateDirectory = Directory.CreateTempSubdirectory("agterm-live-notify-").FullName;
        Action? scheduled = null;
        var scheduledCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = stateDirectory,
                ConfigDir = stateDirectory,
                PipeName = "agterm-live-notify-" + Guid.NewGuid().ToString("N"),
                NotificationSettings = new AppSettings
                {
                    NotificationsEnabled = true,
                    NotificationSoundName = "default",
                },
                OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new NotificationFakeSurface()),
                NotificationScheduler = action =>
                {
                    Assert.Null(scheduled);
                    scheduled = action;
                    return scheduledCompletion.Task;
                },
            });
            var banners = new List<LiveBannerNotification>();
            var statuses = new List<string>();
            host.NotificationRequested += banners.Add;
            host.StatusNotificationRequested += statuses.Add;
            var sessionId = Assert.IsType<Guid>(host.Host.Store.SelectedSessionId);

            ControlResponse? notifyResponse = null;
            host.Host.RunLocked(() =>
            {
                notifyResponse = host.Host.Actions.SendNotification(
                    sessionId.ToString("D"), null, "Build", "Finished");
                host.Host.Actions.SetSessionStatus(
                    sessionId.ToString("D"),
                    null,
                    new ControlSessionStatusUpdate(
                        AgentStatus.Blocked,
                        Blink: null,
                        AutoReset: true,
                        Sound: "default"));
            });

            Assert.Null(notifyResponse?.Result?.Text);
            Assert.NotNull(scheduled);
            Assert.Empty(banners);
            Assert.Empty(statuses);
            host.SetNotificationSettings(new AppSettings { NotificationsEnabled = false });
            scheduled();
            scheduledCompletion.TrySetResult();
            Assert.Equal("default", Assert.Single(banners).SoundName);
            Assert.Equal("default", Assert.Single(statuses));

            ControlResponse? disabled = null;
            host.Host.RunLocked(() => disabled = host.Host.Actions.SendNotification(
                sessionId.ToString("D"), null, "Hidden", "Body"));
            Assert.Equal(ControlNotify.BannersOffNote, disabled?.Result?.Text);
            Assert.Single(banners);
        }
        finally
        {
            try { Directory.Delete(stateDirectory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Main_window_wires_a_visible_banner_and_explicit_sound_policy()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "AgTerm.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Contains("x:Name=\"NotificationBanner\"", xaml, StringComparison.Ordinal);
        Assert.Contains("host.NotificationRequested +=", source, StringComparison.Ordinal);
        Assert.Contains("host.StatusNotificationRequested +=", source, StringComparison.Ordinal);
        Assert.Contains("WindowsAppNotificationPresenter", source, StringComparison.Ordinal);
        Assert.Contains("_notificationSounds.TryPlayAsync", source, StringComparison.Ordinal);
        var show = source[source.IndexOf("private void ShowNotification(", StringComparison.Ordinal)..];
        show = show[..show.IndexOf("private ", 1, StringComparison.Ordinal)];
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(show, "PlayNotificationSound\\("));
        var presenter = File.ReadAllText(Path.Combine(root, "src", "AgTerm.App", "WindowsAppNotificationPresenter.cs"));
        Assert.Contains(".MuteAudio()", presenter, StringComparison.Ordinal);
        Assert.Contains("SetAudioEvent(AppNotificationSoundEvent.Default)", presenter, StringComparison.Ordinal);
        Assert.Contains("NotificationAudioPolicy.PlayFromApp", show, StringComparison.Ordinal);
        Assert.DoesNotContain("TryPlay", presenter, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class NotificationFakeSurface : IHeadlessSurface
    {
        public string? PaneToken => "notification-fake";
        public bool Inject(string text) => true;
        public string? ReadScreenText(bool all, int? lines) => string.Empty;
        public string? SelectedText => null;
        public void SelectAll() { }
        public bool Paste() => true;
        public bool PerformBindingAction(string action) => true;
        public void PromoteToPrimaryPane() { }
        public void Teardown() { }
    }
}

[Collection("webview2")]
public sealed class LiveThemeWebViewTests
{
    private readonly WebView2Fixture _webView;

    public LiveThemeWebViewTests(WebView2Fixture webView)
    {
        _webView = webView;
    }

    [Fact]
    public async Task Theme_updates_existing_panes_and_is_inherited_by_future_panes()
    {
        var view = await _webView.CreateWebViewAsync();
        var page = await _webView.RunAsync(async _ =>
        {
            var result = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await result.InitializeAsync();
            return result;
        });
        _ = await _webView.RunAsync(_ => page.OpenTerminalAsync("theme-existing"));

        var theme = Assert.IsType<TerminalTheme>(ThemeCatalog.Default.Resolve("agterm"));
        var snapshot = RendererSettingsSnapshot.From(new AppSettings(), theme);
        await _webView.RunAsync<object?>(async _ =>
        {
            await page.ApplyRendererSettingsAsync(snapshot);
            return null;
        });
        _ = await _webView.RunAsync(_ => page.OpenTerminalAsync("theme-future"));

        var raw = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                "[agterm.getRendererSettings('theme-existing').ansiColors," +
                " agterm.getRendererSettings('theme-future').ansiColors]"));
        using var json = JsonDocument.Parse(raw);
        var panes = json.RootElement;

        Assert.Equal(2, panes.GetArrayLength());
        Assert.Equal(theme.AnsiColors, panes[0].EnumerateArray().Select(color => color.GetString()!).ToArray());
        Assert.Equal(theme.AnsiColors, panes[1].EnumerateArray().Select(color => color.GetString()!).ToArray());
    }

    [Fact]
    public async Task Theme_apply_preserves_dashboard_font_and_restores_the_session_font()
    {
        var view = await _webView.CreateWebViewAsync();
        var page = await _webView.RunAsync(async _ =>
        {
            var result = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await result.InitializeAsync();
            return result;
        });
        _ = await _webView.RunAsync(_ => page.OpenTerminalAsync("theme-dashboard", fontSize: 19));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Dashboard(
            1,
            [new TerminalDashboardCell("theme-dashboard", "session:main")],
            "session:main",
            fontSize: 8)));

        var theme = Assert.IsType<TerminalTheme>(ThemeCatalog.Default.Resolve("Dracula"));
        await _webView.RunAsync<object?>(async _ =>
        {
            await page.ApplyRendererSettingsAsync(RendererSettingsSnapshot.From(
                new AppSettings(),
                theme,
                preservePaneFontSizes: true));
            return null;
        });
        var duringDashboard = await ReadFontSizeAsync(view, "theme-dashboard");
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(
            new TerminalLayout.Single(2, "theme-dashboard", "shell")));
        var afterDashboard = await ReadFontSizeAsync(view, "theme-dashboard");

        Assert.Equal(8, duringDashboard);
        Assert.Equal(19, afterDashboard);
    }

    private static TerminalTheme PageTheme() => new(
        "page-theme", "#010101", "#020202", "#030303", "#040404", "#050505", "#060606",
        Enumerable.Range(0, 16).Select(i => $"#1{i:x1}1{i:x1}1{i:x1}").ToArray());

    private async Task<XtermPage> OpenPageAsync(Microsoft.Web.WebView2.WinForms.WebView2 view, string paneId)
    {
        var page = await _webView.RunAsync(async _ =>
        {
            var result = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await result.InitializeAsync();
            return result;
        });
        _ = await _webView.RunAsync(_ => page.OpenTerminalAsync(paneId));
        return page;
    }

    private async Task ApplyAsync(XtermPage page, RendererSettingsSnapshot snapshot) =>
        await _webView.RunAsync<object?>(async _ =>
        {
            await page.ApplyRendererSettingsAsync(snapshot);
            return null;
        });

    private async Task<JsonElement> ReadThemeValuesAsync(
        Microsoft.Web.WebView2.WinForms.WebView2 view,
        string paneId)
    {
        var raw = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                $"(() => {{ const s = agterm.getRendererSettings({JsonSerializer.Serialize(paneId)});" +
                " return { ansiColors: s.ansiColors, cursorTextColor: s.cursorTextColor," +
                " selectionForegroundColor: s.selectionForegroundColor }; })()"));
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    [Fact]
    public async Task Theme_cursor_text_and_selection_text_colors_reach_xterm()
    {
        var view = await _webView.CreateWebViewAsync();
        var page = await OpenPageAsync(view, "text-colors");

        await ApplyAsync(page, RendererSettingsSnapshot.From(new AppSettings(), PageTheme()));
        var values = await ReadThemeValuesAsync(view, "text-colors");

        Assert.Equal("#040404", values.GetProperty("cursorTextColor").GetString());
        Assert.Equal("#060606", values.GetProperty("selectionForegroundColor").GetString());
    }

    [Fact]
    public async Task Default_terminal_after_a_named_theme_drops_its_palette_and_text_colors()
    {
        var view = await _webView.CreateWebViewAsync();
        var page = await OpenPageAsync(view, "back-to-default");

        await ApplyAsync(page, RendererSettingsSnapshot.From(new AppSettings(), PageTheme()));
        await ApplyAsync(page, RendererSettingsSnapshot.From(new AppSettings()));
        var values = await ReadThemeValuesAsync(view, "back-to-default");

        Assert.Equal(JsonValueKind.Null, values.GetProperty("ansiColors").ValueKind);
        Assert.Equal(JsonValueKind.Null, values.GetProperty("cursorTextColor").ValueKind);
        Assert.Equal(JsonValueKind.Null, values.GetProperty("selectionForegroundColor").ValueKind);
    }

    [Fact]
    public async Task A_partial_payload_keeps_the_theme_colors_it_does_not_name()
    {
        var view = await _webView.CreateWebViewAsync();
        var page = await OpenPageAsync(view, "partial");

        await ApplyAsync(page, RendererSettingsSnapshot.From(new AppSettings(), PageTheme()));
        _ = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync("agterm.applyRendererSettings({ fontSize: 20 })"));
        var values = await ReadThemeValuesAsync(view, "partial");

        Assert.Equal("#040404", values.GetProperty("cursorTextColor").GetString());
        Assert.Equal(16, values.GetProperty("ansiColors").GetArrayLength());
    }

    private async Task<double> ReadFontSizeAsync(
        Microsoft.Web.WebView2.WinForms.WebView2 view,
        string paneId)
    {
        var raw = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                $"agterm.getRendererSettings({JsonSerializer.Serialize(paneId)}).fontSize"));
        return JsonSerializer.Deserialize<double>(raw);
    }

    [Fact]
    public async Task A_preview_recolors_the_panes_and_the_saved_look_puts_the_colors_back()
    {
        var view = await _webView.CreateWebViewAsync();
        var page = await OpenPageAsync(view, "preview");
        var saved = new AppSettings { Theme = "agterm" };

        await ApplyAsync(page, LiveThemeSettings.Resolve(saved, ThemeCatalog.Default, systemDark: true).RendererSettings);
        await ApplyAsync(page, LiveThemeSettings.Preview(saved, ThemeCatalog.Default, "Dracula").RendererSettings);
        var during = await ReadBackgroundAsync(view, "preview");
        await ApplyAsync(page, LiveThemeSettings.Resolve(
            saved, ThemeCatalog.Default, systemDark: true, preservePaneFontSizes: true).RendererSettings);
        var after = await ReadBackgroundAsync(view, "preview");

        Assert.Equal("#282a36", during);
        Assert.Equal("#303030", after);
    }

    private async Task<string> ReadBackgroundAsync(
        Microsoft.Web.WebView2.WinForms.WebView2 view,
        string paneId)
    {
        var raw = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                $"agterm.getRendererSettings({JsonSerializer.Serialize(paneId)}).backgroundColor"));
        return JsonSerializer.Deserialize<string>(raw)!;
    }
}
