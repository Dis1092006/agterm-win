using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// A theme per workspace is Windows-only (parity/windows-only.json): upstream Workspace has no
// theme, so these tests have no upstream counterpart.
public class WorkspaceThemeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-workspace-theme-").FullName;
    private readonly List<ControlEventDraft> _events = new();
    private readonly AppStore _store;
    private int _sidebarChanges;

    public WorkspaceThemeTests()
    {
        _store = new AppStore(persistence: new PersistenceStore(_dir),
                              controlEventSink: _events.Add,
                              stateDirectory: _dir);
        _store.SidebarChanged += () => _sidebarChanges++;
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private int TreeChanges => _events.Count(e => e.Kind == ControlEventKind.TreeChanged);

    [Fact]
    public void A_new_workspace_has_no_theme()
    {
        Assert.Null(_store.AddWorkspace("w").ThemeName);
    }

    [Fact]
    public void Setting_a_theme_saves_it_and_announces_the_tree_change()
    {
        var workspace = _store.AddWorkspace("w");
        var treeBefore = TreeChanges;
        var sidebarBefore = _sidebarChanges;

        _store.SetWorkspaceTheme(workspace.Id, "Nord");

        Assert.Equal("Nord", workspace.ThemeName);
        Assert.Equal(treeBefore + 1, TreeChanges);
        Assert.True(_sidebarChanges > sidebarBefore);
        Assert.Equal("Nord", Assert.Single(new PersistenceStore(_dir).Load().Workspaces).Theme);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_or_blank_name_clears_the_theme(string? name)
    {
        var workspace = _store.AddWorkspace("w");
        _store.SetWorkspaceTheme(workspace.Id, "Nord");

        _store.SetWorkspaceTheme(workspace.Id, name);

        Assert.Null(workspace.ThemeName);
        Assert.Null(Assert.Single(new PersistenceStore(_dir).Load().Workspaces).Theme);
    }

    [Fact]
    public void Setting_the_theme_a_workspace_already_has_changes_nothing()
    {
        var workspace = _store.AddWorkspace("w");
        _store.SetWorkspaceTheme(workspace.Id, "Nord");
        var treeBefore = TreeChanges;
        var sidebarBefore = _sidebarChanges;

        _store.SetWorkspaceTheme(workspace.Id, "Nord");

        Assert.Equal(treeBefore, TreeChanges);
        Assert.Equal(sidebarBefore, _sidebarChanges);
    }

    [Fact]
    public void An_unknown_workspace_is_ignored()
    {
        var workspace = _store.AddWorkspace("w");
        var treeBefore = TreeChanges;

        _store.SetWorkspaceTheme(Guid.NewGuid(), "Nord");

        Assert.Null(workspace.ThemeName);
        Assert.Equal(treeBefore, TreeChanges);
    }

    [Fact]
    public void Renaming_a_workspace_keeps_its_theme()
    {
        var workspace = _store.AddWorkspace("w");
        _store.SetWorkspaceTheme(workspace.Id, "Nord");

        _store.RenameWorkspace(workspace.Id, "renamed");

        Assert.Equal("Nord", workspace.ThemeName);
    }

    [Fact]
    public void The_theme_survives_a_snapshot_round_trip()
    {
        var workspace = _store.AddWorkspace("w");
        _store.SetWorkspaceTheme(workspace.Id, "Nord");
        var restored = AppStoreFixtures.MakeStore();

        restored.Restore(ControlJson.Deserialize<Snapshot>(ControlJson.Serialize(_store.Snapshot())));

        Assert.Equal("Nord", Assert.Single(restored.Workspaces).ThemeName);
    }

    [Fact]
    public void The_snapshot_writes_the_theme_after_collapsed_and_omits_it_when_absent()
    {
        var themed = new WorkspaceSnapshot
        {
            Id = Guid.Parse("AAAA1111-0000-0000-0000-000000000001"),
            Name = "w",
            Sessions = Array.Empty<SessionSnapshot>(),
            Collapsed = true,
            Theme = "Nord",
        };

        Assert.EndsWith("\"collapsed\":true,\"theme\":\"Nord\"}", ControlJson.Serialize(themed));
        Assert.DoesNotContain("theme", ControlJson.Serialize(themed with { Theme = null }));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("{\"name\":\"Nord\"}")]
    [InlineData("[\"Nord\"]")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("null")]
    public void A_theme_that_is_not_a_name_reads_as_none_and_the_snapshot_still_loads(string value)
    {
        var dir = Directory.CreateTempSubdirectory("agterm-workspace-theme-lossy-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "workspaces.json"), $$"""
                {"version":1,"workspaces":[{"id":"AAAA1111-0000-0000-0000-000000000001","name":"w",
                "sessions":[{"id":"5D3C67F2-4E58-4E12-9DFB-0DDD64FEC792","cwd":"C:\\"}],"theme":{{value}}}]}
                """);

            var workspace = Assert.Single(new PersistenceStore(dir).Load().Workspaces);

            Assert.Null(workspace.Theme);
            Assert.Single(workspace.Sessions);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void The_theme_comes_back_with_open_recent()
    {
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        _ = store.AddWorkspace("keep");
        var doomed = store.AddWorkspace("doomed");
        _ = store.AddSession(doomed.Id, "C:\\");
        store.SetWorkspaceTheme(doomed.Id, "Nord");

        store.RemoveWorkspace(doomed.Id);
        var recent = recentClosed.Load().Single(item => item.Workspace?.Snapshot.Id == doomed.Id);
        Assert.True(store.RestoreRecentClosed(recent));

        Assert.Equal("Nord", store.Workspaces.Single(w => w.Id == doomed.Id).ThemeName);
    }

    [Fact]
    public void Undoing_a_workspace_delete_keeps_its_theme()
    {
        var store = AppStoreFixtures.MakeStore();
        _ = store.AddWorkspace("keep");
        var doomed = store.AddWorkspace("doomed");
        store.SetWorkspaceTheme(doomed.Id, "Nord");

        Assert.True(store.SoftRemoveWorkspace(doomed.Id, grace: 60));
        Assert.True(store.UndoPendingClose());

        Assert.Equal("Nord", store.Workspaces.Single(w => w.Id == doomed.Id).ThemeName);
    }

    [Fact]
    public void Undoing_a_session_close_inside_a_deleted_workspace_rebuilds_it_with_its_theme()
    {
        var store = AppStoreFixtures.MakeStore();
        _ = store.AddWorkspace("keep");
        var doomed = store.AddWorkspace("doomed");
        var first = store.AddSession(doomed.Id, "C:\\", name: "one")!;
        _ = store.AddSession(doomed.Id, "C:\\", name: "two");
        store.SetWorkspaceTheme(doomed.Id, "Nord");

        Assert.True(store.SoftCloseSession(first.Id, grace: 60));
        var sessionClose = store.PendingCloseSummary!.Id;
        Assert.True(store.SoftRemoveWorkspace(doomed.Id, grace: 60));
        Assert.True(store.UndoPendingClose(sessionClose));

        Assert.Equal("Nord", store.Workspaces.Single(w => w.Id == doomed.Id).ThemeName);
    }

    [Fact]
    public void Open_recent_of_a_session_rebuilds_its_deleted_workspace_with_the_theme()
    {
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        _ = store.AddWorkspace("keep");
        var doomed = store.AddWorkspace("doomed");
        var first = store.AddSession(doomed.Id, "C:\\", name: "one")!;
        _ = store.AddSession(doomed.Id, "C:\\", name: "two");
        store.SetWorkspaceTheme(doomed.Id, "Nord");

        store.CloseSession(first.Id);
        store.RemoveWorkspace(doomed.Id);
        var recent = recentClosed.Load().Single(item => item.Session?.Snapshot.Id == first.Id);
        Assert.True(store.RestoreRecentClosed(recent));

        Assert.Equal("Nord", store.Workspaces.Single(w => w.Id == doomed.Id).ThemeName);
    }
}
