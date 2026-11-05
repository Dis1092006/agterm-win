using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// lastSelectedAt (upstream c002d121, 21dc9909)
public class AppStoreLastSelectedAtTests
{
    private static double Epoch(DateTimeOffset at) => at.ToUnixTimeMilliseconds() / 1000.0;

    [Fact]
    public void SelectionStampsTheSessionEnteredAndTheSessionLeft()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var a = store.AddSession(work.Id, "/repo/a")!;
        var b = store.AddSession(work.Id, "/repo/b", select: false)!;
        var never = store.AddSession(work.Id, "/repo/c", select: false)!;
        a.LastSelectedAt = DateTimeOffset.FromUnixTimeSeconds(1);
        var before = DateTimeOffset.UtcNow;

        store.SelectSession(b.Id);

        var after = DateTimeOffset.UtcNow;
        var aStamp = a.LastSelectedAt!.Value;
        Assert.InRange(aStamp, before, after);
        Assert.Equal(aStamp, b.LastSelectedAt);
        Assert.Null(never.LastSelectedAt);
        var nodes = store.ControlTree().Workspaces[0].Sessions;
        Assert.Equal(new double?[] { Epoch(aStamp), Epoch(aStamp), null }, nodes.Select(n => n.LastSelectedAt));
        Assert.DoesNotContain("lastSelectedAt", ControlJson.Serialize(nodes[2]));
    }

    [Fact]
    public void ReselectingTheSelectedSessionKeepsTheStamp()
    {
        var store = AppStoreFixtures.MakeStore();
        var a = store.AddSession(store.AddWorkspace("work").Id, "/a")!;
        a.LastSelectedAt = DateTimeOffset.FromUnixTimeSeconds(1);

        store.SelectSession(a.Id);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1), a.LastSelectedAt);
    }

    [Fact]
    public void LastSelectedAtSurvivesARestoreThatReselectsTheSession()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/a")!;
        session.LastSelectedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var snap = store.Snapshot();
        Assert.Equal(1_700_000_000, snap.Workspaces[0].Sessions[0].LastSelectedAt);
        Assert.Equal(session.Id, snap.SelectedSessionId);

        var restored = AppStoreFixtures.MakeStore();
        restored.Restore(snap);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), restored.Workspaces[0].Sessions[0].LastSelectedAt);
    }

    [Theory]
    [InlineData(1e20)]
    [InlineData(-1e20)]
    public void AnOutOfRangeLastSelectedAtRestoresAsUnset(double stored)
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.SessionFrom(new SessionSnapshot { Id = Guid.NewGuid(), Cwd = "/a", LastSelectedAt = stored });

        Assert.Null(session.LastSelectedAt);
    }

    [Fact]
    public void BatchCloseStampsTheActiveSessionLeftWhenUndoSelectsABatchMate()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var first = store.AddSession(ws.Id, "/a", name: "one")!;
        var second = store.AddSession(ws.Id, "/b", name: "two")!;
        store.AddSession(ws.Id, "/c", name: "three");
        store.SelectSession(first.Id);
        first.LastSelectedAt = DateTimeOffset.FromUnixTimeSeconds(1);
        try
        {
            var before = DateTimeOffset.UtcNow;
            Assert.True(store.SoftCloseSessions(new[] { first.Id, second.Id }, 60));
            var after = DateTimeOffset.UtcNow;
            Assert.True(store.UndoPendingClose(selecting: second.Id));

            Assert.Equal(second.Id, store.SelectedSessionId);
            Assert.InRange(first.LastSelectedAt!.Value, before, after);
        }
        finally
        {
            store.FinalizeAllPendingCloses();
        }
    }
}
