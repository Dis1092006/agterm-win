using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

// upstream WindowContentView title bar buttons: the state each one draws, read from the window's store
public sealed class TitleBarStateTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-titlebar-").FullName;
    private readonly AppStore _store;
    private readonly Guid _workspace;

    public TitleBarStateTests()
    {
        _store = new AppStore(
            persistence: new PersistenceStore(_dir),
            recentClosedStore: new RecentClosedStore(_dir),
            stateDirectory: _dir);
        _workspace = _store.AddWorkspace("main").Id;
    }

    public void Dispose()
    {
        _store.CancelPendingSave();
        Directory.Delete(_dir, recursive: true);
    }

    private Session Add(string cwd) => _store.AddSession(_workspace, cwd)!;

    [Fact]
    public void A_window_without_a_session_disables_the_session_buttons()
    {
        var state = TitleBarState.Project(_store);

        Assert.False(state.HasActiveSession);
        Assert.Equal(TitleBarSplit.None, state.Split);
        Assert.Empty(state.RecentSessions);
        Assert.Equal(TitleBarAttention.None, state.Attention);
    }

    [Fact]
    public void Recent_sessions_are_the_other_sessions_most_recent_first()
    {
        var first = Add(@"C:\a");
        var second = Add(@"C:\b");
        Add(@"C:\c");

        var recent = TitleBarState.Project(_store).RecentSessions;

        Assert.Equal(new[] { second.Id, first.Id }, recent.Select(r => r.Id));
        Assert.Equal(second.DisplayName, recent[0].Title);
        Assert.Equal($"main · {second.SubtitleDetail}", recent[0].Subtitle);
    }

    [Fact]
    public void Recent_sessions_are_capped_like_the_switcher()
    {
        for (var i = 0; i < 12; i++)
            Add($@"C:\s{i}");

        Assert.Equal(TitleBarState.MaxRecentSessions, TitleBarState.Project(_store).RecentSessions.Count);
    }

    [Fact]
    public void Recent_sessions_stay_inside_the_focused_workspace()
    {
        var inside = Add(@"C:\in");
        var other = _store.AddWorkspace("other", clearFocus: false).Id;
        _store.AddSession(other, @"C:\out");
        _store.SelectSession(_store.AddSession(_workspace, @"C:\active")!.Id);
        _store.SetFocusedWorkspace(_workspace);

        Assert.Equal(new[] { inside.Id }, TitleBarState.Project(_store).RecentSessions.Select(r => r.Id));
    }

    // upstream SessionSwitcher.begin: the MRU order inside the visible set, the current session first
    [Fact]
    public void Switcher_sessions_start_with_the_current_one()
    {
        var first = Add(@"C:\first");
        var second = Add(@"C:\second");
        var third = Add(@"C:\c");

        Assert.Equal(new[] { third.Id, second.Id, first.Id },
            TitleBarState.Project(_store).SwitcherSessions.Select(s => s.Id));
    }

    [Fact]
    public void Switcher_sessions_are_capped_like_the_recent_list()
    {
        for (var i = 0; i < 12; i++)
            Add($@"C:\s{i}");

        Assert.Equal(TitleBarState.MaxRecentSessions, TitleBarState.Project(_store).SwitcherSessions.Count);
    }

    [Fact]
    public void Scratch_follows_the_active_session()
    {
        var session = Add(@"C:\a");
        _store.ToggleScratch(session.Id);

        var state = TitleBarState.Project(_store);

        Assert.True(state.HasActiveSession);
        Assert.True(state.ScratchActive);
    }

    [Fact]
    public void Split_reports_which_panes_are_up()
    {
        var session = Add(@"C:\a");
        _store.ToggleSplit(session.Id);
        Assert.Equal(TitleBarSplit.Both, TitleBarState.Project(_store).Split);

        _store.ToggleSplit(session.Id); // hidden, the new right pane still focused
        Assert.Equal(TitleBarSplit.Right, TitleBarState.Project(_store).Split);

        session.SplitFocused = false;
        Assert.Equal(TitleBarSplit.Left, TitleBarState.Project(_store).Split);
    }

    [Fact]
    public void Attention_is_blocked_when_any_session_is_blocked()
    {
        var active = Add(@"C:\a");
        var blocked = Add(@"C:\b");
        _store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), active.Id);
        Assert.Equal(TitleBarAttention.Attention, TitleBarState.Project(_store).Attention);

        _store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), blocked.Id);
        Assert.Equal(TitleBarAttention.Blocked, TitleBarState.Project(_store).Attention);
    }
}
