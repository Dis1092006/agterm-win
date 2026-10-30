using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class LiveTerminalNotificationTests
{
    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    public void Suppresses_only_actual_firing_pane_keyboard_focus(bool nativeFocus, bool domFocus, bool accepted)
    {
        using var fixture = new Fixture(nativeFocus);
        Assert.Equal(accepted, fixture.Host.ReportTerminalNotification(fixture.Main,
            new TerminalOscReport(9, "Finished", domFocus)));
        fixture.Drain();
        Assert.Equal(accepted ? 1 : 0, fixture.Session.UnseenCount);
        Assert.Equal(accepted ? 1 : 0, fixture.Events.Count);
        Assert.Equal(accepted ? 1 : 0, fixture.Banners.Count);
        Assert.Equal(accepted ? 1 : 0, fixture.Attention);
        Assert.Equal(AgentStatus.Idle, fixture.Session.AgentIndicator.Status);
    }

    [Fact]
    public void Focus_suppressed_attempt_consumes_global_budget_but_CLI_notify_does_not_use_it()
    {
        using var fixture = new Fixture(nativeFocus: true);
        Assert.False(fixture.Host.ReportTerminalNotification(fixture.Main, new(9, "focused", true)));
        fixture.Host.Host.RunLocked(() => Assert.True(fixture.Host.Actions.SendNotification(
            fixture.Session.Id.ToString("D"), null, "CLI", "body").Ok));
        fixture.Now = TimeSpan.FromMilliseconds(999);
        Assert.False(fixture.Host.ReportTerminalNotification(fixture.Main, new(9, "sibling", false)));
        fixture.Now = TimeSpan.FromSeconds(1);
        Assert.True(fixture.Host.ReportTerminalNotification(fixture.Main, new(9, "sibling", false)));
        fixture.Drain();
        Assert.Equal(2, fixture.Session.UnseenCount);
        Assert.Equal(2, fixture.Banners.Count);
        Assert.Equal(NotificationPane.Main, fixture.Banners[0].Target?.Pane);
        Assert.NotNull(fixture.Banners[1].Target);
    }

    [Fact]
    public void Banner_and_badge_toggles_do_not_gate_event_or_taskbar_attention()
    {
        using var fixture = new Fixture(settings: new AppSettings
        {
            NotificationsEnabled = false, NotificationBadgeEnabled = false, DockBounce = "once",
        });
        Assert.True(fixture.Host.ReportTerminalNotification(fixture.Main, new(777, "notify;Build;done", false)));
        Assert.Empty(fixture.Events);
        fixture.Drain();
        Assert.Equal(1, fixture.Session.UnseenCount);
        Assert.Single(fixture.Events);
        Assert.Empty(fixture.Banners);
        Assert.Equal(1, fixture.Attention);
    }

    [Fact]
    public void Resolves_current_split_role_after_promotion_and_drops_stale_surfaces()
    {
        using var fixture = new Fixture();
        var split = new FakeSurface("split");
        fixture.Host.Host.RunLocked(() => fixture.Session.SplitSurface = split);
        Assert.True(fixture.Host.ReportTerminalNotification(split, new(9, "split", false)));
        fixture.Drain();
        Assert.Equal(NotificationPane.Split, fixture.Banners[0].Target?.Pane);

        fixture.Host.Host.RunLocked(() =>
        {
            fixture.Session.Surface = split;
            fixture.Session.SplitSurface = null;
        });
        fixture.Now = TimeSpan.FromSeconds(1);
        Assert.True(fixture.Host.ReportTerminalNotification(split, new(9, "promoted", false)));
        fixture.Drain();
        Assert.Equal(NotificationPane.Main, fixture.Banners[1].Target?.Pane);
        fixture.Now = TimeSpan.FromSeconds(2);
        Assert.True(fixture.Host.ReportTerminalNotification(fixture.Main, new(9, "stale", false)));
        fixture.Drain();
        Assert.Equal(2, fixture.Session.UnseenCount);
        Assert.Equal(2, fixture.Banners.Count);
    }

    [Fact]
    public void Drops_scratch_overlay_quick_and_discarded_pending_sources()
    {
        using var fixture = new Fixture();
        var scratch = new FakeSurface("scratch");
        var overlay = new FakeSurface("overlay");
        var quick = new FakeSurface("quick");
        fixture.Host.Host.RunLocked(() =>
        {
            fixture.Session.ScratchSurface = scratch;
            fixture.Session.OverlaySurface = overlay;
        });
        foreach (var source in new[] { scratch, overlay, quick })
        {
            fixture.Host.ReportTerminalNotification(source, new(9, source.PaneToken!, false));
            fixture.Drain();
            fixture.Now += TimeSpan.FromSeconds(1);
        }
        fixture.Host.ReportTerminalNotification(fixture.Main, new(9, "before-close", false));
        fixture.Host.Host.RunLocked(() => fixture.Session.Surface = null);
        fixture.Drain();
        Assert.Empty(fixture.Banners);
        Assert.Empty(fixture.Events);
        Assert.Equal(0, fixture.Session.UnseenCount);
    }

    [Fact]
    public void Bounds_pending_OSC_work_and_reports_overflow_without_raw_payload()
    {
        using var fixture = new Fixture();
        for (var i = 0; i < 64; i++)
        {
            Assert.True(fixture.Host.ReportTerminalNotification(fixture.Main, new(9, "message " + i, false)));
            fixture.Now += TimeSpan.FromSeconds(1);
        }
        Assert.False(fixture.Host.ReportTerminalNotification(fixture.Main, new(9, "private overflow body", false)));
        Assert.Contains(fixture.Log, line => line.Contains("queue is full", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Log, line => line.Contains("private overflow body", StringComparison.Ordinal));
        fixture.Drain();
        Assert.Equal(64, fixture.Session.UnseenCount);
    }

    [Fact]
    public void Actual_focus_clears_unseen_and_all_delivered_panes_even_on_same_pane_refocus()
    {
        using var fixture = new Fixture(nativeFocus: true);
        fixture.Host.Host.RunLocked(() => fixture.Host.Actions.SendNotification(
            fixture.Session.Id.ToString("D"), null, "CLI", "body"));
        fixture.Drain();
        fixture.Host.SessionNotificationsCleared += fixture.Cleared.Add;
        Assert.True(fixture.Host.ReportTerminalFocused(fixture.Main));
        fixture.Drain();
        Assert.Equal(0, fixture.Session.UnseenCount);
        Assert.Equal(fixture.Session.Id, Assert.Single(fixture.Cleared));
        Assert.True(fixture.Host.ReportTerminalFocused(fixture.Main));
        fixture.Drain();
        Assert.Equal(2, fixture.Cleared.Count);
    }

    [Fact]
    public void Background_selection_and_seen_keep_pinned_badge_only_semantics_until_actual_focus()
    {
        using var fixture = new Fixture();
        fixture.Host.SessionNotificationsCleared += fixture.Cleared.Add;
        fixture.Host.Host.RunLocked(() => fixture.Host.Actions.MarkSessionSeen(
            fixture.Session.Id.ToString("D"), null));
        fixture.Drain();
        fixture.Host.Host.RunLocked(() => fixture.Host.Store.SelectSession(fixture.Session.Id));
        fixture.Drain();
        Assert.Empty(fixture.Cleared);
    }

    [Fact]
    public void Sanitized_empty_fallback_title_does_not_restore_control_characters_in_the_event()
    {
        using var fixture = new Fixture();
        fixture.Host.Host.RunLocked(() => fixture.Host.Store.RenameSession(fixture.Session.Id, "\0\u202E"));
        Assert.True(fixture.Host.ReportTerminalNotification(fixture.Main, new(9, "body", false)));
        fixture.Drain();
        Assert.Equal("", Assert.Single(fixture.Events).Payload.Title);
        Assert.Equal("", Assert.Single(fixture.Banners).Title);
    }

    [Fact]
    public void Malformed_notification_diagnostics_are_bounded_and_do_not_include_terminal_content()
    {
        using var fixture = new Fixture();
        for (var i = 0; i < 100; i++)
            Assert.False(fixture.Host.ReportTerminalNotification(fixture.Main,
                new(777, "notify;private title without body separator", false)));
        Assert.Single(fixture.Log);
        Assert.DoesNotContain("private", fixture.Log[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Background_focus_report_does_not_clear_badges()
    {
        using var fixture = new Fixture();
        fixture.Session.UnseenCount = 1;
        Assert.False(fixture.Host.ReportTerminalFocused(fixture.Main));
        fixture.Drain();
        Assert.Equal(1, fixture.Session.UnseenCount);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _state = Directory.CreateTempSubdirectory("agterm-terminal-notify-").FullName;
        private Action? _scheduled;
        private TaskCompletionSource? _completion;
        public TimeSpan Now { get; set; }
        public LiveControlHost Host { get; }
        public Session Session { get; }
        public IHeadlessSurface Main { get; }
        public List<LiveBannerNotification> Banners { get; } = [];
        private readonly ControlEventCursor _anchor;
        public IReadOnlyList<ControlEvent> Events => Host.Ring.Read(_anchor,
            new HashSet<ControlEventKind> { ControlEventKind.Notify }).Batch.Items;
        public List<Guid> Cleared { get; } = [];
        public List<string> Log { get; } = [];
        public int Attention { get; private set; }

        public Fixture(bool nativeFocus = false, AppSettings? settings = null)
        {
            Host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = _state, ConfigDir = _state,
                PipeName = "agterm-terminal-notify-" + Guid.NewGuid().ToString("N"),
                OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface("main")),
                HasTerminalKeyboardFocus = () => nativeFocus,
                TerminalNotificationLimiter = new TerminalNotificationLimiter(() => Now),
                NotificationClock = () => Now,
                NotificationSettings = settings ?? new AppSettings { DockBounce = "once", NotificationSoundName = "default" },
                Log = Log.Add,
                NotificationScheduler = action =>
                {
                    Assert.Null(_scheduled);
                    _scheduled = action;
                    _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    return _completion.Task;
                },
            });
            Session = Host.Store.ActiveSession!;
            Main = Assert.IsAssignableFrom<IHeadlessSurface>(Session.Surface);
            var anchor = Host.Ring.Read(null).Batch;
            _anchor = new(anchor.Run, anchor.Next);
            Host.NotificationRequested += Banners.Add;
            Host.AttentionRequested += () => Attention++;
        }

        public void Drain()
        {
            var scheduled = _scheduled;
            var completion = _completion;
            _scheduled = null;
            _completion = null;
            scheduled?.Invoke();
            completion?.SetResult();
        }

        public void Dispose()
        {
            Drain();
            Host.Dispose();
            Directory.Delete(_state, recursive: true);
        }
    }

    private sealed class FakeSurface(string token) : IHeadlessSurface
    {
        public string PaneToken => token;
        public bool Inject(string text) => true;
        public string? ReadScreenText(bool all, int? lines) => "";
        public string? SelectedText => null;
        public bool Paste() => true;
        public bool PerformBindingAction(string action) => true;
        public void PromoteToPrimaryPane() { }
        public void SelectAll() { }
        public void Teardown() { }
    }
}
