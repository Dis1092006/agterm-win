using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// Ported from upstream AppStoreTintTests and the tint cases of SnapshotRoundTripTests (f3fea57b).
public class AppStoreTintTests
{
    private static AppStore MakeStore() => AppStoreFixtures.MakeStore();

    [Fact]
    public void SetTintOnASessionReportsOnlyARealChange()
    {
        var store = MakeStore();
        var work = store.AddWorkspace("work");
        var session = store.AddSession(work.Id, "/a")!;
        var red = new SidebarTint(TintColor.Red, TintParts.Icon | TintParts.Name);

        Assert.True(store.SetSessionTint(session.Id, red));
        Assert.Equal(red, session.Tint);
        Assert.False(store.SetSessionTint(session.Id, red));
        Assert.True(store.SetSessionTint(session.Id, null));
        Assert.Null(session.Tint);
        Assert.False(store.SetSessionTint(Guid.NewGuid(), red));
    }

    [Fact]
    public void SetTintSchedulesTreeChangedAndSaves()
    {
        var dir = Directory.CreateTempSubdirectory("agterm-tint-").FullName;
        try
        {
            var events = new List<ControlEventDraft>();
            var store = new AppStore(persistence: new PersistenceStore(dir), controlEventSink: events.Add,
                stateDirectory: dir);
            var work = store.AddWorkspace("work");
            var before = events.Count(e => e.Kind == ControlEventKind.TreeChanged);

            store.SetWorkspaceTint(work.Id, new SidebarTint(TintColor.Red));

            Assert.Equal(before + 1, events.Count(e => e.Kind == ControlEventKind.TreeChanged));
            Assert.Equal(new SidebarTint(TintColor.Red), Assert.Single(new PersistenceStore(dir).Load().Workspaces).Tint);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SetTintOnAWorkspaceLeavesItsSessionsUntinted()
    {
        var store = MakeStore();
        var work = store.AddWorkspace("work");
        var session = store.AddSession(work.Id, "/a")!;

        Assert.True(store.SetWorkspaceTint(work.Id, new SidebarTint(TintColor.Red)));
        Assert.Equal(new SidebarTint(TintColor.Red), store.Workspaces.Single(w => w.Id == work.Id).Tint);
        Assert.Null(session.Tint);
        Assert.False(store.SetWorkspaceTint(work.Id, new SidebarTint(TintColor.Red)));
        Assert.False(store.SetWorkspaceTint(Guid.NewGuid(), new SidebarTint(TintColor.Red)));
    }

    [Fact]
    public void BatchColorKeepsEachRowsPlacementAndStartsUntintedRowsOnTheIcon()
    {
        var store = MakeStore();
        var work = store.AddWorkspace("work");
        var named = store.AddSession(work.Id, "/a")!;
        var plain = store.AddSession(work.Id, "/b")!;
        store.SetSessionTint(named.Id, new SidebarTint(TintColor.Green, TintParts.Name));

        store.SetTintColor(TintColor.Red, [named.Id, plain.Id]);

        Assert.Equal(new SidebarTint(TintColor.Red, TintParts.Name), named.Tint);
        Assert.Equal(new SidebarTint(TintColor.Red, TintParts.Icon), plain.Tint);

        store.SetTintColor(null, [named.Id, plain.Id]);
        Assert.Null(named.Tint);
        Assert.Null(plain.Tint);
    }

    [Fact]
    public void BatchPartTogglesOnePartKeepsEachRowsColorAndSkipsUntintedRows()
    {
        var store = MakeStore();
        var work = store.AddWorkspace("work");
        var red = store.AddSession(work.Id, "/a")!;
        var blue = store.AddSession(work.Id, "/b")!;
        var plain = store.AddSession(work.Id, "/c")!;
        store.SetSessionTint(red.Id, new SidebarTint(TintColor.Red));
        store.SetSessionTint(blue.Id, new SidebarTint(TintColor.Blue, TintParts.Name));

        store.SetTintPart(TintParts.Flag, true, [red.Id, blue.Id, plain.Id]);

        Assert.Equal(new SidebarTint(TintColor.Red, TintParts.Icon | TintParts.Flag), red.Tint);
        Assert.Equal(new SidebarTint(TintColor.Blue, TintParts.Name | TintParts.Flag), blue.Tint);
        Assert.Null(plain.Tint);

        store.SetTintPart(TintParts.Icon, false, [red.Id, blue.Id]);

        Assert.Equal(new SidebarTint(TintColor.Red, TintParts.Flag), red.Tint);
        Assert.Equal(new SidebarTint(TintColor.Blue, TintParts.Name | TintParts.Flag), blue.Tint);

        store.SetTintPart(TintParts.Flag, false, [red.Id, blue.Id]);

        Assert.Null(red.Tint);
        Assert.Equal(new SidebarTint(TintColor.Blue, TintParts.Name), blue.Tint);
    }

    // Windows-only: a workspace row has no flag, so its tint never keeps one, from a call or a hand-edited snapshot.
    [Fact]
    public void AWorkspaceTintNeverKeepsTheFlag()
    {
        var (store, _, persistence) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var work = store.AddWorkspace("work");
        store.SetWorkspaceTint(work.Id, new SidebarTint(TintColor.Gray, TintParts.Name | TintParts.Flag));

        Assert.Equal(new SidebarTint(TintColor.Gray, TintParts.Name), store.Workspaces.Single(w => w.Id == work.Id).Tint);
        Assert.False(store.SetWorkspaceTint(work.Id, new SidebarTint(TintColor.Gray, TintParts.Name | TintParts.Flag)));

        var snapshot = persistence.Load();
        var edited = snapshot with
        {
            Workspaces = snapshot.Workspaces
                .Select(w => w.Id == work.Id ? w with { Tint = new SidebarTint(TintColor.Gray, TintParts.Flag) } : w)
                .ToArray(),
        };
        var reloaded = new AppStore(persistence: persistence);
        reloaded.Restore(edited);

        Assert.Equal(new SidebarTint(TintColor.Gray), reloaded.Workspaces.Single(w => w.Id == work.Id).Tint);
    }

    [Fact]
    public void CreationTintIsSetBeforeTheFirstSave()
    {
        var (store, _, persistence) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var work = store.AddWorkspace("work", tint: new SidebarTint(TintColor.Orange, TintParts.Name));
        var session = store.AddSession(work.Id, "/a", tint: new SidebarTint(TintColor.Red))!;

        var saved = persistence.Load();
        var workspace = saved.Workspaces.Single(w => w.Id == work.Id);
        Assert.Equal(new SidebarTint(TintColor.Orange, TintParts.Name), workspace.Tint);
        Assert.Equal(new SidebarTint(TintColor.Red), workspace.Sessions.Single(s => s.Id == session.Id).Tint);
    }

    [Fact]
    public void TintsSurviveASaveAndReload()
    {
        var (store, _, persistence) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var work = store.AddWorkspace("work");
        var session = store.AddSession(work.Id, "/a")!;
        store.SetSessionTint(session.Id, new SidebarTint(TintColor.Purple, TintParts.Icon | TintParts.Name));
        store.SetWorkspaceTint(work.Id, new SidebarTint(TintColor.Gray, TintParts.Name));

        var reloaded = new AppStore(persistence: persistence);
        reloaded.Restore(persistence.Load());

        Assert.Equal(new SidebarTint(TintColor.Purple, TintParts.Icon | TintParts.Name),
            reloaded.SessionWithId(session.Id)?.Tint);
        Assert.Equal(new SidebarTint(TintColor.Gray, TintParts.Name),
            reloaded.Workspaces.Single(w => w.Id == work.Id).Tint);
    }

    [Fact]
    public void DuplicateDoesNotCopyTheTint()
    {
        var store = MakeStore();
        var work = store.AddWorkspace("work");
        var session = store.AddSession(work.Id, "/a")!;
        store.SetSessionTint(session.Id, new SidebarTint(TintColor.Red));

        var copy = store.DuplicateSession(session.Id)!;

        Assert.Null(copy.Tint);
    }

    [Fact]
    public void UndoingASessionCloseIntoARemovedWorkspaceRebuildsItWithItsTint()
    {
        var (store, _, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var keep = store.AddWorkspace("keep");
        var work = store.AddWorkspace("work", tint: new SidebarTint(TintColor.Red, TintParts.Name));
        _ = store.AddSession(keep.Id, "/a")!;
        var stray = store.AddSession(work.Id, "/b", select: false)!;

        Assert.True(store.SoftCloseSession(stray.Id, grace: 60));
        var sessionCloseId = store.PendingCloseSummary!.Id;
        Assert.True(store.SoftRemoveWorkspace(work.Id, grace: 60));

        Assert.True(store.UndoPendingClose(sessionCloseId));
        Assert.Equal(new SidebarTint(TintColor.Red, TintParts.Name), store.Workspaces.Single(w => w.Id == work.Id).Tint);
    }

    [Fact]
    public void ReopeningASessionWhoseWorkspaceWasClosedRebuildsItWithItsTint()
    {
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var keep = store.AddWorkspace("keep");
        var work = store.AddWorkspace("work", tint: new SidebarTint(TintColor.Blue));
        _ = store.AddSession(keep.Id, "/a")!;
        var stray = store.AddSession(work.Id, "/b", select: false)!;
        _ = store.AddSession(work.Id, "/c", select: false)!;

        Assert.True(store.SoftCloseSession(stray.Id, grace: 60));
        store.FinalizePendingClose(store.PendingCloseSummary!.Id);
        Assert.True(store.SoftRemoveWorkspace(work.Id, grace: 60));
        store.FinalizePendingClose(store.PendingCloseSummary!.Id);

        var item = recentClosed.Load().First(i => i.Session?.Snapshot.Id == stray.Id);
        Assert.True(store.RestoreRecentClosed(item));
        Assert.Equal(new SidebarTint(TintColor.Blue), store.Workspaces.Single(w => w.Id == work.Id).Tint);
    }

    [Fact]
    public void AWorkspaceRestoredFromRecentlyClosedKeepsItsTint()
    {
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        _ = store.AddWorkspace("keep");
        var doomed = store.AddWorkspace("doomed", tint: new SidebarTint(TintColor.Green, TintParts.Name));
        var session = store.AddSession(doomed.Id, "/a")!;
        store.SetSessionTint(session.Id, new SidebarTint(TintColor.Yellow));

        store.RemoveWorkspace(doomed.Id);
        var recent = recentClosed.Load().Single(item => item.Workspace?.Snapshot.Id == doomed.Id);
        Assert.True(store.RestoreRecentClosed(recent));

        var restored = store.Workspaces.Single(w => w.Id == doomed.Id);
        Assert.Equal(new SidebarTint(TintColor.Green, TintParts.Name), restored.Tint);
        Assert.Equal(new SidebarTint(TintColor.Yellow), restored.Sessions.Single().Tint);
    }

    [Fact]
    public void UndoingAWorkspaceDeleteKeepsItsTint()
    {
        var store = MakeStore();
        _ = store.AddWorkspace("keep");
        var doomed = store.AddWorkspace("doomed", tint: new SidebarTint(TintColor.Gray));

        Assert.True(store.SoftRemoveWorkspace(doomed.Id, grace: 60));
        Assert.True(store.UndoPendingClose());

        Assert.Equal(new SidebarTint(TintColor.Gray), store.Workspaces.Single(w => w.Id == doomed.Id).Tint);
    }

    [Fact]
    public void TreeReportsTintAndPlacementAndOmitsThemWhenUntinted()
    {
        var store = MakeStore();
        var work = store.AddWorkspace("work");
        var tinted = store.AddSession(work.Id, "/a")!;
        _ = store.AddSession(work.Id, "/b")!;
        store.SetSessionTint(tinted.Id, new SidebarTint(TintColor.Green, TintParts.Flag | TintParts.Name | TintParts.Icon));
        store.SetWorkspaceTint(work.Id, new SidebarTint(TintColor.Yellow, TintParts.Name));

        var node = store.ControlTree().Workspaces.Single(w => w.Id == work.Id.ToString("D").ToUpperInvariant());

        Assert.Equal("yellow", node.Tint);
        Assert.Equal(["name"], node.TintOn!);
        Assert.Equal("green", node.Sessions[0].Tint);
        Assert.Equal(["icon", "name", "flag"], node.Sessions[0].TintOn!);
        Assert.Null(node.Sessions[1].Tint);
        Assert.Null(node.Sessions[1].TintOn);
        store.SetWorkspaceTint(work.Id, null);
        store.SetSessionTint(tinted.Id, null);
        var bare = ControlJson.Serialize(store.ControlTree());
        Assert.DoesNotContain("\"tint\"", bare);
        Assert.DoesNotContain("\"tintOn\"", bare);
    }

    [Fact]
    public void AnUntintedTreeWritesNoTintKey()
    {
        var store = MakeStore();
        var work = store.AddWorkspace("work");
        _ = store.AddSession(work.Id, "/a")!;

        Assert.DoesNotContain("\"tint\"", ControlJson.Serialize(store.Snapshot()));
    }
}
