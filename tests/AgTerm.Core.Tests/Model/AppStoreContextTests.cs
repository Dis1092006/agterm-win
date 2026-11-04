using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// session.context (upstream 73b63b71): what a session is FOR, set over the control API, shown in the
// title bar, kept across a restart, never copied by session.duplicate
public class AppStoreContextTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-session-context-").FullName;
    private readonly List<ControlEventDraft> _events = new();
    private readonly AppStore _store;
    private readonly Session _session;

    public AppStoreContextTests()
    {
        _store = new AppStore(persistence: new PersistenceStore(_dir),
                              controlEventSink: _events.Add,
                              stateDirectory: _dir);
        _session = _store.AddSession(_store.AddWorkspace("work").Id, "/repo")!;
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private int TreeChanges => _events.Count(e => e.Kind == ControlEventKind.TreeChanged);

    [Fact]
    public void SetContextReportsAChangeAndSchedulesTreeChangedOnlyThen()
    {
        var before = TreeChanges;

        Assert.True(_store.SetContext("PR #517", _session.Id));
        Assert.Equal(before + 1, TreeChanges);

        Assert.False(_store.SetContext("PR #517", _session.Id));
        Assert.Equal(before + 1, TreeChanges);

        Assert.True(_store.SetContext(null, _session.Id));
        Assert.Equal(before + 2, TreeChanges);

        Assert.False(_store.SetContext(null, _session.Id));
        Assert.Equal(before + 2, TreeChanges);
        Assert.Null(_session.Context);
    }

    [Fact]
    public void SetContextForAnUnknownSessionChangesNothing()
    {
        var before = TreeChanges;

        Assert.False(_store.SetContext("PR #517", Guid.NewGuid()));

        Assert.Equal(before, TreeChanges);
        Assert.Null(_session.Context);
    }

    [Fact]
    public void SetContextWritesTheSavedSnapshot()
    {
        _store.SetContext("PR #517", _session.Id);

        var saved = Assert.Single(Assert.Single(new PersistenceStore(_dir).Load().Workspaces).Sessions);
        Assert.Equal("PR #517", saved.Context);
    }

    [Fact]
    public void ContextRoundTripsThroughSnapshotAndRestore()
    {
        _session.Context = "PR #517: restore reap ordering";
        var restored = AppStoreFixtures.MakeStore();

        restored.Restore(Assert.IsType<Snapshot>(
            ControlJson.Deserialize<Snapshot>(ControlJson.Serialize(_store.Snapshot()))));

        Assert.Equal("PR #517: restore reap ordering",
            Assert.Single(Assert.Single(restored.Workspaces).Sessions).Context);
    }

    [Fact]
    public void ADuplicatedSessionDoesNotInheritContext()
    {
        _session.Context = "PR #517";

        var copy = _store.DuplicateSession(_session.Id);

        Assert.NotNull(copy);
        Assert.Null(copy!.Context);
        Assert.Equal("PR #517", _session.Context);
    }
}
