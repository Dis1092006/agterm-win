using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// An icon per workspace is Windows-only (parity/windows-only.json): upstream Workspace has no icon.
public class AppStoreIconTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-workspace-icon-").FullName;
    private readonly List<ControlEventDraft> _events = new();
    private readonly AppStore _store;

    public AppStoreIconTests()
    {
        _store = new AppStore(persistence: new PersistenceStore(_dir),
                              controlEventSink: _events.Add,
                              stateDirectory: _dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private int TreeChanges => _events.Count(e => e.Kind == ControlEventKind.TreeChanged);

    [Fact]
    public void Setting_an_icon_stores_it_and_schedules_tree_changed()
    {
        var workspace = _store.AddWorkspace("w");
        var treeBefore = TreeChanges;

        _store.SetWorkspaceIcon(workspace.Id, "MapPin");

        Assert.Equal("MapPin", workspace.IconName);
        Assert.Equal(treeBefore + 1, TreeChanges);
        Assert.Equal("MapPin", Assert.Single(new PersistenceStore(_dir).Load().Workspaces).Icon);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_icon_clears_it(string? name)
    {
        var workspace = _store.AddWorkspace("w");
        _store.SetWorkspaceIcon(workspace.Id, "MapPin");

        _store.SetWorkspaceIcon(workspace.Id, name);

        Assert.Null(workspace.IconName);
        Assert.Null(Assert.Single(new PersistenceStore(_dir).Load().Workspaces).Icon);
    }

    [Fact]
    public void Setting_the_same_icon_changes_nothing()
    {
        var workspace = _store.AddWorkspace("w");
        _store.SetWorkspaceIcon(workspace.Id, "MapPin");
        var treeBefore = TreeChanges;

        _store.SetWorkspaceIcon(workspace.Id, "MapPin");

        Assert.Equal(treeBefore, TreeChanges);
    }

    [Fact]
    public void A_restored_snapshot_keeps_the_icon()
    {
        var workspace = _store.AddWorkspace("w");
        _store.SetWorkspaceIcon(workspace.Id, "MapPin");
        var restored = AppStoreFixtures.MakeStore();

        restored.Restore(Assert.IsType<Snapshot>(
            ControlJson.Deserialize<Snapshot>(ControlJson.Serialize(_store.Snapshot()))));

        Assert.Equal("MapPin", Assert.Single(restored.Workspaces).IconName);
    }

    [Fact]
    public void A_workspace_restored_from_recently_closed_keeps_its_icon()
    {
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        _ = store.AddWorkspace("keep");
        var doomed = store.AddWorkspace("doomed");
        _ = store.AddSession(doomed.Id, "C:\\");
        store.SetWorkspaceIcon(doomed.Id, "MapPin");

        store.RemoveWorkspace(doomed.Id);
        var recent = recentClosed.Load().Single(item => item.Workspace?.Snapshot.Id == doomed.Id);
        Assert.True(store.RestoreRecentClosed(recent));

        Assert.Equal("MapPin", store.Workspaces.Single(w => w.Id == doomed.Id).IconName);
    }

    [Fact]
    public void Undoing_a_workspace_delete_keeps_its_icon()
    {
        var store = AppStoreFixtures.MakeStore();
        _ = store.AddWorkspace("keep");
        var doomed = store.AddWorkspace("doomed");
        store.SetWorkspaceIcon(doomed.Id, "MapPin");

        Assert.True(store.SoftRemoveWorkspace(doomed.Id, grace: 60));
        Assert.True(store.UndoPendingClose());

        Assert.Equal("MapPin", store.Workspaces.Single(w => w.Id == doomed.Id).IconName);
    }
}
