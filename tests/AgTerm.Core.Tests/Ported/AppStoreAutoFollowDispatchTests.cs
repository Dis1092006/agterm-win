using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Ported;

public sealed class AppStoreAutoFollowDispatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deferred_fire_waits_for_host_dispatch_and_rejects_disabled_or_rearmed_work(bool rearm)
    {
        var store = AppStoreFixtures.MakeStore();
        var workspace = store.AddWorkspace("w");
        var current = store.AddSession(workspace.Id, "/current")!;
        var blocked = store.AddSession(workspace.Id, "/blocked")!;
        store.SelectSession(current.Id);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), blocked.Id);
        var pending = new Queue<Action>();
        store.SetAutoFollowDispatcher(pending.Enqueue);
        try
        {
            store.SetAutoFollow(100, false);
            store.AutoFollowDebouncer.Flush();
            Assert.Equal(current.Id, store.SelectedSessionId);
            Assert.Single(pending);
            store.SetAutoFollow(rearm ? 200 : null, false);
            pending.Dequeue()();
            Assert.Equal(current.Id, store.SelectedSessionId);
            if (rearm)
            {
                store.AutoFollowDebouncer.Flush();
                pending.Dequeue()();
                Assert.Equal(blocked.Id, store.SelectedSessionId);
            }
        }
        finally { store.SetAutoFollow(null, false); }
    }
}
