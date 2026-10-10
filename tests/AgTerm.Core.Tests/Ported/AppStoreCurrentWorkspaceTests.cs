using AgTerm.Core.Model;
using Xunit;

namespace AgTerm.Core.Tests.Ported;

// Ported from agtermCore/Tests/agtermCoreTests/AppStoreCurrentWorkspaceTests.swift (upstream 0106275a),
// adapted to the pin's single focused workspace.
public class AppStoreCurrentWorkspaceTests
{
    private static (AppStore Store, Workspace Work, Session Session) StoreWithSelection()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var session = store.AddSession(work.Id, "/a")!;
        store.SelectSession(session.Id);
        return (store, work, session);
    }

    [Fact]
    public void ForegroundCreateBecomesCurrentOverTheSelectedSessionsWorkspace()
    {
        var (store, work, _) = StoreWithSelection();
        Assert.Equal(work.Id, store.CurrentWorkspaceId);
        var fresh = store.AddWorkspace("fresh");
        Assert.Equal(fresh.Id, store.CurrentWorkspaceId);
    }

    [Fact]
    public void ReselectingTheActiveSessionKeepsTheFreshWorkspaceCurrent()
    {
        var (store, _, session) = StoreWithSelection();
        var fresh = store.AddWorkspace("fresh");
        store.SelectSession(session.Id);
        Assert.Equal(fresh.Id, store.CurrentWorkspaceId);
    }

    [Fact]
    public void ChangingTheSelectionDropsTheFreshWorkspace()
    {
        var (store, work, _) = StoreWithSelection();
        var second = store.AddSession(work.Id, "/b", select: false)!;
        store.AddWorkspace("fresh");
        store.SelectSession(second.Id);
        Assert.Equal(work.Id, store.CurrentWorkspaceId);
    }

    [Fact]
    public void BackgroundCreateDoesNotBecomeCurrent()
    {
        var (store, work, _) = StoreWithSelection();
        store.AddWorkspace("background", clearFocus: false);
        Assert.Equal(work.Id, store.CurrentWorkspaceId);
    }

    [Fact]
    public void CollapsedCreateDoesNotBecomeCurrent()
    {
        var (store, work, _) = StoreWithSelection();
        store.AddWorkspace("parked", collapsed: true, holdsTarget: false);
        Assert.Equal(work.Id, store.CurrentWorkspaceId);
    }

    [Fact]
    public void SelectingAWorkspaceDropsTheFreshWorkspaceAsCurrent()
    {
        var (store, work, session) = StoreWithSelection();
        store.AddWorkspace("fresh");
        Assert.True(store.SelectWorkspace(work.Id));
        Assert.Equal(session.Id, store.SelectedSessionId);
        Assert.Equal(work.Id, store.CurrentWorkspaceId);
    }

    [Fact]
    public void SelectingAnEmptyWorkspaceStillMakesItCurrent()
    {
        var (store, _, session) = StoreWithSelection();
        var empty = store.AddWorkspace("empty", clearFocus: false);
        Assert.True(store.SelectWorkspace(empty.Id));
        Assert.Equal(session.Id, store.SelectedSessionId);
        Assert.Equal(empty.Id, store.CurrentWorkspaceId);
    }

    [Fact]
    public void SelectingAnEmptyWorkspaceHiddenByTheFocusFilterRevealsIt()
    {
        var (store, work, _) = StoreWithSelection();
        var empty = store.AddWorkspace("empty", clearFocus: false);
        store.SetFocusedWorkspace(work.Id);
        Assert.True(store.SelectWorkspace(empty.Id));
        Assert.Null(store.FocusedWorkspaceId);
        Assert.Equal(empty.Id, store.CurrentWorkspaceId);
    }

    [Fact]
    public void SelectingAnUnknownWorkspaceFails()
    {
        var (store, _, _) = StoreWithSelection();
        Assert.False(store.SelectWorkspace(Guid.NewGuid()));
    }

    [Fact]
    public void RemovingTheFreshWorkspaceFallsBackToSelection()
    {
        var (store, work, _) = StoreWithSelection();
        var fresh = store.AddWorkspace("fresh");
        store.RemoveWorkspace(fresh.Id);
        Assert.Equal(work.Id, store.CurrentWorkspaceId);
    }

    [Fact]
    public void UndoingAFreshWorkspaceCloseDoesNotReviveItAsCurrent()
    {
        var (store, work, _) = StoreWithSelection();
        var fresh = store.AddWorkspace("fresh");
        Assert.True(store.SoftRemoveWorkspace(fresh.Id, 60));
        store.UndoPendingClose();
        Assert.Contains(store.Workspaces, w => w.Id == fresh.Id);
        Assert.Equal(work.Id, store.CurrentWorkspaceId);
    }

    [Fact]
    public void FilteringOutTheFreshWorkspaceDropsItForGood()
    {
        var (store, work, _) = StoreWithSelection();
        store.AddWorkspace("fresh");
        store.SetFocusedWorkspace(work.Id);
        store.SetFocusedWorkspace(null);
        Assert.Equal(work.Id, store.CurrentWorkspaceId);
    }

    [Fact]
    public void RestoringASnapshotDropsTheFreshWorkspaceAsCurrent()
    {
        var (store, _, _) = StoreWithSelection();
        var fresh = store.AddWorkspace("fresh");
        var snapshot = store.Snapshot();
        store.Restore(snapshot);
        Assert.NotEqual(fresh.Id, store.CurrentWorkspaceId);
        Assert.Equal(store.WorkspaceForSession(store.SelectedSessionId!.Value)!.Id, store.CurrentWorkspaceId);
    }
}
