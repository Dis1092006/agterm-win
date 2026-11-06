using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

/// <summary>Ported from agtermCore AppStoreFlaggedOrderTests.swift (upstream 8088e146).</summary>
public sealed class AppStoreFlaggedOrderTests
{
    private static List<Session> Sessions(AppStore store, string[] names, string workspace = "work")
    {
        var ws = store.AddWorkspace(workspace);
        return names.Select(name => store.AddSession(ws.Id, "/" + name)!).ToList();
    }

    private static List<Guid> Order(AppStore store) => store.OrderedFlaggedSessions.Select(s => s.Id).ToList();

    private static (AppStore Store, List<Session> Sessions) Flagged(string[] names, int[] flagOrder)
    {
        var store = AppStoreFixtures.MakeStore();
        var all = Sessions(store, names);
        foreach (var index in flagOrder) store.SetFlag(true, all[index].Id);
        return (store, all);
    }

    [Fact]
    public void FlaggingAppendsAndUnflaggingRemoves()
    {
        var (store, s) = Flagged(["a", "b", "c"], [0, 2, 1]);
        Assert.Equal([s[0].Id, s[2].Id, s[1].Id], store.FlaggedOrder);
        Assert.Equal([s[0].Id, s[2].Id, s[1].Id], Order(store));
        Assert.Equal([s[0].Id, s[1].Id, s[2].Id], store.FlaggedSessions.Select(x => x.Id));

        store.SetFlag(false, s[2].Id);

        Assert.Equal([s[0].Id, s[1].Id], store.FlaggedOrder);
    }

    [Fact]
    public void FlagAllKeepsArrangedPositionsAndAppendsTheRestInTreeOrder()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var s = new[] { "a", "b", "c", "d" }.Select(n => store.AddSession(ws.Id, "/" + n)!).ToList();
        store.SetFlag(true, s[2].Id);
        store.SetFlag(true, s[0].Id);

        store.ApplyWorkspaceFlag(ControlToggleMode.On, ws.Id);

        Assert.Equal([s[2].Id, s[0].Id, s[1].Id, s[3].Id], store.FlaggedOrder);
    }

    [Fact]
    public void FlaggingAnAlreadyFlaggedSessionChangesNothing()
    {
        var (store, s) = Flagged(["a", "b"], [1, 0]);

        store.SetFlag(true, s[1].Id);
        store.SetFlag(true, [s[0].Id, s[1].Id]);

        Assert.Equal([s[1].Id, s[0].Id], store.FlaggedOrder);
    }

    [Fact]
    public void ClearFlaggedEmptiesTheOrder()
    {
        var (store, _) = Flagged(["a", "b"], [0, 1]);

        store.ClearFlags();

        Assert.Empty(store.FlaggedOrder);
        Assert.Empty(store.OrderedFlaggedSessions);
    }

    [Theory]
    [InlineData(new[] { 3 }, FlaggedOrderPlacement.Before, 0, new[] { 3, 0, 1, 2 })]
    [InlineData(new[] { 0 }, FlaggedOrderPlacement.After, 2, new[] { 1, 2, 0, 3 })]
    [InlineData(new[] { 2, 0 }, FlaggedOrderPlacement.After, 3, new[] { 1, 3, 0, 2 })]
    [InlineData(new[] { 1, 2 }, FlaggedOrderPlacement.Before, 0, new[] { 1, 2, 0, 3 })]
    public void MoveRearrangesTheBlockInItsCurrentOrder(int[] moving, FlaggedOrderPlacement placement, int anchor,
                                                        int[] want)
    {
        var (store, s) = Flagged(["a", "b", "c", "d"], [0, 1, 2, 3]);

        Assert.True(store.MoveFlagged(moving.Select(i => s[i].Id).ToList(), placement, s[anchor].Id));

        Assert.Equal(want.Select(i => s[i].Id), Order(store));
    }

    [Fact]
    public void MoveThatLeavesTheOrderUnchangedIsANoOp()
    {
        var (store, s) = Flagged(["a", "b", "c"], [0, 1, 2]);

        Assert.False(store.MoveFlagged([s[1].Id], FlaggedOrderPlacement.After, s[0].Id));
        Assert.False(store.MoveFlagged([s[0].Id], FlaggedOrderPlacement.Before, s[1].Id));
        Assert.Equal(s.Select(x => x.Id), Order(store));
    }

    [Fact]
    public void MoveRefusesTheAnchorInTheBlockAndIdsOutsideTheFlaggedSet()
    {
        var store = AppStoreFixtures.MakeStore();
        var s = Sessions(store, ["a", "b", "c"]);
        store.SetFlag(true, s[0].Id);
        store.SetFlag(true, s[1].Id);

        Assert.False(store.MoveFlagged([s[0].Id], FlaggedOrderPlacement.Before, s[0].Id));
        Assert.False(store.MoveFlagged([s[2].Id], FlaggedOrderPlacement.Before, s[0].Id));
        Assert.False(store.MoveFlagged([s[0].Id], FlaggedOrderPlacement.After, s[2].Id));
        Assert.False(store.MoveFlagged([Guid.NewGuid()], FlaggedOrderPlacement.After, s[1].Id));
        Assert.False(store.MoveFlagged([], FlaggedOrderPlacement.After, s[1].Id));
        Assert.Equal([s[0].Id, s[1].Id], Order(store));
    }

    [Fact]
    public void MoveSignalsATreeChangeOnlyWhenTheOrderChanges()
    {
        var kinds = new List<ControlEventKind>();
        var dir = Path.Combine(Path.GetTempPath(), "agterm-tests-" + Guid.NewGuid().ToString("D"));
        var store = new AppStore(persistence: new PersistenceStore(dir), controlEventSink: e => kinds.Add(e.Kind),
                                 stateDirectory: dir);
        var s = Sessions(store, ["a", "b"]);
        store.SetFlag(true, s[0].Id);
        store.SetFlag(true, s[1].Id);
        kinds.Clear();

        store.MoveFlagged([s[0].Id], FlaggedOrderPlacement.Before, s[1].Id);
        Assert.Empty(kinds);

        store.MoveFlagged([s[1].Id], FlaggedOrderPlacement.Before, s[0].Id);
        Assert.Equal([ControlEventKind.TreeChanged], kinds);
    }

    [Fact]
    public void MoveKeepsAParkedPendingCloseInItsSlotSoUndoReturnsItInPlace()
    {
        var (store, s) = Flagged(["a", "b", "c"], [0, 1, 2]);
        try
        {
            Assert.True(store.SoftCloseSession(s[1].Id, 60));
            Assert.Equal([s[0].Id, s[2].Id], Order(store));

            Assert.True(store.MoveFlagged([s[2].Id], FlaggedOrderPlacement.Before, s[0].Id));
            Assert.Equal([s[2].Id, s[1].Id, s[0].Id], store.FlaggedOrder);

            Assert.True(store.UndoPendingClose());
            Assert.Equal([s[2].Id, s[1].Id, s[0].Id], Order(store));
        }
        finally
        {
            store.FinalizeAllPendingCloses();
        }
    }

    [Fact]
    public void FinalizingAPendingCloseDropsItFromTheOrder()
    {
        var (store, s) = Flagged(["a", "b"], [0, 1]);
        Assert.True(store.SoftCloseSession(s[0].Id, 60));

        store.FinalizeAllPendingCloses();

        Assert.Equal([s[1].Id], store.FlaggedOrder);
    }

    [Fact]
    public void UndoAfterClearFlaggedAppendsTheReturningSession()
    {
        var (store, s) = Flagged(["a", "b", "c"], [0, 1]);
        try
        {
            Assert.True(store.SoftCloseSession(s[0].Id, 60));
            store.ClearFlags();
            store.SetFlag(true, s[2].Id);

            Assert.True(store.UndoPendingClose());

            Assert.Equal([s[2].Id, s[0].Id], Order(store));
        }
        finally
        {
            store.FinalizeAllPendingCloses();
        }
    }

    [Fact]
    public void ReopenedSessionsAppendInArrivalOrderNotTreeOrder()
    {
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var s = Sessions(store, ["a", "b", "c"]);
        foreach (var session in s) store.SetFlag(true, session.Id);
        store.CloseSession(s[0].Id);
        store.CloseSession(s[1].Id);
        Assert.Equal([s[2].Id], store.FlaggedOrder);

        foreach (var id in new[] { s[1].Id, s[0].Id })
        {
            var item = recentClosed.Load().First(i => i.Session?.Snapshot.Id == id);
            Assert.True(store.RestoreRecentClosed(item));
        }

        Assert.Equal(s.Select(x => x.Id), store.Workspaces.SelectMany(w => w.Sessions).Select(x => x.Id));
        Assert.Equal([s[2].Id, s[1].Id, s[0].Id], Order(store));
    }

    [Fact]
    public void AFlagAfterAReopenLandsAfterTheReopenedSession()
    {
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var s = Sessions(store, ["b", "c", "d"]);
        store.SetFlag(true, s[0].Id);
        store.SetFlag(true, s[1].Id);
        store.CloseSession(s[0].Id);
        var item = recentClosed.Load().First(i => i.Session?.Snapshot.Id == s[0].Id);
        Assert.True(store.RestoreRecentClosed(item));

        store.SetFlag(true, s[2].Id);

        Assert.Equal([s[1].Id, s[0].Id, s[2].Id], Order(store));
    }

    [Fact]
    public void HardClosedThenReopenedSessionAppends()
    {
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var s = Sessions(store, ["a", "b", "c"]);
        foreach (var session in s) store.SetFlag(true, session.Id);
        store.CloseSession(s[1].Id);
        Assert.Equal([s[0].Id, s[2].Id], store.FlaggedOrder);

        var item = recentClosed.Load().First(i => i.Session?.Snapshot.Id == s[1].Id);
        Assert.True(store.RestoreRecentClosed(item));

        Assert.Equal([s[0].Id, s[2].Id, s[1].Id], Order(store));
    }

    [Fact]
    public void DeletedThenReopenedWorkspaceAppendsItsFlaggedSessions()
    {
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var kept = Sessions(store, ["a"], "kept");
        var gone = Sessions(store, ["x", "y"], "gone");
        store.SetFlag(true, gone[0].Id);
        store.SetFlag(true, kept[0].Id);
        store.SetFlag(true, gone[1].Id);
        var goneId = store.WorkspaceForSession(gone[0].Id)!.Id;

        store.RemoveWorkspace(goneId);
        Assert.Equal([kept[0].Id], store.FlaggedOrder);

        var item = recentClosed.Load().First(i => i.Kind == RecentClosedKind.Workspace);
        Assert.True(store.RestoreRecentClosed(item));

        Assert.Equal([kept[0].Id, gone[0].Id, gone[1].Id], Order(store));
    }

    [Fact]
    public void ReopeningIntoAnotherWindowAppendsToThatWindowsOrder()
    {
        var (first, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var dir = Path.Combine(Path.GetTempPath(), "agterm-tests-" + Guid.NewGuid().ToString("D"));
        var second = new AppStore(persistence: new PersistenceStore(dir), recentClosedStore: recentClosed,
                                  stateDirectory: dir);
        var moved = Sessions(first, ["a"]);
        var resident = Sessions(second, ["z"]);
        first.SetFlag(true, moved[0].Id);
        second.SetFlag(true, resident[0].Id);
        first.CloseSession(moved[0].Id);

        var item = recentClosed.Load().First(i => i.Session?.Snapshot.Id == moved[0].Id);
        Assert.True(second.RestoreRecentClosed(item));

        Assert.Equal([resident[0].Id, moved[0].Id], Order(second));
    }

    [Fact]
    public void SavedOrderSurvivesASnapshotRoundTrip()
    {
        var (store, s) = Flagged(["a", "b", "c"], [2, 0]);

        var json = ControlJson.Serialize(store.Snapshot());
        var decoded = ControlJson.Deserialize<Snapshot>(json)!;
        var restored = AppStoreFixtures.MakeStore();
        restored.Restore(decoded);

        Assert.Equal([s[2].Id, s[0].Id], decoded.FlaggedOrder);
        Assert.Equal([s[2].Id, s[0].Id], restored.OrderedFlaggedSessions.Select(x => x.Id));
    }

    [Fact]
    public void SnapshotWithoutTheOrderRestoresInTreeOrder()
    {
        var (store, s) = Flagged(["a", "b", "c"], [2, 0]);
        var snapshot = store.Snapshot() with { FlaggedOrder = null };

        var restored = AppStoreFixtures.MakeStore();
        restored.Restore(snapshot);

        Assert.Equal([s[0].Id, s[2].Id], restored.FlaggedOrder);
    }

    [Fact]
    public void RestoreDropsUnknownUnflaggedAndRepeatedIdsAndAppendsMissingFlaggedOnes()
    {
        var (store, s) = Flagged(["a", "b", "c", "d"], [3, 0, 2]);
        var snapshot = store.Snapshot() with
        {
            FlaggedOrder = [s[2].Id, Guid.NewGuid(), s[1].Id, s[2].Id, s[3].Id],
        };

        var restored = AppStoreFixtures.MakeStore();
        restored.Restore(snapshot);

        Assert.Equal([s[2].Id, s[3].Id, s[0].Id], restored.FlaggedOrder);
    }

    [Fact]
    public void SnapshotOmitsTheOrderWhenNothingIsFlagged()
    {
        var store = AppStoreFixtures.MakeStore();
        _ = Sessions(store, ["a"]);

        Assert.Null(store.Snapshot().FlaggedOrder);
    }

    [Theory]
    [InlineData(FlaggedViewLayout.Flat, 0)]
    [InlineData(FlaggedViewLayout.Plain, 0)]
    [InlineData(FlaggedViewLayout.Tree, 2)]
    public void CloseInTheFlaggedViewWithNoRecencyPicksTheNeighbourInTheOrderShown(FlaggedViewLayout layout, int want)
    {
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var store = AppStoreFixtures.MakeStore();
        store.FlaggedLayout = layout;
        store.Restore(FlaggedSnapshot(ids, ids[1], [ids[2], ids[1], ids[0], ids[3]], _ => true));
        Assert.Equal([ids[1]], store.SessionRecency.Items);

        store.CloseSession(ids[1]);

        Assert.Equal(ids[want], store.SelectedSessionId);
    }

    [Fact]
    public void SoftCloseInTheFlatFlaggedViewPicksTheSavedOrderNeighbour()
    {
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var store = AppStoreFixtures.MakeStore();
        try
        {
            store.Restore(FlaggedSnapshot(ids, ids[0], [ids[1], ids[0], ids[2]], _ => true));

            Assert.True(store.SoftCloseSession(ids[0], 60));

            Assert.Equal(ids[2], store.SelectedSessionId);
        }
        finally
        {
            store.FinalizeAllPendingCloses();
        }
    }

    [Theory]
    [InlineData(FlaggedViewLayout.Flat, new[] { 3, 0, 2 })]
    [InlineData(FlaggedViewLayout.Plain, new[] { 3, 0, 2 })]
    [InlineData(FlaggedViewLayout.Tree, new[] { 0, 2, 3 })]
    public void FlaggedNavigationFollowsTheOrderTheLayoutShows(FlaggedViewLayout layout, int[] want)
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var ids = new List<Guid>
        {
            store.AddSession(work.Id, "/a")!.Id, store.AddSession(work.Id, "/b")!.Id,
            store.AddSession(personal.Id, "/c")!.Id, store.AddSession(personal.Id, "/d")!.Id,
        };
        foreach (var index in new[] { 3, 0, 2 }) store.SetFlag(true, ids[index]);
        store.SetSidebarMode(SidebarMode.Flagged);
        store.FlaggedLayout = layout;

        Assert.Equal(want.Select(i => ids[i]), store.NavigableSessions.Select(x => x.Id));
    }

    [Theory]
    [InlineData(FlaggedViewLayout.Plain, 2)]
    [InlineData(FlaggedViewLayout.Tree, 0)]
    public void RestoreIntoTheFlaggedViewReselectsTheFirstRowOfTheLayoutsOrder(FlaggedViewLayout layout, int want)
    {
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var store = AppStoreFixtures.MakeStore();
        store.FlaggedLayout = layout;

        store.Restore(FlaggedSnapshot(ids, ids[1], [ids[2], ids[0]], index => index != 1));

        Assert.Equal(ids[want], store.SelectedSessionId);
    }

    [Fact]
    public void ControlTreeReportsTheSavedFlaggedOrderAndOmitsItWhenNothingIsFlagged()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        Assert.Null(store.ControlTree().FlaggedOrder);
        Assert.DoesNotContain("\"flaggedOrder\"", ControlJson.Serialize(store.ControlTree()));

        store.SetFlag(true, b.Id);
        store.SetFlag(true, a.Id);
        var tree = store.ControlTree();

        Assert.Equal([Upper(b.Id), Upper(a.Id)], tree.FlaggedOrder);
        Assert.Equal([Upper(a.Id), Upper(b.Id)],
                     tree.Workspaces.First(w => w.Id == Upper(ws.Id)).Sessions.Select(x => x.Id));
        Assert.Contains("\"flaggedOrder\"", ControlJson.Serialize(tree));
    }

    private static string Upper(Guid id) => id.ToString("D").ToUpperInvariant();

    private static Snapshot FlaggedSnapshot(Guid[] ids, Guid selected, Guid[] order, Func<int, bool> flagged) => new()
    {
        SelectedSessionId = selected,
        Workspaces =
        [
            new WorkspaceSnapshot
            {
                Id = Guid.NewGuid(),
                Name = "work",
                Sessions = ids.Select((id, index) => new SessionSnapshot
                {
                    Id = id, Cwd = "/" + index, Flagged = flagged(index),
                }).ToArray(),
            },
        ],
        SidebarMode = SidebarMode.Flagged,
        FlaggedOrder = order,
    };
}
