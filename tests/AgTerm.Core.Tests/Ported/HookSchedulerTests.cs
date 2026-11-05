using AgTerm.Core.Hooks;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Ported;

// Port of HookSchedulerTests.swift (upstream a4944436).
internal sealed class FakeHookLauncher : IHookLauncher
{
    public sealed record Launch(HookEntry Entry, ControlEvent Event, Action<string> OnDeliveryFailure,
        Action<int> OnExit);

    public List<Launch> Launches { get; } = new();

    public string? ThrowNext { get; set; }

    private int _nextPid = 100;

    public int Start(HookEntry entry, ControlEvent controlEvent, Action<string> onDeliveryFailure,
        Action<int> onExit)
    {
        if (ThrowNext is { } message)
        {
            ThrowNext = null;
            throw new InvalidOperationException(message);
        }
        Launches.Add(new Launch(entry, controlEvent, onDeliveryFailure, onExit));
        return ++_nextPid;
    }
}

public sealed class HookSchedulerTests
{
    private readonly FakeHookLauncher _launcher = new();
    private double _tick = 1_000;

    private static ControlEvent Event(ControlEventKind kind, ulong seq) =>
        new() { Seq = seq, Ts = seq, Kind = kind, Session = $"s{seq}" };

    private static HooksConfig Hooks(string lines) => HooksParser.Parse(lines).Hooks;

    private HookScheduler MakeScheduler() =>
        new(_launcher, () => DateTimeOffset.FromUnixTimeMilliseconds((long)(_tick * 1000)));

    private ulong[] Seqs() => _launcher.Launches.Select(l => l.Event.Seq).ToArray();

    private string[] Commands() => _launcher.Launches.Select(l => l.Entry.Command).ToArray();

    [Fact]
    public void EventsFanOutToEveryMatchingHookWithIndependentSlots()
    {
        var scheduler = MakeScheduler();
        scheduler.Apply(Hooks("on status a\non status b\non notify c"));

        scheduler.Dispatch(Event(ControlEventKind.Status, 1));
        scheduler.Dispatch(Event(ControlEventKind.TreeChanged, 2));

        Assert.Equal(new[] { "a", "b" }, Commands());
        Assert.Equal(new ulong[] { 1, 1 }, Seqs());
        _launcher.Launches[0].OnExit(0);
        scheduler.Dispatch(Event(ControlEventKind.Status, 3));
        Assert.Equal(new[] { "a", "b", "a" }, Commands());
        Assert.Equal(new[] { 0, 1, 0 }, scheduler.Status().Select(r => r.Pending));
    }

    [Fact]
    public void BurstIsDeliveredInOrderOneChildAtATime()
    {
        var scheduler = MakeScheduler();
        scheduler.Apply(Hooks("on status a"));

        for (ulong seq = 1; seq <= 4; seq++) scheduler.Dispatch(Event(ControlEventKind.Status, seq));
        Assert.Equal(new ulong[] { 1 }, Seqs());
        Assert.Equal(3, scheduler.Status()[0].Pending);
        Assert.NotNull(scheduler.Status()[0].RunningPid);

        _launcher.Launches[0].OnExit(0);
        Assert.Equal(new ulong[] { 1, 2 }, Seqs());
        _launcher.Launches[1].OnExit(0);
        _launcher.Launches[2].OnExit(0);
        Assert.Equal(new ulong[] { 1, 2, 3, 4 }, Seqs());
        Assert.Equal(0, scheduler.Status()[0].Pending);
        _launcher.Launches[3].OnExit(0);
        Assert.Null(scheduler.Status()[0].RunningPid);
    }

    [Fact]
    public void PendingIsCappedDroppingTheOldestAndCounting()
    {
        var scheduler = MakeScheduler();
        scheduler.Apply(Hooks("on status a"));

        for (ulong seq = 1; seq <= HookScheduler.PendingCapacity + 3; seq++)
            scheduler.Dispatch(Event(ControlEventKind.Status, seq));
        Assert.Equal(HookScheduler.PendingCapacity, scheduler.Status()[0].Pending);
        Assert.Equal(2UL, scheduler.Status()[0].Dropped);

        _launcher.Launches[0].OnExit(0);
        Assert.Equal(4UL, _launcher.Launches[^1].Event.Seq);
    }

    [Fact]
    public void LaunchThrowAdvancesTheQueueAndRecordsOneFailure()
    {
        var scheduler = MakeScheduler();
        var banners = new List<string>();
        scheduler.OnFailure = (_, message) => banners.Add(message);
        scheduler.Apply(Hooks("on status a"));

        scheduler.Dispatch(Event(ControlEventKind.Status, 1));
        _launcher.ThrowNext = "no such file";
        scheduler.Dispatch(Event(ControlEventKind.Status, 2));
        scheduler.Dispatch(Event(ControlEventKind.Status, 3));
        _launcher.Launches[0].OnExit(0);

        Assert.Equal(new ulong[] { 1, 3 }, Seqs());
        Assert.Equal("spawn failed: no such file", scheduler.Status()[0].LastFailure);
        Assert.Equal(new[] { "spawn failed: no such file" }, banners);
        _launcher.Launches[1].OnExit(0);
        Assert.Null(scheduler.Status()[0].LastFailure);
    }

    [Fact]
    public void DeliveryFailureOnALiveRunNeverReleasesTheSlot()
    {
        var scheduler = MakeScheduler();
        var banners = new List<string>();
        scheduler.OnFailure = (_, message) => banners.Add(message);
        scheduler.Apply(Hooks("on status a"));

        scheduler.Dispatch(Event(ControlEventKind.Status, 1));
        scheduler.Dispatch(Event(ControlEventKind.Status, 2));
        _launcher.Launches[0].OnDeliveryFailure("EBADF");
        Assert.Single(_launcher.Launches);
        Assert.NotNull(scheduler.Status()[0].RunningPid);
        Assert.Equal(1, scheduler.Status()[0].Pending);
        Assert.Equal("delivery failed: EBADF", scheduler.Status()[0].LastFailure);
        Assert.Equal(new[] { "delivery failed: EBADF" }, banners);

        scheduler.Apply(Hooks("on status a"));
        Assert.Equal("delivery failed: EBADF", scheduler.Status()[0].LastFailure);

        _launcher.Launches[0].OnExit(0);
        Assert.Equal("delivery failed: EBADF", scheduler.Status()[0].LastFailure);
        Assert.Equal(new ulong[] { 1, 2 }, Seqs());
        _launcher.Launches[1].OnExit(0);
        Assert.Null(scheduler.Status()[0].LastFailure);
    }

    [Fact]
    public void NonZeroExitBannersOnceUntilSuccessOrReload()
    {
        var scheduler = MakeScheduler();
        var banners = new List<string>();
        scheduler.OnFailure = (_, message) => banners.Add(message);
        scheduler.Apply(Hooks("on status a"));

        scheduler.Dispatch(Event(ControlEventKind.Status, 1));
        _launcher.Launches[0].OnExit(2);
        scheduler.Dispatch(Event(ControlEventKind.Status, 2));
        _launcher.Launches[1].OnExit(3);
        Assert.Equal(new[] { "exit 2" }, banners);
        Assert.Equal("exit 3", scheduler.Status()[0].LastFailure);

        scheduler.Apply(Hooks("on status a"));
        scheduler.Dispatch(Event(ControlEventKind.Status, 3));
        _launcher.Launches[2].OnExit(4);
        Assert.Equal(new[] { "exit 2", "exit 4" }, banners);

        scheduler.Dispatch(Event(ControlEventKind.Status, 4));
        _launcher.Launches[3].OnExit(0);
        Assert.Null(scheduler.Status()[0].LastFailure);
        scheduler.Dispatch(Event(ControlEventKind.Status, 5));
        _launcher.Launches[4].OnExit(5);
        Assert.Equal(new[] { "exit 2", "exit 4", "exit 5" }, banners);
    }

    [Fact]
    public void ReloadAfterReorderAndCommentsKeepsStateAndUpdatesLines()
    {
        var scheduler = MakeScheduler();
        var banners = new List<string>();
        scheduler.OnFailure = (_, message) => banners.Add(message);
        scheduler.Apply(Hooks("on status a\non status b"));
        for (ulong seq = 1; seq <= 3; seq++) scheduler.Dispatch(Event(ControlEventKind.Status, seq));
        _launcher.Launches[1].OnExit(7);
        var pidA = scheduler.Status()[0].RunningPid;

        scheduler.Apply(Hooks("# moved\n\non status b\n# a comment above a\non status a"));

        var rows = scheduler.Status();
        Assert.Equal(new[] { "b", "a" }, rows.Select(r => r.Command));
        Assert.Equal(new[] { 3, 5 }, rows.Select(r => r.Line));
        Assert.Equal(pidA, rows[1].RunningPid);
        Assert.Equal(2, rows[1].Pending);
        Assert.Equal("exit 7", rows[0].LastFailure);
        Assert.Equal(1, rows[0].Pending);
        Assert.Equal(new ulong[] { 1, 1, 2 }, Seqs());

        scheduler.Dispatch(Event(ControlEventKind.Status, 4));
        Assert.Equal(3, _launcher.Launches.Count);
        _launcher.Launches[2].OnExit(8);
        Assert.Equal(new[] { "exit 7", "exit 8" }, banners);
        Assert.Equal(new ulong[] { 1, 1, 2, 3 }, Seqs());
    }

    [Fact]
    public void ReloadKeepsANonzeroDroppedCount()
    {
        var scheduler = MakeScheduler();
        scheduler.Apply(Hooks("on status a"));
        for (ulong seq = 1; seq <= HookScheduler.PendingCapacity + 3; seq++)
            scheduler.Dispatch(Event(ControlEventKind.Status, seq));

        scheduler.Apply(Hooks("# comment\non status a"));

        var row = scheduler.Status()[0];
        Assert.Equal(2UL, row.Dropped);
        Assert.Equal(HookScheduler.PendingCapacity, row.Pending);
        Assert.Equal(2, row.Line);
    }

    [Fact]
    public void RemovedHookDropsPendingAndStaysOccupiedUntilExit()
    {
        var scheduler = MakeScheduler();
        scheduler.Apply(Hooks("on status a\non status b"));
        for (ulong seq = 1; seq <= 3; seq++) scheduler.Dispatch(Event(ControlEventKind.Status, seq));

        scheduler.Apply(Hooks("on status b"));
        scheduler.Dispatch(Event(ControlEventKind.Status, 4));
        var rows = scheduler.Status();
        Assert.Equal(new[] { "b", "a" }, rows.Select(r => r.Command));
        Assert.Equal(new bool?[] { null, true }, rows.Select(r => r.Retired));
        Assert.Equal(0, rows[1].Pending);
        Assert.Equal(101, rows[1].RunningPid);
        Assert.Single(_launcher.Launches, l => l.Entry.Command == "a");

        _launcher.Launches[0].OnExit(0);
        Assert.Single(_launcher.Launches, l => l.Entry.Command == "a");
        Assert.Equal(new[] { "b" }, scheduler.Status().Select(r => r.Command));
    }

    [Fact]
    public void ChangedCommandIsANewIdentityRunningAlongsideTheRetiredOne()
    {
        var scheduler = MakeScheduler();
        scheduler.Apply(Hooks("on status a"));
        scheduler.Dispatch(Event(ControlEventKind.Status, 1));

        scheduler.Apply(Hooks("on status a2"));
        scheduler.Dispatch(Event(ControlEventKind.Status, 2));

        Assert.Equal(new[] { "a", "a2" }, Commands());
        Assert.Equal(new[] { "a2", "a" }, scheduler.Status().Select(r => r.Command));
        Assert.Equal(new bool?[] { null, true }, scheduler.Status().Select(r => r.Retired));
        _launcher.Launches[0].OnExit(0);
        Assert.Equal(2, _launcher.Launches.Count);
        Assert.Equal(new[] { "a2" }, scheduler.Status().Select(r => r.Command));
    }

    [Fact]
    public void ReAddBeforeExitReattachesAndTheOriginalExitStartsTheNewQueue()
    {
        var scheduler = MakeScheduler();
        scheduler.Apply(Hooks("on status a"));
        scheduler.Dispatch(Event(ControlEventKind.Status, 1));
        scheduler.Dispatch(Event(ControlEventKind.Status, 2));
        var firstExit = _launcher.Launches[0].OnExit;

        scheduler.Apply(Hooks(""));
        Assert.Equal(new bool?[] { true }, scheduler.Status().Select(r => r.Retired));
        scheduler.Apply(Hooks("on status a"));
        scheduler.Dispatch(Event(ControlEventKind.Status, 3));
        Assert.Single(_launcher.Launches);
        var rows = scheduler.Status();
        Assert.Single(rows);
        Assert.Null(rows[0].Retired);
        Assert.Equal(1, rows[0].Pending);
        Assert.Equal(101, rows[0].RunningPid);

        firstExit(0);
        Assert.Equal(new ulong[] { 1, 3 }, Seqs());

        // a duplicate exit for the finished run is ignored
        firstExit(0);
        Assert.Equal(2, _launcher.Launches.Count);
        Assert.NotNull(scheduler.Status()[0].RunningPid);
        _launcher.Launches[1].OnExit(0);
        Assert.Null(scheduler.Status()[0].RunningPid);
    }

    [Fact]
    public void StatusReportsLiveStateInFileOrder()
    {
        var scheduler = MakeScheduler();
        scheduler.Apply(Hooks("on notify n\non status a"));
        scheduler.Dispatch(Event(ControlEventKind.Status, 1));
        scheduler.Dispatch(Event(ControlEventKind.Status, 2));
        _tick += 2.5;

        var rows = scheduler.Status();
        Assert.Equal(new[] { "notify", "status" }, rows.Select(r => r.Kind));
        Assert.Equal(new ControlHookEntry { Kind = "notify", Command = "n", Line = 1 }, rows[0]);
        Assert.Equal(101, rows[1].RunningPid);
        Assert.Equal(2.5, rows[1].ElapsedSeconds);
        Assert.Equal(1, rows[1].Pending);
        Assert.Equal(0UL, rows[1].Dropped);
        Assert.Null(rows[1].LastFailure);
    }
}
