using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public sealed class SessionAgentTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-agent-").FullName;
    private readonly AppStore _store;

    public SessionAgentTests()
    {
        _store = new AppStore(persistence: new PersistenceStore(_dir));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class FakeSurface(string token) : ITerminalSurface, IPaneRoleMutableSurface
    {
        public string? PaneToken { get; } = token;
        public SwappablePaneRole Role { get; private set; }
        public void Teardown() { }
        public void PromoteToPrimaryPane() => Role = SwappablePaneRole.Primary;
        public void SetPaneRole(SwappablePaneRole role) => Role = role;
    }

    [Fact]
    public void Focused_agent_follows_the_focused_pane_when_the_split_surface_exists()
    {
        var session = new Session("C:\\work")
        {
            Agent = SessionAgent.Claude,
            SplitAgent = SessionAgent.Copilot,
        };

        Assert.Equal(SessionAgent.Claude, session.FocusedAgent);

        session.SplitFocused = true;
        Assert.Equal(SessionAgent.Claude, session.FocusedAgent); // no split surface yet

        session.SplitSurface = new FakeSurface("split");
        Assert.Equal(SessionAgent.Copilot, session.FocusedAgent);

        session.SplitFocused = false;
        Assert.Equal(SessionAgent.Claude, session.FocusedAgent);
    }

    [Fact]
    public void Closing_promoting_and_swapping_panes_move_the_pane_agents_with_their_surfaces()
    {
        var workspace = _store.AddWorkspace("work");
        var session = _store.AddSession(workspace.Id, "C:\\work")!;
        session.Surface = new FakeSurface("main");
        _store.ToggleSplit(session.Id);
        session.SplitSurface = new FakeSurface("split");
        session.Agent = SessionAgent.Claude;
        session.SplitAgent = SessionAgent.Codex;

        Assert.Null(_store.SwapPanes(session.Id));
        Assert.Equal(SessionAgent.Codex, session.Agent);
        Assert.Equal(SessionAgent.Claude, session.SplitAgent);

        _store.ClosePrimaryPane(session.Id);
        Assert.Equal(SessionAgent.Claude, session.Agent);
        Assert.Null(session.SplitAgent);

        _store.ToggleSplit(session.Id);
        session.SplitSurface = new FakeSurface("split-2");
        session.SplitAgent = SessionAgent.Copilot;
        _store.CloseSplit(session.Id);
        Assert.Equal(SessionAgent.Claude, session.Agent);
        Assert.Null(session.SplitAgent);
    }

    [Fact]
    public void Sidebar_projection_carries_the_focused_agent_and_whether_the_name_is_custom()
    {
        var workspace = _store.AddWorkspace("work");
        var session = _store.AddSession(workspace.Id, "C:\\work")!;
        session.OscTitle = "✳ Fix tests";
        var shapeBefore = SidebarProjection.ShapeKey(_store);
        var before = SidebarProjection.Project(_store).Single(r => r.Kind == SidebarRowKind.Session);

        Assert.Null(before.Agent);
        Assert.False(before.HasCustomName);

        session.Agent = SessionAgent.Claude;
        var withAgent = SidebarProjection.Project(_store).Single(r => r.Kind == SidebarRowKind.Session);
        Assert.Equal(SessionAgent.Claude, withAgent.Agent);
        Assert.False(withAgent.HasCustomName);
        Assert.NotEqual(before, withAgent);
        Assert.Equal(shapeBefore, SidebarProjection.ShapeKey(_store));

        _store.RenameSession(session.Id, "My task");
        var renamed = SidebarProjection.Project(_store).Single(r => r.Kind == SidebarRowKind.Session);
        Assert.True(renamed.HasCustomName);
    }
}
