using AgTerm.Core.Model;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// Ported from agtermTests ControlServerHudAutoHideTests.swift (upstream ed831ab), moved to the store: the
// port arms the timer in AppStore behind HudAutoHideScheduler, so these tests fire it by hand.
public class AppStoreHudTimerTests
{
    private sealed class ManualTimer(TimeSpan delay, Action fire) : IDisposable
    {
        public TimeSpan Delay { get; } = delay;

        public bool Disposed { get; private set; }

        public void Fire() => fire();

        public void Dispose() => Disposed = true;
    }

    private readonly List<ManualTimer> _timers = new();

    private (AppStore Store, Session Session) NewSession()
    {
        var store = AppStoreFixtures.MakeStore();
        store.HudAutoHideScheduler = (delay, fire) =>
        {
            var timer = new ManualTimer(delay, fire);
            _timers.Add(timer);
            return timer;
        };
        var session = store.AddSession(store.AddWorkspace("work").Id, "/repo")!;
        return (store, session);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    public void AHudWithNoAutoHideArmsNothing(double? hideAfter)
    {
        var (store, session) = NewSession();

        store.OpenHud(session.Id, new HudSpec("waiting", HideAfter: hideAfter), null);

        Assert.Empty(_timers);
    }

    [Fact]
    public void AnOpenArmsOneTimerForItsSecondsAndTheCapBoundsIt()
    {
        var (store, session) = NewSession();

        store.OpenHud(session.Id, new HudSpec("one", HideAfter: 2.5), null);
        Assert.Equal(TimeSpan.FromSeconds(2.5), Assert.Single(_timers).Delay);

        store.OpenHud(session.Id, new HudSpec("two", HideAfter: 90_000), null);
        Assert.Equal(TimeSpan.FromSeconds(HudSpec.MaxHideAfter), _timers[1].Delay);
    }

    [Fact]
    public void AnExpiredTimerClosesTheHudAndSelectsNothing()
    {
        var (store, session) = NewSession();
        var selected = store.AddSession(store.Workspaces[0].Id, "/other")!;
        store.SelectSession(selected.Id);
        store.OpenHud(session.Id, new HudSpec("one", HideAfter: 30), null);
        var changes = 0;
        store.HudChanged += () => changes++;

        _timers.Single().Fire();

        Assert.False(session.HudActive);
        Assert.Equal(selected.Id, store.SelectedSessionId);
        Assert.Equal(1, changes);
        Assert.True(_timers.Single().Disposed);
    }

    [Fact]
    public void AnUpdateRestartsTheIntervalAndTheEarlierTimerCannotClose()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("one", HideAfter: 30), null);

        store.UpdateHud(session.Id, new HudSpec("two", HideAfter: 30), null);

        Assert.Equal(2, _timers.Count);
        Assert.True(_timers[0].Disposed);
        _timers[0].Fire();
        Assert.True(session.HudActive);
        _timers[1].Fire();
        Assert.False(session.HudActive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    public void AnUpdateWithoutAnAutoHideCancelsTheRunningOne(double? hideAfter)
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("one", HideAfter: 30), null);

        store.UpdateHud(session.Id, new HudSpec("two", HideAfter: hideAfter), null);

        Assert.True(Assert.Single(_timers).Disposed);
        _timers[0].Fire();
        Assert.True(session.HudActive);
    }

    [Fact]
    public void AReplacingOpenLeavesTheOldTimerStale()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("one", HideAfter: 30), null);

        store.OpenHud(session.Id, new HudSpec("two"), null);

        Assert.True(Assert.Single(_timers).Disposed);
        _timers[0].Fire();
        Assert.Equal("two", session.Hud!.Message);
    }

    [Fact]
    public void AResizeAndNewMetricsLeaveTheTimerRunning()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("one", HideAfter: 30), null);

        store.ResizeHud(session.Id, 50);
        store.SetHudMetrics(session.Id, new HudSessionMetrics(12, 8, 18, new HudRect(1000, 640), null, null, 12));

        var timer = Assert.Single(_timers);
        Assert.False(timer.Disposed);
        timer.Fire();
        Assert.False(session.HudActive);
    }

    [Theory]
    [InlineData("hud.close")]
    [InlineData("overlay.open")]
    [InlineData("session.close")]
    [InlineData("workspace.remove")]
    [InlineData("split.close")]
    [InlineData("primary.exit")]
    [InlineData("soft.close")]
    [InlineData("all")]
    public void EveryCloseCancelsTheTimer(string path)
    {
        var (store, session) = NewSession();
        store.AddWorkspace("keep");
        session.Surface = new SpySurface("left-token");
        store.ToggleSplit(session.Id);
        session.SplitSurface = new SpySurface("right-token");
        Guid? anchor = path switch
        {
            "split.close" => session.SplitPaneIdentity,
            "primary.exit" => session.PaneIdentity,
            _ => null,
        };
        store.OpenHud(session.Id, new HudSpec("one", HideAfter: 30), anchor);

        switch (path)
        {
            case "hud.close": store.CloseHud(session.Id); break;
            case "overlay.open": store.OpenOverlay(session.Id, "htop"); break;
            case "session.close": store.CloseSession(session.Id); break;
            case "workspace.remove": store.RemoveWorkspace(store.Workspaces[0].Id); break;
            case "split.close": store.CloseSplit(session.Id); break;
            case "primary.exit": store.ClosePrimaryPane(session.Id); break;
            case "soft.close": store.SoftCloseSession(session.Id, 60); break;
            case "all": store.CloseAllHuds(); break;
        }

        Assert.False(session.HudActive);
        Assert.True(Assert.Single(_timers).Disposed);
    }

    // review focus 5: a window close runs CloseAllHuds, and an expiry already on its way to the
    // host lock must then find no HUD to close and change nothing
    [Fact]
    public void AnExpiryThatFiresAfterCloseAllHudsChangesNothing()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("deploying", HideAfter: 30), null);
        store.CloseAllHuds();
        var revision = session.HudRevision;
        var changes = 0;
        store.HudChanged += () => changes++;

        var fired = Record.Exception(() => _timers.Single().Fire());

        Assert.Null(fired);
        Assert.False(session.HudActive);
        Assert.Equal(revision, session.HudRevision);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void AnUndoneSoftCloseBringsNoTimedHudBackAndItsTimerStaysStale()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("deploying", HideAfter: 10), null);
        store.SoftCloseSession(session.Id, 60);
        store.UndoPendingClose();
        store.OpenHud(session.Id, new HudSpec("again"), null);

        _timers[0].Fire();

        Assert.Equal("again", session.Hud!.Message);
    }

    [Fact]
    public async Task TheDefaultTimerRunsTheExpiryThroughTheHostScheduler()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/repo")!;
        var scheduled = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.PendingCloseFinalizationScheduler = action => scheduled.TrySetResult(action);

        store.OpenHud(session.Id, new HudSpec("deploying", HideAfter: 0.01), null);
        var expire = await scheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(session.HudActive);
        expire();
        Assert.False(session.HudActive);
    }
}
