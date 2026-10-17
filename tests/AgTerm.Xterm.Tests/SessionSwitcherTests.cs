namespace AgTerm.Xterm.Tests;

// upstream SessionSwitcher: hold Ctrl and press Tab to walk a frozen MRU list (Shift+Tab reverses),
// release Ctrl to commit the highlighted session, Esc to abandon
public sealed class SessionSwitcherTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

    private static IReadOnlyList<TitleBarRecentSession> Sessions(params Guid[] ids) =>
        ids.Select(id => new TitleBarRecentSession(id, id.ToString("N")[..4], "ws · cwd")).ToList();

    private readonly SessionSwitcher _switcher = new();

    [Fact]
    public void Fewer_than_two_sessions_do_not_begin()
    {
        _switcher.Tab(reverse: false, canSwitch: true, () => Sessions(A));

        Assert.False(_switcher.IsActive);
    }

    [Fact]
    public void The_first_tab_highlights_the_previous_session()
    {
        _switcher.Tab(reverse: false, canSwitch: true, () => Sessions(A, B, C));

        Assert.True(_switcher.IsActive);
        Assert.Equal(1, _switcher.Index);
        Assert.Equal(new[] { A, B, C }, _switcher.Candidates.Select(c => c.Id));
    }

    [Fact]
    public void A_reverse_tab_also_begins_at_the_previous_session()
    {
        _switcher.Tab(reverse: true, canSwitch: true, () => Sessions(A, B, C));

        Assert.Equal(1, _switcher.Index);
    }

    [Fact]
    public void Tab_walks_forward_and_shift_tab_back_both_wrapping()
    {
        _switcher.Tab(false, true, () => Sessions(A, B, C));
        _switcher.Tab(false, true, () => throw new InvalidOperationException("the list is frozen"));
        Assert.Equal(2, _switcher.Index);
        _switcher.Tab(false, true, () => Sessions());
        Assert.Equal(0, _switcher.Index);
        _switcher.Tab(true, true, () => Sessions());
        Assert.Equal(2, _switcher.Index);
    }

    [Fact]
    public void Releasing_ctrl_commits_the_highlighted_session_and_resets()
    {
        _switcher.Tab(false, true, () => Sessions(A, B, C));
        _switcher.Tab(false, true, () => Sessions());

        Assert.Equal(C, _switcher.ControlReleased(canSwitch: true));
        Assert.False(_switcher.IsActive);
        Assert.Empty(_switcher.Candidates);
    }

    [Fact]
    public void Releasing_ctrl_while_idle_selects_nothing()
    {
        Assert.Null(_switcher.ControlReleased(canSwitch: true));
    }

    [Fact]
    public void Escape_abandons_an_active_switch_only()
    {
        Assert.False(_switcher.Escape());

        _switcher.Tab(false, true, () => Sessions(A, B));
        Assert.True(_switcher.Escape());
        Assert.False(_switcher.IsActive);
        Assert.Null(_switcher.ControlReleased(canSwitch: true));
    }

    [Fact]
    public void A_blocked_window_resets_instead_of_switching()
    {
        _switcher.Tab(false, true, () => Sessions(A, B));
        _switcher.Tab(false, canSwitch: false, () => Sessions(A, B));
        Assert.False(_switcher.IsActive);

        _switcher.Tab(false, true, () => Sessions(A, B));
        Assert.Null(_switcher.ControlReleased(canSwitch: false));
        Assert.False(_switcher.IsActive);
    }

    [Fact]
    public void Every_state_change_is_announced()
    {
        var changes = 0;
        _switcher.Changed += () => changes++;

        _switcher.Tab(false, true, () => Sessions(A, B));
        _switcher.Tab(false, true, () => Sessions());
        _switcher.ControlReleased(canSwitch: true);

        Assert.Equal(3, changes);
    }
}
