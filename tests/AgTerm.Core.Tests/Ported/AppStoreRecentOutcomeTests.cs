using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported from upstream abc1c0a4 (restore an entry into the window that still owns it): a
/// restore that treats the sessions other windows hold as taken, and reports whether it placed all.</summary>
public class AppStoreRecentOutcomeTests
{
    private static RecentClosedItem SessionEntry(SessionSnapshot snapshot, Guid workspaceId) => new()
    {
        Id = Guid.NewGuid(), Kind = RecentClosedKind.Session, Title = "t",
        Session = new RecentClosedSession
        {
            WorkspaceId = workspaceId, WorkspaceName = "w", WorkspaceIndex = 0, SessionIndex = 0,
            Snapshot = snapshot,
        },
    };

    private static RecentClosedItem WorkspaceEntry(WorkspaceSnapshot snapshot) => new()
    {
        Id = Guid.NewGuid(), Kind = RecentClosedKind.Workspace, Title = snapshot.Name,
        Workspace = new RecentClosedWorkspace { Snapshot = snapshot },
    };

    [Fact]
    public void ASessionHeldByAnotherWindowIsNotRebuiltHere()
    {
        var store = AppStoreFixtures.MakeStore();
        var held = new SessionSnapshot { Id = Guid.NewGuid(), Cwd = "/a" };

        var outcome = store.RestoreRecentClosed(
            SessionEntry(held, Guid.NewGuid()), new HashSet<Guid> { held.Id });

        Assert.False(outcome.Restored);
        Assert.Empty(store.Workspaces);
    }

    [Fact]
    public void AWorkspaceRestoreSkipsSessionsAnotherWindowHoldsAndStillCountsThemPlaced()
    {
        var store = AppStoreFixtures.MakeStore();
        var elsewhere = new SessionSnapshot { Id = Guid.NewGuid(), Cwd = "/a" };
        var here = new SessionSnapshot { Id = Guid.NewGuid(), Cwd = "/b" };
        var snapshot = new WorkspaceSnapshot
        {
            Id = Guid.NewGuid(), Name = "project", Sessions = new[] { elsewhere, here },
        };

        var outcome = store.RestoreRecentClosed(WorkspaceEntry(snapshot), new HashSet<Guid> { elsewhere.Id });

        Assert.True(outcome.Restored);
        Assert.True(outcome.Complete);
        Assert.Equal(new[] { here.Id }, store.Workspaces.Single(w => w.Id == snapshot.Id).Sessions.Select(s => s.Id));
    }

    [Fact]
    public void AWorkspaceRestoreRebuildsEverySessionNothingHoldsAndIsComplete()
    {
        var store = AppStoreFixtures.MakeStore();
        var a = new SessionSnapshot { Id = Guid.NewGuid(), Cwd = "/a" };
        var b = new SessionSnapshot { Id = Guid.NewGuid(), Cwd = "/b" };
        var snapshot = new WorkspaceSnapshot { Id = Guid.NewGuid(), Name = "project", Sessions = new[] { a, b } };

        var outcome = store.RestoreRecentClosed(WorkspaceEntry(snapshot), new HashSet<Guid>());

        Assert.True(outcome.Restored);
        Assert.True(outcome.Complete);
        Assert.Equal(2, store.Workspaces.Single(w => w.Id == snapshot.Id).Sessions.Count);
    }

    [Fact]
    public void ASessionRestoreIntoAnEmptyStoreIsRestoredAndComplete()
    {
        var store = AppStoreFixtures.MakeStore();
        var snapshot = new SessionSnapshot { Id = Guid.NewGuid(), Cwd = "/a" };

        var outcome = store.RestoreRecentClosed(SessionEntry(snapshot, Guid.NewGuid()), new HashSet<Guid>());

        Assert.True(outcome.Restored);
        Assert.True(outcome.Complete);
        Assert.NotNull(store.SessionWithId(snapshot.Id));
    }

    [Fact]
    public void ASessionAlreadyLiveInThisStoreIsSelectedNotDuplicated()
    {
        var store = AppStoreFixtures.MakeStore();
        var workspace = store.AddWorkspace("w");
        var live = store.AddSession(workspace.Id, "/a")!;
        var snapshot = store.SessionSnapshot(live);

        var outcome = store.RestoreRecentClosed(SessionEntry(snapshot, workspace.Id), new HashSet<Guid> { live.Id });

        Assert.True(outcome.Restored);
        Assert.True(outcome.Complete);
        Assert.Equal(1, store.Workspaces.SelectMany(w => w.Sessions).Count(s => s.Id == live.Id));
    }

    [Fact]
    public void AnOverlappingSessionInAnotherWorkspaceDoesNotStopTheWorkspaceRebuild()
    {
        var store = AppStoreFixtures.MakeStore();
        var other = store.AddWorkspace("other");
        var live = store.AddSession(other.Id, "/a")!;
        var missing = new SessionSnapshot { Id = Guid.NewGuid(), Cwd = "/b" };
        var snapshot = new WorkspaceSnapshot
        {
            Id = Guid.NewGuid(), Name = "gone", Sessions = new[] { store.SessionSnapshot(live), missing },
        };

        var outcome = store.RestoreRecentClosed(WorkspaceEntry(snapshot), new HashSet<Guid>());

        Assert.True(outcome.Restored);
        Assert.True(outcome.Complete);
        Assert.NotNull(store.SessionWithId(missing.Id));
        Assert.Equal(1, store.Workspaces.SelectMany(w => w.Sessions).Count(s => s.Id == live.Id));
    }
}
