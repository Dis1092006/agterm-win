using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

// upstream WindowContentView title bar buttons: the state each one draws, read from the window's store
public sealed class TitleBarStateTests
{
    private readonly AppStore _store = TestStores.Scratch();
    private readonly Workspace _workspace = new("main");

    public TitleBarStateTests()
    {
        _store.Workspaces.Add(_workspace);
    }

    private Session Add(string cwd, Workspace? workspace = null)
    {
        var session = new Session(cwd);
        (workspace ?? _workspace).Sessions.Add(session);
        _store.SelectedSessionId = session.Id;
        _store.SessionRecency.Push(session.Id);
        return session;
    }

    [Fact]
    public void Subtitle_tracks_the_focused_panes_detail_not_the_workspace_name()
    {
        var session = Add(@"C:\main");
        session.CustomName = "Work";
        session.OscTitle = "remote:~/main";
        session.SplitSurface = new SubtitleSurface();
        session.SplitCwd = @"C:\split";
        session.SplitTitle = "remote:~/split";
        session.SplitFocused = true;

        var split = TitleBarState.Project(_store);
        Assert.Equal("remote:~/split", split.SubtitleDetail);
        session.SplitFocused = false;
        Assert.Equal("remote:~/main", TitleBarState.Project(_store).SubtitleDetail);
        Assert.Equal("remote:~/split", split.SubtitleDetail);
        session.OscTitle = null;
        Assert.Equal(@"C:\main", TitleBarState.Project(_store).SubtitleDetail);
    }

    [Theory]
    [InlineData("normal", 48, @"C:\main")]
    [InlineData("compact", 32, "")]
    [InlineData("hidden", 32, "")]
    [InlineData(null, 32, "")]
    public void Toolbar_height_and_subtitle_follow_the_effective_mode(string? mode, double height, string subtitle)
    {
        var settings = new AppSettings { ToolbarMode = mode };
        Assert.Equal(height, TitleBarText.ToolbarHeight(settings));
        Assert.Equal(subtitle, TitleBarText.Subtitle(@"C:\main", settings));
        Assert.Equal("", TitleBarText.Subtitle(null, settings));
    }

    private sealed class SubtitleSurface : ITerminalSurface
    {
        public string? PaneToken => "split";
        public void Teardown() { }
        public void PromoteToPrimaryPane() { }
    }

    [Fact]
    public void Projecting_recent_sessions_does_not_write_state_files()
    {
        Add(@"C:\first");
        Add(@"C:\second");

        Assert.Single(TitleBarState.Project(_store).RecentSessions);
        Assert.False(Directory.Exists(_store.StateDirectory));
    }

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
        var other = new Workspace("other");
        _store.Workspaces.Add(other);
        Add(@"C:\out", other);
        Add(@"C:\active");
        _store.FocusedWorkspaceId = _workspace.Id;

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
        session.ScratchActive = true;

        var state = TitleBarState.Project(_store);

        Assert.True(state.HasActiveSession);
        Assert.True(state.ScratchActive);
    }

    [Fact]
    public void Split_reports_which_panes_are_up()
    {
        var session = Add(@"C:\a");
        session.HasSplit = true;
        session.IsSplit = true;
        session.SplitFocused = true;
        Assert.Equal(TitleBarSplit.Both, TitleBarState.Project(_store).Split);

        session.IsSplit = false;
        Assert.Equal(TitleBarSplit.Right, TitleBarState.Project(_store).Split);

        session.SplitFocused = false;
        Assert.Equal(TitleBarSplit.Left, TitleBarState.Project(_store).Split);
    }

    [Fact]
    public void Attention_is_blocked_when_any_session_is_blocked()
    {
        var active = Add(@"C:\a");
        var blocked = Add(@"C:\b");
        active.AgentIndicator = new AgentIndicator(AgentStatus.Active);
        Assert.Equal(TitleBarAttention.Attention, TitleBarState.Project(_store).Attention);

        blocked.AgentIndicator = new AgentIndicator(AgentStatus.Blocked);
        Assert.Equal(TitleBarAttention.Blocked, TitleBarState.Project(_store).Attention);
    }
}
