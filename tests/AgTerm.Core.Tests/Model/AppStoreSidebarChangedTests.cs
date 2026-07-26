using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// AppStore.SidebarChanged — the coarse UI-invalidation event (the Observation replacement for the
/// sidebar, same precedent as SelectionChanged / SidebarVisibilityChanged). Every sidebar-relevant
/// mutator raises it; the control-event RING is untouched (flag/collapse/focus/mode deliberately
/// do NOT emit tree.changed — wire parity).
/// </summary>
public class AppStoreSidebarChangedTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-sidebar-ev-").FullName;
    private readonly List<ControlEventDraft> _ring = new();
    private readonly AppStore _store;
    private int _raised;

    public AppStoreSidebarChangedTests()
    {
        _store = new AppStore(persistence: new PersistenceStore(_dir),
                              controlEventSink: _ring.Add);
        _store.SidebarChanged += () => _raised++;
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private int Raised(Action mutate)
    {
        var before = _raised;
        mutate();
        return _raised - before;
    }

    [Fact]
    public void Structure_mutators_raise_the_event()
    {
        Workspace workspace = null!;
        Session a = null!, b = null!;
        Assert.True(Raised(() => workspace = _store.AddWorkspace("w")) > 0, "AddWorkspace");
        Assert.True(Raised(() => a = _store.AddSession(workspace.Id, "C:\\")!) > 0, "AddSession");
        Assert.True(Raised(() => b = _store.AddSession(workspace.Id, "C:\\", select: false)!) > 0,
            "AddSession --no-select");
        Assert.True(Raised(() => _store.RenameSession(a.Id, "api")) > 0, "RenameSession");
        Assert.True(Raised(() => _store.RenameWorkspace(workspace.Id, "work")) > 0, "RenameWorkspace");
        Assert.True(Raised(() => _store.MoveSession(a.Id, workspace.Id, 0)) > 0, "MoveSession");
        Assert.True(Raised(() => _store.MoveSessions(new[] { a.Id, b.Id }, workspace.Id, 0)) > 0,
            "MoveSessions");
        Assert.True(Raised(() => _store.ReorderSession(a.Id, ReorderDirection.Bottom)) > 0,
            "ReorderSession");
        var second = _store.AddWorkspace("w2", clearFocus: false);
        Assert.True(Raised(() => _store.MoveWorkspace(second.Id, 0)) > 0, "MoveWorkspace");
        Assert.True(Raised(() => _store.CloseSession(b.Id)) > 0, "CloseSession");
        Assert.True(Raised(() => _store.RemoveWorkspace(second.Id)) > 0, "RemoveWorkspace");
    }

    [Fact]
    public void Selection_flag_focus_collapse_mode_and_indicator_raise_the_event()
    {
        var workspace = _store.AddWorkspace("w");
        var session = _store.AddSession(workspace.Id, "C:\\")!;

        Assert.True(Raised(() => _store.SelectSession(session.Id)) > 0, "SelectSession");
        Assert.True(Raised(() => _store.SetFlag(true, session.Id)) > 0, "SetFlag");
        Assert.True(Raised(() => _store.SetFlag(false, new[] { session.Id })) > 0, "SetFlag batch");
        _store.SetFlag(true, session.Id);
        Assert.True(Raised(() => _store.ClearFlags()) > 0, "ClearFlags");
        Assert.True(Raised(() => _store.SetFocusedWorkspace(workspace.Id)) > 0, "SetFocusedWorkspace");
        Assert.True(Raised(() => _store.SetWorkspaceExpanded(workspace.Id, false)) > 0,
            "SetWorkspaceExpanded");
        Assert.True(Raised(() => _store.SetWorkspacesExpanded(new HashSet<Guid> { workspace.Id })) > 0,
            "SetWorkspacesExpanded");
        Assert.True(Raised(() => _store.SetSidebarMode(SidebarMode.Flagged)) > 0, "SetSidebarMode");
        Assert.True(Raised(() => _store.SetSidebarVisible(false)) > 0, "SetSidebarVisible");
        Assert.True(Raised(() => _store.SetAgentIndicator(
            new AgentIndicator(AgentStatus.Blocked), session.Id)) > 0, "SetAgentIndicator");
        session.UnseenCount = 3;
        Assert.True(Raised(() => _store.ClearUnseen(session.Id)) > 0, "ClearUnseen");
        Assert.True(Raised(_store.NotifySidebar) > 0, "NotifySidebar (host OOB hook)");
    }

    [Fact]
    public void Flag_focus_collapse_and_mode_do_not_emit_tree_changed_into_the_ring()
    {
        var workspace = _store.AddWorkspace("w");
        var session = _store.AddSession(workspace.Id, "C:\\")!;
        _ring.Clear();

        _store.SetFlag(true, session.Id);
        _store.SetFocusedWorkspace(workspace.Id);
        _store.SetWorkspaceExpanded(workspace.Id, false);
        _store.SetSidebarMode(SidebarMode.Flagged);
        _store.SetSidebarVisible(false);

        Assert.DoesNotContain(_ring, e => e.Kind == ControlEventKind.TreeChanged);
    }
}
