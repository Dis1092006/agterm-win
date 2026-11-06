using AgTerm.Core.Model;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// Ported from agtermCore AppStoreFocusTests.swift (upstream 146747da, the flagged-view half): switching
/// to the flagged view and flagging or unflagging inside it keep the active session inside the visible set.
/// </summary>
public class AppStoreFlaggedReselectTests
{
    [Fact]
    public void SwitchingToTheFlaggedViewMovesTheSelectionIntoIt()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var flagged = store.AddSession(work.Id, "/a")!;
        var plain = store.AddSession(work.Id, "/b")!;
        store.SetFlag(true, flagged.Id);
        store.SelectSession(plain.Id);

        store.SetSidebarMode(SidebarMode.Flagged);
        Assert.Equal(flagged.Id, store.SelectedSessionId);
    }

    [Fact]
    public void SwitchingToTheFlaggedViewNeverDisablesTheWorkspaceFilter()
    {
        var store = AppStoreFixtures.MakeStore();
        var marked = store.AddWorkspace("marked");
        var other = store.AddWorkspace("other");
        var inSet = store.AddSession(marked.Id, "/a")!;
        var flaggedOutside = store.AddSession(other.Id, "/b", select: false)!;
        store.SetFlag(true, flaggedOutside.Id);
        store.SelectSession(inSet.Id);
        store.SetFocusedWorkspace(marked.Id);

        store.SetSidebarMode(SidebarMode.Flagged);
        Assert.Equal(flaggedOutside.Id, store.SelectedSessionId);
        Assert.True(store.FocusedWorkspaceIds.SetEquals(new[] { marked.Id }) && store.FocusEnabled);

        store.SetSidebarMode(SidebarMode.Tree);
        Assert.Equal(inSet.Id, store.SelectedSessionId);
        Assert.True(store.FocusedWorkspaceIds.SetEquals(new[] { marked.Id }) && store.FocusEnabled);
    }

    [Fact]
    public void UnflaggingTheActiveSessionInFlaggedModeMovesTheSelection()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var survivor = store.AddSession(work.Id, "/a")!;
        var active = store.AddSession(work.Id, "/b")!;
        store.SetFlag(true, survivor.Id);
        store.SetFlag(true, active.Id);
        store.SelectSession(active.Id);
        store.SetSidebarMode(SidebarMode.Flagged);
        Assert.Equal(active.Id, store.SelectedSessionId);

        store.SetFlag(false, active.Id);
        Assert.Equal(survivor.Id, store.SelectedSessionId);
    }

    [Fact]
    public void BatchUnflaggingTheActiveSessionInFlaggedModeMovesTheSelection()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var survivor = store.AddSession(work.Id, "/a")!;
        var active = store.AddSession(work.Id, "/b")!;
        var alsoUnflagged = store.AddSession(work.Id, "/c", select: false)!;
        store.SetFlag(true, new[] { survivor.Id, active.Id, alsoUnflagged.Id });
        store.SelectSession(active.Id);
        store.SetSidebarMode(SidebarMode.Flagged);

        store.SetFlag(false, new[] { active.Id, alsoUnflagged.Id });
        Assert.Equal(new[] { survivor.Id }, store.FlaggedSessions.Select(s => s.Id));
        Assert.Equal(survivor.Id, store.SelectedSessionId);
    }

    [Fact]
    public void FlaggingASessionWhileTheFlaggedViewIsEmptyMovesTheSelectionIntoIt()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var active = store.AddSession(work.Id, "/a")!;
        var other = store.AddSession(work.Id, "/b", select: false)!;
        store.SelectSession(active.Id);
        store.SetSidebarMode(SidebarMode.Flagged);
        Assert.Equal(active.Id, store.SelectedSessionId);

        store.SetFlag(true, other.Id);
        Assert.Equal(other.Id, store.SelectedSessionId);
    }

    [Fact]
    public void ClearingEveryFlagInFlaggedModeLeavesTheSelectionAlone()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var active = store.AddSession(work.Id, "/a")!;
        store.SetFlag(true, active.Id);
        store.SelectSession(active.Id);
        store.SetSidebarMode(SidebarMode.Flagged);

        store.ClearFlags();
        Assert.Empty(store.FlaggedSessions);
        Assert.Equal(active.Id, store.SelectedSessionId);
    }

    [Fact]
    public void UnflaggingInTreeModeDoesNotMoveTheSelection()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        Assert.NotNull(store.AddSession(work.Id, "/a"));
        var active = store.AddSession(work.Id, "/b")!;
        store.SetFlag(true, active.Id);
        store.SelectSession(active.Id);

        store.SetFlag(false, active.Id);
        Assert.Equal(active.Id, store.SelectedSessionId);
    }
}
