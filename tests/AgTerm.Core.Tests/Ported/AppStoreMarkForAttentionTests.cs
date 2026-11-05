using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Mark for Attention (upstream #763): a sidebar row action, no control command.</summary>
public sealed class AppStoreMarkForAttentionTests
{
    private static (AppStore Store, Session A, Session B, Session C) MakeStore()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        return (store, store.AddSession(work.Id, "/a")!, store.AddSession(work.Id, "/b")!, store.AddSession(work.Id, "/c")!);
    }

    [Fact]
    public void Marks_an_idle_session_completed_without_an_auto_reset()
    {
        var (store, a, _, _) = MakeStore();
        store.MarkForAttention([a.Id]);
        Assert.Equal(AgentStatus.Completed, a.AgentIndicator.Status);
        Assert.Equal(new AgentIndicator(AgentStatus.Completed), a.AgentIndicator);
        Assert.NotNull(a.StatusChangedAt);
        Assert.Contains(a, store.AttentionSessions);
    }

    [Fact]
    public void Never_overwrites_a_live_status()
    {
        var (store, a, b, c) = MakeStore();
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), a.Id);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), b.Id);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Completed, Blink: true), c.Id);
        store.MarkForAttention([a.Id, b.Id, c.Id]);
        Assert.Equal(AgentStatus.Blocked, a.AgentIndicator.Status);
        Assert.Equal(AgentStatus.Active, b.AgentIndicator.Status);
        Assert.True(c.AgentIndicator.Blink);
    }

    [Fact]
    public void Marks_only_the_idle_members_of_a_batch()
    {
        var (store, a, b, c) = MakeStore();
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), b.Id);
        store.MarkForAttention([a.Id, b.Id, c.Id]);
        Assert.Equal(AgentStatus.Completed, a.AgentIndicator.Status);
        Assert.Equal(AgentStatus.Active, b.AgentIndicator.Status);
        Assert.Equal(AgentStatus.Completed, c.AgentIndicator.Status);
    }

    [Fact]
    public void Ignores_unknown_ids_and_an_empty_batch()
    {
        var (store, a, _, _) = MakeStore();
        store.MarkForAttention([]);
        store.MarkForAttention([Guid.NewGuid()]);
        Assert.Equal(AgentStatus.Idle, a.AgentIndicator.Status);
    }

    [Fact]
    public void The_mark_survives_selection_and_makes_attention_navigation_stop()
    {
        var (store, a, b, c) = MakeStore();
        store.MarkForAttention([c.Id]);
        store.SelectSession(a.Id);
        store.NavigateSession(SessionNavigation.NextAttention);
        Assert.Equal(c.Id, store.SelectedSessionId);
        Assert.Equal(AgentStatus.Completed, c.AgentIndicator.Status);
    }

    [Fact]
    public void Clear_status_clears_the_mark()
    {
        var (store, a, _, _) = MakeStore();
        store.MarkForAttention([a.Id]);
        store.SetAgentIndicator(new AgentIndicator(), a.Id);
        Assert.Equal(AgentStatus.Idle, a.AgentIndicator.Status);
    }
}
