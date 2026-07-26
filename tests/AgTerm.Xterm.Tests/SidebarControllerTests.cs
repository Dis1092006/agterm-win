using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// SidebarController — the UI-free control plane of the WinUI sidebar: it snapshots the row
/// projection UNDER the host gate, marshals it to the UI via the injected dispatcher, and turns
/// every sidebar gesture into the same AppStore mutations the control socket performs (the third
/// caller, upstream-style). No WebView2 involved — driven against a HeadlessHost.
/// </summary>
public sealed class SidebarControllerTests : IDisposable
{
    private readonly string _stateDir = Directory.CreateTempSubdirectory("agterm-sbc-").FullName;
    private readonly HeadlessHost _host;
    private readonly SidebarController _controller;
    private readonly List<SidebarSnapshot> _snapshots = new();

    public SidebarControllerTests()
    {
        _host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            PipeName = "agterm-sbc-" + Guid.NewGuid().ToString("N"),
            HomeDirectory = _stateDir,
        });
        // the "UI dispatcher" runs inline — tests observe snapshots synchronously
        _controller = new SidebarController(_host, runOnUi: work => work());
        _controller.SnapshotChanged += _snapshots.Add;
        _controller.Start();
    }

    public void Dispose()
    {
        _controller.Dispose();
        _host.Dispose();
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private SidebarSnapshot Latest => _snapshots[^1];

    private void Mutate(Action<AppStore> action) => _host.RunLocked(() => action(_host.Store));

    [Fact]
    public void Start_broadcasts_the_bootstrap_snapshot()
    {
        Assert.NotEmpty(_snapshots);
        var row = Assert.Single(Latest.Rows, r => r.Kind == SidebarRowKind.Session);
        Assert.Equal(Latest.SelectedSessionId, row.Id);
        Assert.True(Latest.SidebarVisible);
        Assert.Equal(SidebarMode.Tree, Latest.Mode);
        Assert.Single(Latest.Workspaces);
        Assert.False(Latest.CanRemoveWorkspace);
    }

    [Fact]
    public void Store_mutations_rebroadcast_through_the_injected_dispatcher()
    {
        var before = _snapshots.Count;
        Mutate(store => store.AddWorkspace("w2"));
        Assert.True(_snapshots.Count > before);
        Assert.Equal(2, Latest.Workspaces.Count);
        Assert.True(Latest.CanRemoveWorkspace);
    }

    [Fact]
    public void Controller_gestures_mutate_the_store_like_the_socket_would()
    {
        Guid w2 = default, background = default;
        Mutate(store =>
        {
            w2 = store.AddWorkspace("w2", clearFocus: false).Id;
            background = store.AddSession(w2, _stateDir, select: false)!.Id;
        });

        _controller.Select(background);
        Assert.Equal(background, Latest.SelectedSessionId);

        _controller.RenameSession(background, "api");
        Assert.Equal("api", Latest.Rows.Single(r => r.Id == background).Label);

        _controller.SetFlag(new[] { background }, on: true);
        Assert.True(Latest.Rows.Single(r => r.Id == background).Flagged);
        Assert.Equal(1, Latest.FlaggedCount);

        _controller.ToggleFlaggedView();
        Assert.Equal(SidebarMode.Flagged, Latest.Mode);
        Assert.Equal("api : w2", Assert.Single(Latest.Rows).Label);
        _controller.ToggleFlaggedView();

        _controller.ToggleWorkspaceCollapse(w2);
        Assert.True(Latest.Rows.Single(r => r.Id == w2).Collapsed);
        Assert.DoesNotContain(Latest.Rows, r => r.Id == background);
        _controller.ToggleWorkspaceCollapse(w2); // expand it back for the row asserts below
        Assert.False(Latest.Rows.Single(r => r.Id == w2).Collapsed);

        _controller.FocusWorkspaceToggle(w2);
        Assert.Equal(w2, Latest.FocusedWorkspaceId);
        Assert.DoesNotContain(Latest.Rows, r => r.Kind == SidebarRowKind.Workspace && r.Id != w2);
        _controller.ClearFocus();
        Assert.Null(Latest.FocusedWorkspaceId);

        _controller.ClearStatuses(new[] { background });
        Assert.Equal(AgentStatus.Idle, Latest.Rows.Single(r => r.Id == background).Indicator.Status);
    }

    [Fact]
    public void New_duplicate_close_and_delete_follow_the_upstream_paths()
    {
        var w1 = Latest.Workspaces.Single().Id;
        var initial = Latest.SelectedSessionId!.Value;

        _controller.NewSession(w1);
        var created = Latest.SelectedSessionId!.Value;
        Assert.NotEqual(initial, created);

        _controller.DuplicateSession(created);
        var duplicated = Latest.SelectedSessionId!.Value;
        Assert.NotEqual(created, duplicated);
        // the duplicate sits directly after its source, in the same workspace
        var ids = Latest.Rows.Where(r => r.Kind == SidebarRowKind.Session).Select(r => r.Id).ToList();
        Assert.Equal(ids.IndexOf(created) + 1, ids.IndexOf(duplicated));

        _controller.CloseSessions(new[] { duplicated, created });
        Assert.DoesNotContain(Latest.Rows, r => r.Id == created || r.Id == duplicated);

        Guid w2 = default;
        Mutate(store => w2 = store.AddWorkspace("w2", clearFocus: false).Id);
        _controller.DeleteWorkspace(w2);
        Assert.DoesNotContain(Latest.Workspaces, w => w.Id == w2);
    }

    [Fact]
    public void Width_and_visibility_persist_through_the_store()
    {
        _controller.SetSidebarWidth(300);
        Assert.Equal(300, Latest.SidebarWidth);
        _controller.SetSidebarVisible(false);
        Assert.False(Latest.SidebarVisible);
        // survives a restart from the same state dir
        _controller.Dispose();
        _host.RunLocked(() => _host.Store.Save());
        using var second = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            PipeName = "agterm-sbc2-" + Guid.NewGuid().ToString("N"),
            HomeDirectory = _stateDir,
        });
        Assert.Equal(300, second.Store.SidebarWidth);
        Assert.False(second.Store.SidebarVisible);
    }

    [Fact]
    public void Drop_gestures_apply_the_sidebar_drop_math()
    {
        Guid w1 = default, a = default, b = default, c = default;
        Mutate(store =>
        {
            w1 = store.Workspaces[0].Id;
            a = store.Workspaces[0].Sessions[0].Id;
            b = store.AddSession(w1, _stateDir, select: false)!.Id;
            c = store.AddSession(w1, _stateDir, select: false)!.Id;
        });

        // drag a into the slot before c (raw child index 2) → lands between b and c
        _controller.DropSessions(new[] { a },
            new SidebarDrop.SessionDropTarget.WorkspaceRow(w1, 3), rawChildIndex: 2);
        Assert.Equal(new[] { b, a, c },
            Latest.Rows.Where(r => r.Kind == SidebarRowKind.Session).Select(r => r.Id));

        Guid w2 = default;
        Mutate(store => w2 = store.AddWorkspace("w2", clearFocus: false).Id);
        var count = _snapshots.Count;
        _controller.DropWorkspace(w2, rawIndex: 2); // already last → no-op, no reorder broadcastworthy
        Assert.Equal(new[] { w1, w2 }, Latest.Workspaces.Select(w => w.Id));
        _ = count;

        _controller.DropWorkspace(w2, rawIndex: 0);
        Assert.Equal(new[] { w2, w1 }, Latest.Workspaces.Select(w => w.Id));
    }
}
