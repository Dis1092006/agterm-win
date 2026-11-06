using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// session.info (upstream 80f822bf): the store setter, the tree read-back, the expiry timer and the
// teardown paths. Upstream arms the timer in ControlServer+SessionInfo.swift; the port arms it in the
// store, as it does the HUD auto-hide, so these tests fire it by hand.
public class AppStoreInfoTests : IDisposable
{
    private sealed class ManualTimer(TimeSpan delay, Action fire) : IDisposable
    {
        public TimeSpan Delay { get; } = delay;

        public bool Disposed { get; private set; }

        public void Fire() => fire();

        public void Dispose() => Disposed = true;
    }

    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-session-info-").FullName;
    private readonly List<ControlEventDraft> _events = new();
    private readonly List<ManualTimer> _timers = new();
    private readonly AppStore _store;
    private readonly Guid _workspaceId;
    private readonly Session _session;

    public AppStoreInfoTests()
    {
        _store = new AppStore(persistence: new PersistenceStore(_dir),
                              controlEventSink: _events.Add,
                              stateDirectory: _dir);
        _store.InfoExpiryScheduler = (delay, fire) =>
        {
            var timer = new ManualTimer(delay, fire);
            _timers.Add(timer);
            return timer;
        };
        _workspaceId = _store.AddWorkspace("work").Id;
        _session = _store.AddSession(_workspaceId, "/repo")!;
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private int TreeChanges => _events.Count(e => e.Kind == ControlEventKind.TreeChanged);

    private static SessionInfo Info(string source, double hideAfter = 0, bool markdown = false) =>
        SessionInfo.Make(source, markdown, hideAfter, out _)!;

    [Fact]
    public void SettingInfoEmitsTreeChangedOnlyWhenItChanges()
    {
        var before = TreeChanges;

        Assert.True(_store.SetInfo(Info("3 agents"), _session.Id));
        Assert.Equal(before + 1, TreeChanges);

        Assert.False(_store.SetInfo(Info("3 agents"), _session.Id));
        Assert.Equal(before + 1, TreeChanges);

        Assert.True(_store.SetInfo(null, _session.Id));
        Assert.Equal(before + 2, TreeChanges);
        Assert.Null(_session.Info);

        Assert.False(_store.SetInfo(null, _session.Id));
        Assert.Equal(before + 2, TreeChanges);
    }

    [Fact]
    public void AChangedLineRefreshesTheSidebar()
    {
        var refreshes = 0;
        _store.SidebarChanged += () => refreshes++;

        _store.SetInfo(Info("3 agents", 30), _session.Id);
        Assert.Equal(1, refreshes);

        _store.SetInfo(Info("3 agents", 30), _session.Id);
        Assert.Equal(1, refreshes);

        _timers[^1].Fire();
        Assert.Equal(2, refreshes);

        _store.SetInfo(Info("idle"), _session.Id);
        _store.SetInfo(null, _session.Id);
        Assert.Equal(4, refreshes);
    }

    [Fact]
    public void SettingInfoForAnUnknownSessionChangesNothing()
    {
        var before = TreeChanges;

        Assert.False(_store.SetInfo(Info("x", 30), Guid.NewGuid()));

        Assert.Equal(before, TreeChanges);
        Assert.Empty(_timers);
    }

    [Fact]
    public void InfoIsNotSavedSnapshottedOrDuplicated()
    {
        _store.SetInfo(Info("3 agents"), _session.Id);
        var restored = AppStoreFixtures.MakeStore();

        restored.Restore(Assert.IsType<Snapshot>(
            ControlJson.Deserialize<Snapshot>(ControlJson.Serialize(_store.Snapshot()))));

        Assert.All(restored.Workspaces.SelectMany(w => w.Sessions), s => Assert.Null(s.Info));
        Assert.Null(_store.DuplicateSession(_session.Id)!.Info);
        Assert.DoesNotContain("3 agents", ControlJson.Serialize(_store.Snapshot()));
    }

    [Fact]
    public void TheTreeProjectsTheInfoFields()
    {
        var timed = _store.AddSession(_workspaceId, "/repo/b")!;
        _store.AddSession(_workspaceId, "/repo/c");
        _store.SetInfo(Info("3 agents"), _session.Id);
        _store.SetInfo(Info("**ci**", 60, markdown: true), timed.Id);

        var sessions = _store.ControlTree().Workspaces.Single(w => w.Id == _workspaceId.ToString("D").ToUpperInvariant()).Sessions;

        Assert.Equal("3 agents", sessions[0].Info);
        Assert.False(sessions[0].InfoMarkdown);
        Assert.Equal(0.0, sessions[0].InfoHideAfter);
        Assert.Equal("**ci**", sessions[1].Info);
        Assert.True(sessions[1].InfoMarkdown);
        Assert.Equal(60.0, sessions[1].InfoHideAfter);
        Assert.Null(sessions[2].Info);
        Assert.Null(sessions[2].InfoMarkdown);
        Assert.Null(sessions[2].InfoHideAfter);
    }

    [Fact]
    public void TheInfoFieldsAreOmittedFromJsonWhenUnset()
    {
        Assert.DoesNotContain("\"info", ControlJson.Serialize(_store.ControlTree()));

        _store.SetInfo(Info("x"), _session.Id);

        var json = ControlJson.Serialize(_store.ControlTree());
        Assert.Contains("\"info\":\"x\",\"infoMarkdown\":false,\"infoHideAfter\":0", json);
    }

    [Fact]
    public void ATimedSetArmsItsSecondsAndAnUntimedOneArmsNothing()
    {
        _store.SetInfo(Info("idle"), _session.Id);
        Assert.Empty(_timers);

        _store.SetInfo(Info("3 agents", 30), _session.Id);

        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(_timers).Delay);
    }

    [Fact]
    public void AnExpiredLineIsClearedWithATreeChange()
    {
        _store.SetInfo(Info("brief", 30), _session.Id);
        var before = TreeChanges;

        _timers.Single().Fire();

        Assert.Null(_session.Info);
        Assert.Equal(before + 1, TreeChanges);
        Assert.True(_timers.Single().Disposed);
    }

    [Fact]
    public void AnIdenticalSetRestartsTheExpiryWithoutATreeChange()
    {
        _store.SetInfo(Info("3 agents", 30), _session.Id);
        var before = TreeChanges;

        Assert.False(_store.SetInfo(Info("3 agents", 30), _session.Id));

        Assert.Equal(before, TreeChanges);
        Assert.Equal(2, _timers.Count);
        Assert.True(_timers[0].Disposed);
        _timers[0].Fire();
        Assert.Equal("3 agents", _session.Info?.Source);
        _timers[1].Fire();
        Assert.Null(_session.Info);
    }

    [Fact]
    public void ASupersededTimerIsInert()
    {
        _store.SetInfo(Info("brief", 5), _session.Id);

        _store.SetInfo(Info("long", 30), _session.Id);
        _timers[0].Fire();

        Assert.Equal("long", _session.Info?.Source);
        Assert.False(_timers[1].Disposed);
    }

    [Fact]
    public void ASetWithoutHideAfterCancelsATimedLine()
    {
        _store.SetInfo(Info("brief", 5), _session.Id);

        _store.SetInfo(Info("stays"), _session.Id);
        _timers[0].Fire();

        Assert.True(Assert.Single(_timers).Disposed);
        Assert.Equal("stays", _session.Info?.Source);
    }

    [Fact]
    public void AClearCancelsTheTimer()
    {
        _store.SetInfo(Info("brief", 30), _session.Id);

        _store.SetInfo(null, _session.Id);

        Assert.True(Assert.Single(_timers).Disposed);
        Assert.Null(_session.Info);
    }

    [Fact]
    public void ClosingAHudKeepsTheLineAndItsTimer()
    {
        _store.OpenHud(_session.Id, new HudSpec("one"), null);
        _store.SetInfo(Info("kept", 30), _session.Id);

        Assert.True(_store.CloseHud(_session.Id));

        Assert.Equal("kept", _session.Info?.Source);
        Assert.False(Assert.Single(_timers).Disposed);
    }

    [Theory]
    [InlineData("session.close")]
    [InlineData("workspace.remove")]
    [InlineData("all")]
    public void EveryTeardownDropsTheLineAndCancelsTheTimer(string path)
    {
        _store.AddWorkspace("keep");
        _store.SetInfo(Info("brief", 30), _session.Id);

        switch (path)
        {
            case "session.close": _store.CloseSession(_session.Id); break;
            case "workspace.remove": _store.RemoveWorkspace(_workspaceId); break;
            case "all": _store.ClearAllInfos(); break;
        }

        Assert.Null(_session.Info);
        Assert.True(Assert.Single(_timers).Disposed);
    }

    [Fact]
    public void AFinalizedSoftCloseDropsAnUntimedLine()
    {
        _store.AddSession(_workspaceId, "/other");
        _store.SetInfo(Info("kept"), _session.Id);
        _store.SoftCloseSession(_session.Id, 60);

        _store.FinalizeAllPendingCloses();

        Assert.Null(_session.Info);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("batch")]
    [InlineData("workspace")]
    public void SoftCloseDropsATimedLineAndUndoKeepsAnUntimedOne(string path)
    {
        var timed = _store.AddSession(_workspaceId, "/timed")!;
        var untimed = _store.AddSession(_workspaceId, "/untimed")!;
        var workspaceId = _workspaceId;
        _store.AddWorkspace("staying");
        _store.SetInfo(Info("brief", 30), timed.Id);
        _store.SetInfo(Info("kept"), untimed.Id);

        switch (path)
        {
            case "session":
                Assert.True(_store.SoftCloseSession(timed.Id, 60));
                Assert.True(_store.SoftCloseSession(untimed.Id, 60));
                break;
            case "batch":
                Assert.True(_store.SoftCloseSessions(new[] { timed.Id, untimed.Id }, 60));
                break;
            default:
                Assert.True(_store.SoftRemoveWorkspace(workspaceId, 60));
                break;
        }
        Assert.Null(timed.Info);
        Assert.Equal("kept", untimed.Info?.Source);
        Assert.True(Assert.Single(_timers).Disposed);

        while (_store.UndoPendingClose()) { }
        Assert.Null(_store.SessionWithId(timed.Id)?.Info);
        Assert.Equal("kept", _store.SessionWithId(untimed.Id)?.Info?.Source);
    }

    [Fact]
    public async Task TheDefaultTimerRunsTheExpiryThroughTheHostScheduler()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/repo")!;
        var scheduled = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.PendingCloseFinalizationScheduler = action => scheduled.TrySetResult(action);

        store.SetInfo(Info("brief", 0.01), session.Id);
        var expire = await scheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(session.Info);
        expire();
        Assert.Null(session.Info);
    }
}
