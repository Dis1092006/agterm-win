using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// Ported from agtermCore AppStoreFocusTests.swift (upstream 6722755a, focus as a set of workspaces):
/// the focus mutators, the VisibleWorkspaces projection they drive, the sidebar-selection prune that
/// follows it, and the ControlTree focus read-back. Upstream cases already ported elsewhere
/// (VisibleWorkspacesReturnsOneWhenFocused, the all-stale VisibleWorkspaces fallback, the
/// ControlTreeReportsFocusedWorkspace and WorkspaceFocusPrunesRowsOutsideFocusedWorkspace pair) are
/// not repeated here.
/// </summary>
public class AppStoreFocusTests
{
    private static string Upper(Guid id) => id.ToString("D").ToUpperInvariant();

    private static Guid[] Visible(AppStore store) => store.VisibleWorkspaces.Select(w => w.Id).ToArray();

    private static void AssertFocus(AppStore store, bool enabled, params Guid[] members)
    {
        Assert.True(store.FocusedWorkspaceIds.SetEquals(members),
            $"focus set was [{string.Join(", ", store.FocusedWorkspaceIds)}]");
        Assert.Equal(enabled, store.FocusEnabled);
    }

    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "agterm-tests-" + Guid.NewGuid().ToString("D"));

    [Fact]
    public void SetFocusedWorkspaceSetsAndClears()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        AssertFocus(store, false);
        store.SetFocusedWorkspace(work.Id);
        AssertFocus(store, true, work.Id);
        store.ClearFocus();
        AssertFocus(store, false);
    }

    [Fact]
    public void ClearFocusEmptiesAWholeMultiMemberSet()
    {
        // what "Clear Focus" / Unfocus actually does in the set model: it empties the WHOLE set, not just
        // drops a member. The one-member case above cannot tell the two apart.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var side = store.AddWorkspace("side");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(personal.Id, true);
        store.SetFocusEnabled(true);

        store.ClearFocus();

        AssertFocus(store, false);
        Assert.Equal(new[] { work.Id, personal.Id, side.Id }, Visible(store)); // the whole tree is back
    }

    [Fact]
    public void MarkingAnIdThatIsNotAWorkspaceIsACleanNoOp()
    {
        // `enabled` must never hold over a set of ids no workspace matches, or workspaceFilter reads true
        // while nothing reports `focused`. Both entry points guard, so a caller that skipped its own
        // existence check cannot create the state.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var phantom = Guid.NewGuid();

        store.SetFocusMembership(phantom, true);
        Assert.Empty(store.FocusedWorkspaceIds);
        store.SetFocusEnabled(true);
        Assert.False(store.FocusEnabled); // nothing was marked, so there is still nothing to enable

        store.SetFocusedWorkspace(phantom);
        AssertFocus(store, false);

        // un-marking is NOT gated on existence: a stale id already in the set must stay removable.
        store.SetFocusMembership(work.Id, true);
        store.FocusedWorkspaceIds.Add(phantom); // as a restore of a hand-edited file could leave it
        store.SetFocusMembership(phantom, false);
        AssertFocus(store, false, work.Id);
    }

    [Fact]
    public void IsSoleFocusIsTrueOnlyForTheOneMarkedWorkspaceWithTheFilterOn()
    {
        // the row menu's label, the View-menu label and the replace-toggle all read this one predicate,
        // so its three false cases are what keep a label from disagreeing with the action it invokes.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");

        store.SetFocusMembership(work.Id, true);
        Assert.False(store.IsSoleFocus(work.Id)); // marked, but the filter is off: nothing to un-focus yet
        store.SetFocusEnabled(true);
        Assert.True(store.IsSoleFocus(work.Id));
        Assert.False(store.IsSoleFocus(personal.Id)); // an unmarked workspace is never the sole focus
        store.SetFocusMembership(personal.Id, true);
        Assert.False(store.IsSoleFocus(work.Id)); // one of two members is not the SOLE focus
    }

    [Fact]
    public void ToggleFocusedWorkspaceNarrowsAMultiMemberSetThenClears()
    {
        // the DEFAULT workspace.focus mode and the row menu's Focus/Unfocus. Against a set of two it
        // REPLACES (narrows to the target) rather than un-marking it, and only a repeat, where the target
        // is now the sole focus, clears.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(personal.Id, true);
        store.SetFocusEnabled(true);

        store.ToggleFocusedWorkspace(personal.Id);
        AssertFocus(store, true, personal.Id);
        Assert.Equal(new[] { personal.Id }, Visible(store));

        store.ToggleFocusedWorkspace(personal.Id);
        AssertFocus(store, false);

        // from an empty set it marks + applies, so one toggle is the whole zoom-in.
        store.ToggleFocusedWorkspace(work.Id);
        AssertFocus(store, true, work.Id);
        // a toggle onto a workspace marked while the filter is OFF applies rather than clears: it is not
        // the SOLE FOCUS until the filter is on, so IsSoleFocus and the toggle agree.
        store.SetFocusEnabled(false);
        store.ToggleFocusedWorkspace(work.Id);
        AssertFocus(store, true, work.Id);
    }

    [Fact]
    public void CurrentWorkspaceFocusHelpersFollowTheSelection()
    {
        // the keyless View-menu items target CurrentWorkspaceId, so their label flip and their disabled
        // state read these two, the store-side twins of IsSoleFocus.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        Assert.NotNull(store.AddSession(work.Id, "/a"));
        Assert.Equal(work.Id, store.CurrentWorkspaceId);
        Assert.False(store.IsCurrentWorkspaceSoleFocus);
        Assert.False(store.IsCurrentWorkspaceFocusMember);

        store.SetFocusMembership(work.Id, true);
        Assert.True(store.IsCurrentWorkspaceFocusMember); // marked, so "Add Workspace to Focus" is a no-op now
        Assert.False(store.IsCurrentWorkspaceSoleFocus);  // but not applied, so the label still reads "Focus"
        store.SetFocusEnabled(true);
        Assert.True(store.IsCurrentWorkspaceSoleFocus);

        // the current workspace follows the selection, and so do both helpers.
        Assert.NotNull(store.AddSession(personal.Id, "/b"));
        Assert.Equal(personal.Id, store.CurrentWorkspaceId);
        Assert.False(store.IsCurrentWorkspaceSoleFocus);
        Assert.False(store.IsCurrentWorkspaceFocusMember);
    }

    [Fact]
    public void SetFocusedWorkspaceReplacesTheWholeSet()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(personal.Id, true);
        AssertFocus(store, false, work.Id, personal.Id);
        // the single-workspace convenience REPLACES rather than adds, so the row menu's Focus still zooms.
        store.SetFocusedWorkspace(personal.Id);
        AssertFocus(store, true, personal.Id);
    }

    [Fact]
    public void SetFocusMembershipAddsAndRemoves()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        store.SetFocusMembership(work.Id, true);
        AssertFocus(store, false, work.Id); // marking never turns the filter on
        store.SetFocusMembership(personal.Id, true);
        AssertFocus(store, false, work.Id, personal.Id);
        store.SetFocusEnabled(true);
        store.SetFocusMembership(work.Id, false);
        AssertFocus(store, true, personal.Id); // the survivor keeps the filter on
    }

    [Fact]
    public void AddingToTheSetNeverTurnsTheFilterOn()
    {
        // an add MARKS and nothing else. If it enabled the filter, marking the first workspace would
        // collapse the tree to it and the rows of every workspace still to be marked would be gone. Both
        // polarities are pinned: adding while OFF leaves it off, adding while ON leaves it on.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        store.SetFocusMembership(work.Id, true);
        AssertFocus(store, false, work.Id);
        Assert.Equal(new[] { work.Id, personal.Id }, Visible(store)); // the whole tree is still on screen
        store.SetFocusEnabled(true);
        store.SetFocusMembership(personal.Id, true);
        AssertFocus(store, true, work.Id, personal.Id); // still on, not flipped
    }

    [Fact]
    public void RemovingTheLastMemberDisablesTheFilter()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusEnabled(true);
        store.SetFocusMembership(work.Id, false);
        AssertFocus(store, false); // `enabled + empty` is unrepresentable
    }

    [Fact]
    public void SetFocusMembershipOnADisabledSetKeepsItDisabledWhenRemoving()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(personal.Id, true);
        store.SetFocusEnabled(true);
        store.SetFocusEnabled(false);
        store.SetFocusMembership(work.Id, false);
        AssertFocus(store, false, personal.Id); // removing never turns it back on
    }

    [Fact]
    public void SetFocusEnabledRoundTripsWithoutLosingTheSet()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(personal.Id, true);
        store.SetFocusEnabled(true);
        store.SetFocusEnabled(false);
        AssertFocus(store, false, work.Id, personal.Id); // the set survives
        store.SetFocusEnabled(true);
        AssertFocus(store, true, work.Id, personal.Id);
    }

    [Fact]
    public void SetFocusEnabledRefusesAnEmptySet()
    {
        var store = AppStoreFixtures.MakeStore();
        store.AddWorkspace("work");
        store.SetFocusEnabled(true);
        Assert.False(store.FocusEnabled); // nothing marked, so there is nothing to filter to
    }

    [Fact]
    public void FocusSettersAreNoOpWritesWhenUnchanged()
    {
        var dir = TempDir();
        var store = new AppStore(persistence: new PersistenceStore(directory: dir));
        var ws = store.AddWorkspace("work");
        store.SetFocusedWorkspace(ws.Id);
        var file = Path.Combine(dir, "workspaces.json");
        File.Delete(file); // a no-op setter must NOT recreate the file
        store.SetFocusedWorkspace(ws.Id);            // unchanged
        store.SetFocusMembership(ws.Id, true);       // already a member, already enabled
        store.SetFocusEnabled(true);                 // already enabled
        Assert.False(File.Exists(file)); // no write happened
        AssertFocus(store, true, ws.Id); // state stable across the no-op setters
    }

    [Fact]
    public void SetFocusEnabledOnAnEmptySetIsANoOpWrite()
    {
        var dir = TempDir();
        var store = new AppStore(persistence: new PersistenceStore(directory: dir));
        store.AddWorkspace("work");
        var file = Path.Combine(dir, "workspaces.json");
        File.Delete(file);
        store.SetFocusEnabled(true); // refused, so it must not write either
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void VisibleWorkspacesReturnsAllWhenTheFilterIsOff()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        Assert.Equal(new[] { work.Id, personal.Id }, Visible(store));
        // marking then applying then switching the filter off reveals the whole tree again, set intact.
        store.SetFocusMembership(work.Id, true);
        store.SetFocusEnabled(true);
        store.SetFocusEnabled(false);
        Assert.Equal(new[] { work.Id, personal.Id }, Visible(store));
    }

    [Fact]
    public void VisibleWorkspacesReturnsTheMarkedSubsetInTreeOrder()
    {
        var store = AppStoreFixtures.MakeStore();
        var one = store.AddWorkspace("one");
        store.AddWorkspace("two");
        var three = store.AddWorkspace("three");
        store.SetFocusMembership(three.Id, true); // marked out of tree order
        store.SetFocusMembership(one.Id, true);
        store.SetFocusEnabled(true);
        Assert.Equal(new[] { one.Id, three.Id }, Visible(store)); // rendered in tree order, not mark order
    }

    [Fact]
    public void VisibleWorkspacesKeepsSurvivorsForAPartiallyStaleSet()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        store.AddWorkspace("personal");
        store.FocusedWorkspaceIds = new HashSet<Guid> { work.Id, Guid.NewGuid() }; // one member no longer in the tree
        store.FocusEnabled = true;
        Assert.Equal(new[] { work.Id }, Visible(store));
    }

    [Fact]
    public void RemovingAMemberPrunesItAndKeepsTheRestFiltered()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var doomed = store.AddWorkspace("doomed");
        store.AddWorkspace("other");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(doomed.Id, true);
        store.SetFocusEnabled(true);
        store.RemoveWorkspace(doomed.Id);
        AssertFocus(store, true, work.Id); // the survivor keeps filtering
        Assert.Equal(new[] { work.Id }, Visible(store));
    }

    [Fact]
    public void RemovingTheLastMemberWorkspaceDisablesTheFilter()
    {
        var store = AppStoreFixtures.MakeStore();
        store.AddWorkspace("other");
        var doomed = store.AddWorkspace("doomed");
        store.SetFocusMembership(doomed.Id, true);
        store.SetFocusEnabled(true);
        store.RemoveWorkspace(doomed.Id);
        AssertFocus(store, false); // nothing left to filter to
    }

    [Fact]
    public void RemovingANonMemberWorkspaceLeavesTheFilterUntouched()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var doomed = store.AddWorkspace("doomed");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(personal.Id, true);
        store.SetFocusEnabled(true);
        store.RemoveWorkspace(doomed.Id);
        AssertFocus(store, true, work.Id, personal.Id);
    }

    [Fact]
    public void UndoingASoftRemovedMemberRestoresItsMembership()
    {
        // without the re-mark a re-added member came back UNMARKED: a workspace holding sessions was
        // revealed anyway (the undo's reselect trips the cross-set auto-disable), but an EMPTY one was
        // reinserted behind the still-applied filter and its row simply never came back.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var doomed = store.AddWorkspace("doomed"); // empty on purpose: the case the undo used to lose
        Assert.NotNull(store.AddSession(work.Id, "/a"));
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(doomed.Id, true);
        store.SetFocusEnabled(true);

        Assert.True(store.SoftRemoveWorkspace(doomed.Id));
        AssertFocus(store, true, work.Id);

        Assert.True(store.UndoPendingClose());
        Assert.True(store.FocusedWorkspaceIds.SetEquals(new[] { work.Id, doomed.Id }), "an undone member must be marked again");
        Assert.True(store.FocusEnabled);
        Assert.Equal(new[] { work.Id, doomed.Id }, Visible(store)); // its row is rendered again
    }

    [Fact]
    public void UndoingTheLastMemberRestoresTheSetWithoutReApplyingTheFilter()
    {
        // removing the last member disables the filter, and the undo brings the SET back but leaves the
        // flag alone: membership belongs to the closed workspace, the flag is current window state. Both
        // restore legs (this and Reopen Closed Item) mark only, so they cannot disagree.
        var store = AppStoreFixtures.MakeStore();
        var other = store.AddWorkspace("other");
        var doomed = store.AddWorkspace("doomed");
        var spare = store.AddWorkspace("spare"); // soft-remove keeps at least one workspace
        store.SetFocusMembership(doomed.Id, true);
        store.SetFocusEnabled(true);

        Assert.True(store.SoftRemoveWorkspace(doomed.Id));
        AssertFocus(store, false);

        Assert.True(store.UndoPendingClose());
        Assert.True(store.FocusedWorkspaceIds.SetEquals(new[] { doomed.Id }), "an undone member must be marked again");
        Assert.False(store.FocusEnabled, "restoring a member must never switch the filter on");
        // so the workspace is genuinely back on screen, in its old slot, alongside everything else
        Assert.Equal(new[] { other.Id, doomed.Id, spare.Id }, Visible(store));
        store.SetFocusEnabled(true); // and one flip of the bottom-bar toggle re-applies it
        Assert.Equal(new[] { doomed.Id }, Visible(store));
    }

    [Fact]
    public void SuspendingTheFilterDuringTheGraceSurvivesTheUndo()
    {
        // the flag is CURRENT window state, so a toggle the user makes inside the grace is newer than
        // anything the pending record captured. An undo that restored the captured flag would put the
        // filter back on seconds after he deliberately switched it off.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var doomed = store.AddWorkspace("doomed");
        store.AddWorkspace("spare");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(doomed.Id, true);
        store.SetFocusEnabled(true);

        Assert.True(store.SoftRemoveWorkspace(doomed.Id));
        store.SetFocusEnabled(false); // one click on the bottom-bar toggle, still inside the grace

        Assert.True(store.UndoPendingClose());
        Assert.True(store.FocusedWorkspaceIds.SetEquals(new[] { work.Id, doomed.Id }), "the membership still comes back");
        Assert.False(store.FocusEnabled, "the undo must not override the toggle the user just made");
    }

    [Fact]
    public void ReClosingAWorkspaceRebuiltByASessionUndoKeepsItsMembership()
    {
        // the superseded-record case: closing a workspace, undoing an EARLIER session close (which rebuilds
        // that workspace as an unmarked shell and trips the cross-set auto-disable), then closing it again
        // makes the second close absorb the first record. The live set no longer holds the membership by
        // then, so the absorbed record's flag is its only surviving copy, and the newer Open Recent entry
        // overwrites the older one on the same workspace id, so losing it there kills BOTH recovery routes.
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var keep = store.AddWorkspace("keep");
        var work = store.AddWorkspace("work");
        Assert.NotNull(store.AddSession(keep.Id, "/a"));
        var stray = store.AddSession(work.Id, "/b", select: false)!;
        Assert.NotNull(stray);
        store.SetFocusMembership(keep.Id, true);
        store.SetFocusMembership(work.Id, true);
        store.SetFocusEnabled(true);

        Assert.True(store.SoftCloseSession(stray.Id, grace: 60));
        var sessionCloseId = store.PendingCloseSummary!.Id;
        Assert.True(store.SoftRemoveWorkspace(work.Id, grace: 60));
        AssertFocus(store, true, keep.Id); // the close pruned the membership into its record

        // undoing the SESSION close rebuilds `work` as a shell: unmarked, and the reselect into it
        // switches the filter off through the cross-set auto-disable.
        Assert.True(store.UndoPendingClose(sessionCloseId));
        Assert.Contains(store.Workspaces, w => w.Id == work.Id);
        Assert.DoesNotContain(work.Id, store.FocusedWorkspaceIds);
        Assert.False(store.FocusEnabled);

        Assert.True(store.SoftRemoveWorkspace(work.Id, grace: 60));
        // the second recovery route: this close OVERWRITES the Open Recent entry the first one wrote, so a
        // lost membership here also kills every reopen past the grace window.
        var item = recentClosed.Load().First(i => i.Workspace?.Snapshot.Id == work.Id);
        Assert.True(item.Workspace?.FocusMember, "the overwriting Open Recent entry must keep the membership");

        Assert.True(store.UndoPendingClose());
        Assert.True(store.FocusedWorkspaceIds.SetEquals(new[] { keep.Id, work.Id }),
            "the absorbed record's membership must survive the re-close");
    }

    [Fact]
    public void UndoingANonMemberWorkspaceDoesNotMarkIt()
    {
        // an unmarked workspace must come back unmarked, or an undo would silently widen a working set
        // the user never put it in.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var doomed = store.AddWorkspace("doomed");
        store.AddWorkspace("spare");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusEnabled(true);

        Assert.True(store.SoftRemoveWorkspace(doomed.Id));
        Assert.True(store.UndoPendingClose());
        AssertFocus(store, true, work.Id);
    }

    [Fact]
    public void ReopeningARecentClosedMemberRestoresItsMembership()
    {
        // once the pending record is finalized, Reopen Closed Item is the only route back, so an empty
        // member reopened behind a still-applied filter hit the exact silent no-op the undo leg fixed:
        // appended, never marked, never rendered.
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var work = store.AddWorkspace("work");
        var doomed = store.AddWorkspace("doomed"); // empty on purpose: the case the restore used to lose
        Assert.NotNull(store.AddSession(work.Id, "/a"));
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(doomed.Id, true);
        store.SetFocusEnabled(true);

        Assert.True(store.SoftRemoveWorkspace(doomed.Id, grace: 60));
        store.FinalizePendingClose(store.PendingCloseSummary!.Id); // the grace expires
        AssertFocus(store, true, work.Id);

        var item = recentClosed.Load().First(i => i.Workspace?.Snapshot.Id == doomed.Id);
        Assert.True(store.RestoreRecentClosed(item));
        Assert.True(store.FocusedWorkspaceIds.SetEquals(new[] { work.Id, doomed.Id }), "a reopened member must be marked again");
        Assert.True(store.FocusEnabled);
        Assert.Equal(new[] { work.Id, doomed.Id }, Visible(store)); // its row is rendered again
    }

    [Fact]
    public void ReopeningARecentClosedMemberWithSessionsKeepsTheFilterOn()
    {
        // rejoining the set is what keeps the reselect INSIDE it. Without the re-mark the restore
        // suspended the whole filter through the cross-set auto-disable, blowing a hand-curated working
        // set open, while the same restore one second earlier (the undo) preserved it.
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var work = store.AddWorkspace("work");
        var doomed = store.AddWorkspace("doomed");
        Assert.NotNull(store.AddSession(work.Id, "/a"));
        var stray = store.AddSession(doomed.Id, "/b", select: false)!;
        Assert.NotNull(stray);
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(doomed.Id, true);
        store.SetFocusEnabled(true);

        Assert.True(store.SoftRemoveWorkspace(doomed.Id, grace: 60));
        store.FinalizePendingClose(store.PendingCloseSummary!.Id);

        var item = recentClosed.Load().First(i => i.Workspace?.Snapshot.Id == doomed.Id);
        Assert.True(store.RestoreRecentClosed(item));
        AssertFocus(store, true, work.Id, doomed.Id);
        Assert.Equal(new[] { work.Id, doomed.Id }, Visible(store));
        Assert.Equal(stray.Id, store.SelectedSessionId); // the rebuilt session, selected inside the set
    }

    [Fact]
    public void ReopeningTheLastRecentClosedMemberMarksItWithoutReApplyingTheFilter()
    {
        // the recent-closed leg MARKS ONLY, unlike the undo twin: removing the last member disabled the
        // filter, and the reopen must NOT switch it back on. The entry is not window-scoped or
        // time-scoped, so a flag taken from it would describe some other window (or some other day), and
        // re-applying here would collapse the tree the user is currently looking at.
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var other = store.AddWorkspace("other");
        var doomed = store.AddWorkspace("doomed");
        var spare = store.AddWorkspace("spare"); // soft-remove keeps at least one workspace
        store.SetFocusMembership(doomed.Id, true);
        store.SetFocusEnabled(true);

        Assert.True(store.SoftRemoveWorkspace(doomed.Id, grace: 60));
        store.FinalizePendingClose(store.PendingCloseSummary!.Id);
        AssertFocus(store, false);

        var item = recentClosed.Load().First(i => i.Workspace?.Snapshot.Id == doomed.Id);
        Assert.True(store.RestoreRecentClosed(item));
        Assert.True(store.FocusedWorkspaceIds.SetEquals(new[] { doomed.Id }), "a reopened member must be marked again");
        Assert.False(store.FocusEnabled, "marking on reopen must never switch the filter on");
        // so the workspace is genuinely back on screen, alongside everything else
        Assert.Equal(new[] { other.Id, spare.Id, doomed.Id }, Visible(store));
    }

    [Fact]
    public void ReopeningARecentClosedMemberWithTheFilterOffLeavesItOff()
    {
        // the target window's filter is OFF while it has its own marked set. The reopen must join the set
        // and change nothing else: an enable here would collapse the tree onto `the window's members +
        // this one` behind the user's back.
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var work = store.AddWorkspace("work");
        var doomed = store.AddWorkspace("doomed");
        var spare = store.AddWorkspace("spare");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(doomed.Id, true);
        store.SetFocusEnabled(true);

        Assert.True(store.SoftRemoveWorkspace(doomed.Id, grace: 60));
        store.FinalizePendingClose(store.PendingCloseSummary!.Id);
        store.SetFocusEnabled(false); // the user peeks at the whole tree before reopening

        var item = recentClosed.Load().First(i => i.Workspace?.Snapshot.Id == doomed.Id);
        Assert.True(store.RestoreRecentClosed(item));
        AssertFocus(store, false, work.Id, doomed.Id);
        Assert.Equal(new[] { work.Id, spare.Id, doomed.Id }, Visible(store)); // the whole tree, unchanged
    }

    [Fact]
    public void ReopeningARecordFromAFilteringWindowCannotEnableAnotherWindowsFilter()
    {
        // ONE RecentClosedStore is shared by every window's store and entries never expire, so a record
        // written while window A was filtering can be reopened into window B (or into the same window
        // days later). Marking only is what makes that safe: B's filter, and therefore what B renders, is
        // untouched.
        var dir = TempDir();
        var recentClosed = new RecentClosedStore(dir);
        var windowA = new AppStore(persistence: new PersistenceStore(Path.Combine(dir, "a")),
            recentClosedStore: recentClosed);
        var windowB = new AppStore(persistence: new PersistenceStore(Path.Combine(dir, "b")),
            recentClosedStore: recentClosed);

        var shared = windowA.AddWorkspace("shared");
        windowA.AddWorkspace("keeper"); // RemoveWorkspace keeps at least one
        windowA.SetFocusedWorkspace(shared.Id); // A closes it with its own filter applied
        Assert.True(windowA.FocusEnabled);
        windowA.RemoveWorkspace(shared.Id);

        var curated = windowB.AddWorkspace("curated");
        var extra = windowB.AddWorkspace("extra");
        windowB.SetFocusMembership(curated.Id, true); // B has its own set, filter off

        var item = recentClosed.Load().First(i => i.Workspace?.Snapshot.Id == shared.Id);
        Assert.True(windowB.RestoreRecentClosed(item));
        Assert.False(windowB.FocusEnabled, "a record from a filtering window must not apply B's filter");
        Assert.True(windowB.FocusedWorkspaceIds.SetEquals(new[] { curated.Id, shared.Id }));
        Assert.Equal(new[] { curated.Id, extra.Id, shared.Id }, Visible(windowB)); // B still shows everything
    }

    [Fact]
    public void ReopeningARecentClosedNonMemberDoesNotMarkIt()
    {
        // an unmarked workspace comes back unmarked, or a reopen would silently widen a working set the
        // user never put it in.
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var work = store.AddWorkspace("work");
        var doomed = store.AddWorkspace("doomed");
        store.AddWorkspace("spare");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusEnabled(true);

        Assert.True(store.SoftRemoveWorkspace(doomed.Id, grace: 60));
        store.FinalizePendingClose(store.PendingCloseSummary!.Id);

        var item = recentClosed.Load().First(i => i.Workspace?.Snapshot.Id == doomed.Id);
        Assert.True(store.RestoreRecentClosed(item));
        AssertFocus(store, true, work.Id);
        Assert.Equal(new[] { work.Id }, Visible(store));
    }

    [Fact]
    public void HardRemovingAMemberRecordsItsMembershipForReopen()
    {
        // RemoveWorkspace (no grace at all) records its recent entry BEFORE DropFocusMember prunes the
        // id, so the restore leg has the same state to work from as the soft-remove path.
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var work = store.AddWorkspace("work");
        var doomed = store.AddWorkspace("doomed");
        Assert.NotNull(store.AddSession(work.Id, "/a"));
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(doomed.Id, true);
        store.SetFocusEnabled(true);

        store.RemoveWorkspace(doomed.Id);
        var item = recentClosed.Load().First(i => i.Workspace?.Snapshot.Id == doomed.Id);
        Assert.True(item.Workspace?.FocusMember); // membership only: the flag is deliberately not recorded

        Assert.True(store.RestoreRecentClosed(item));
        Assert.True(store.FocusedWorkspaceIds.SetEquals(new[] { work.Id, doomed.Id }));
        Assert.True(store.FocusEnabled); // still on because `work` kept it on, not because the reopen re-applied it
    }

    [Fact]
    public void SoftRemovingAMemberPrunesItAndDisablesWhenTheSetEmpties()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var doomed = store.AddWorkspace("doomed");
        store.AddWorkspace("spare"); // soft-remove keeps at least one workspace
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(doomed.Id, true);
        store.SetFocusEnabled(true);
        Assert.True(store.SoftRemoveWorkspace(doomed.Id));
        AssertFocus(store, true, work.Id);
        Assert.True(store.SoftRemoveWorkspace(work.Id));
        AssertFocus(store, false);
    }

    [Fact]
    public void SelectingOutsideTheSetDisablesTheFilterButKeepsEveryMember()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var outside = store.AddWorkspace("outside");
        store.AddSession(work.Id, "/a");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(personal.Id, true);
        store.SetFocusEnabled(true);
        var stray = store.AddSession(outside.Id, "/b", select: false)!;
        Assert.NotNull(stray);
        store.SelectSession(stray.Id);
        AssertFocus(store, false, work.Id, personal.Id); // set intact
        store.SetFocusEnabled(true); // one flip restores the hand-curated working set
        Assert.Equal(new[] { work.Id, personal.Id }, Visible(store));
    }

    [Fact]
    public void SelectingInsideTheSetChangesNothing()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        store.AddSession(work.Id, "/a");
        var inSet = store.AddSession(personal.Id, "/b", select: false)!;
        Assert.NotNull(inSet);
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(personal.Id, true);
        store.SetFocusEnabled(true);
        store.SelectSession(inSet.Id);
        AssertFocus(store, true, work.Id, personal.Id);
    }

    [Fact]
    public void AddWorkspaceJoinsTheSetOnlyWhileTheFilterIsOn()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        // filter OFF with a marked set: the new workspace is visible anyway, so neither field is touched.
        store.SetFocusMembership(work.Id, true);
        store.SetFocusEnabled(false);
        store.AddWorkspace("quiet");
        AssertFocus(store, false, work.Id);
        // filter ON: the new workspace joins the set so it is visible without dropping the filter.
        store.SetFocusEnabled(true);
        var fresh = store.AddWorkspace("fresh");
        AssertFocus(store, true, work.Id, fresh.Id);
        Assert.Equal(new[] { work.Id, fresh.Id }, Visible(store)); // `quiet` stays filtered out
    }

    [Fact]
    public void AddWorkspaceWithoutRevealDoesNotWidenTheSet()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusEnabled(true);
        store.AddWorkspace("background", revealNewWorkspace: false);
        AssertFocus(store, true, work.Id); // the background create stays hidden
        Assert.Equal(new[] { work.Id }, Visible(store));
    }

    [Fact]
    public void RestorePrunesAnAllStaleSetToEmptyAndDisabled()
    {
        // every marked workspace is gone from the restored tree (deleted from another window, or a
        // hand-edited file). Restoring the set verbatim would leave `enabled + empty`, which reports
        // workspaceFilter == true while no workspace reports `focused`: a read-back contract that lies.
        var store = AppStoreFixtures.MakeStore();
        var survivor = new WorkspaceSnapshot { Id = Guid.NewGuid(), Name = "survivor", Sessions = Array.Empty<SessionSnapshot>() };
        store.Restore(new Snapshot
        {
            Workspaces = new[] { survivor },
            FocusedWorkspaceIds = new[] { Guid.NewGuid(), Guid.NewGuid() },
            FocusEnabled = true,
        });
        AssertFocus(store, false);
        Assert.Equal(new[] { survivor.Id }, Visible(store));
    }

    [Fact]
    public void RestoreKeepsTheSurvivorsOfAPartiallyStaleSet()
    {
        var store = AppStoreFixtures.MakeStore();
        var kept = new WorkspaceSnapshot { Id = Guid.NewGuid(), Name = "kept", Sessions = Array.Empty<SessionSnapshot>() };
        var other = new WorkspaceSnapshot { Id = Guid.NewGuid(), Name = "other", Sessions = Array.Empty<SessionSnapshot>() };
        store.Restore(new Snapshot
        {
            Workspaces = new[] { kept, other },
            FocusedWorkspaceIds = new[] { kept.Id, Guid.NewGuid() },
            FocusEnabled = true,
        });
        AssertFocus(store, true, kept.Id); // the survivor keeps filtering
        Assert.Equal(new[] { kept.Id }, Visible(store));
    }

    [Fact]
    public void RestoreKeepsADisabledSetWithoutEnablingIt()
    {
        // the flag is persisted apart from the set, so restoring a marked-but-off filter must not turn it
        // back on just because members survived the prune.
        var store = AppStoreFixtures.MakeStore();
        var kept = new WorkspaceSnapshot { Id = Guid.NewGuid(), Name = "kept", Sessions = Array.Empty<SessionSnapshot>() };
        var other = new WorkspaceSnapshot { Id = Guid.NewGuid(), Name = "other", Sessions = Array.Empty<SessionSnapshot>() };
        store.Restore(new Snapshot { Workspaces = new[] { kept, other }, FocusedWorkspaceIds = new[] { kept.Id } });
        AssertFocus(store, false, kept.Id);
        Assert.Equal(new[] { kept.Id, other.Id }, Visible(store)); // off means the whole tree renders
    }

    [Fact]
    public void RestoreClearsAFocusSetLeftOverFromAPreviousTree()
    {
        // Restore replaces the state wholesale (a window reopen reloads through it), so a set left from
        // the store's previous contents must not survive into the new tree.
        var store = AppStoreFixtures.MakeStore();
        var stale = store.AddWorkspace("stale");
        store.SetFocusMembership(stale.Id, true);
        var fresh = new WorkspaceSnapshot { Id = Guid.NewGuid(), Name = "fresh", Sessions = Array.Empty<SessionSnapshot>() };
        store.Restore(new Snapshot { Workspaces = new[] { fresh } });
        AssertFocus(store, false);
    }

    [Fact]
    public void DropFallbackIsNilWithNothingMarked()
    {
        var store = AppStoreFixtures.MakeStore();
        store.AddWorkspace("work");
        Assert.Null(store.SoleFocusedWorkspaceId); // no filter, so an empty-space drop uses the current workspace
    }

    [Fact]
    public void DropFallbackIsTheSoleMemberWhileTheFilterIsOn()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        store.AddWorkspace("personal");
        store.SetFocusedWorkspace(work.Id);
        Assert.Equal(work.Id, store.SoleFocusedWorkspaceId); // the only workspace the tree is rendering
    }

    [Fact]
    public void DropFallbackIsNilWhenTheFilterIsOff()
    {
        // one workspace is MARKED but the filter is OFF, so the whole tree is on screen: an empty-space
        // drop must not silently land in the marked workspace.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        store.AddWorkspace("personal");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusEnabled(false);
        AssertFocus(store, false, work.Id); // still marked, so set SIZE alone would answer wrong
        Assert.Null(store.SoleFocusedWorkspaceId);
    }

    [Fact]
    public void DropFallbackIsNilWithTwoMembersMarked()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusMembership(personal.Id, true);
        store.SetFocusEnabled(true); // enabled, so the nil answer comes from the COUNT, not from the flag
        Assert.Null(store.SoleFocusedWorkspaceId); // no unambiguous target among several rendered rows
    }

    [Fact]
    public void DropFallbackFeedsTheDirectoryDropResolverAcrossEverySetShape()
    {
        // the pair the sidebar's empty-space Explorer drop is built from: the store answers "is there ONE
        // unambiguous rendered workspace", and SidebarDrop turns that into the destination. Composed here
        // because the promise the two make together, that an empty-space drop lands where the user is
        // looking and falls through to the current workspace whenever the tree shows more than one
        // workspace, is not visible from either side alone.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var current = store.CurrentWorkspaceId!.Value;
        Guid? EmptySpaceDrop() => SidebarDrop.ResolveDirectoryWorkspace(store.SidebarMode, null,
            store.SoleFocusedWorkspaceId, store.CurrentWorkspaceId);

        Assert.Equal(current, EmptySpaceDrop()); // nothing marked: the current workspace
        store.SetFocusMembership(work.Id, true);
        Assert.Equal(current, EmptySpaceDrop()); // marked but NOT applied: the whole tree is on screen
        store.SetFocusEnabled(true);
        Assert.Equal(work.Id, EmptySpaceDrop()); // the one workspace the filtered tree renders
        store.SetFocusMembership(personal.Id, true);
        Assert.Equal(current, EmptySpaceDrop()); // two rendered workspaces give no unambiguous target

        // a drop ON a row always wins over both, and the flagged view takes no folder drops at all.
        Assert.Equal(personal.Id, SidebarDrop.ResolveDirectoryWorkspace(SidebarMode.Tree, personal.Id, work.Id, current));
        store.SetSidebarMode(SidebarMode.Flagged);
        Assert.Null(EmptySpaceDrop());
    }

    [Fact]
    public void ControlTreeReportsEveryMemberAsFocused()
    {
        var store = AppStoreFixtures.MakeStore();
        var one = store.AddWorkspace("one");
        store.AddWorkspace("two");
        var three = store.AddWorkspace("three");
        store.SetFocusMembership(one.Id, true);
        store.SetFocusMembership(three.Id, true);

        var nodes = store.ControlTree().Workspaces;
        Assert.Equal(new[] { Upper(one.Id), Upper(three.Id) }.OrderBy(s => s, StringComparer.Ordinal),
            nodes.Where(n => n.Focused == true).Select(n => n.Id).OrderBy(s => s, StringComparer.Ordinal));
        Assert.Null(nodes.First(n => n.Id != Upper(one.Id) && n.Id != Upper(three.Id)).Focused);
    }

    [Fact]
    public void ControlTreeReportsMembershipIndependentlyOfTheFilterFlag()
    {
        // membership and the flag are separate read-back fields: a marked-but-not-filtering set must still
        // report `focused`, else a script could not record a working set while the filter is off.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        store.AddWorkspace("personal");
        store.SetFocusMembership(work.Id, true);
        store.SetFocusEnabled(false);

        var tree = store.ControlTree();
        Assert.False(tree.WorkspaceFilter);
        Assert.True(tree.Workspaces.First(n => n.Id == Upper(work.Id)).Focused);
    }

    [Fact]
    public void ControlTreeReportsWorkspaceFilterInBothStates()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        Assert.False(store.ControlTree().WorkspaceFilter); // nothing marked, filter off
        store.SetFocusMembership(work.Id, true);
        Assert.False(store.ControlTree().WorkspaceFilter); // marking alone does not apply the filter
        store.SetFocusEnabled(true);
        Assert.True(store.ControlTree().WorkspaceFilter);
        store.SetFocusEnabled(false);
        Assert.False(store.ControlTree().WorkspaceFilter);
    }

    [Fact]
    public async Task WorkspaceFilterOnAnEmptySetLeavesTheFilterOffThroughTheControlPath()
    {
        // the row-visibility contract published to scripts carries `!workspaceFilter || focused` as its
        // filter term. If the control path could enable an EMPTY set, workspaceFilter would report true
        // while no workspace reported `focused`, so a script would conclude nothing is visible while the
        // whole tree is on screen. Driven through the dispatcher's parse into
        // AppStore.ApplyWorkspaceFilter, the same host-free helper the app-side arm calls.
        var store = AppStoreFixtures.MakeStore();
        store.AddWorkspace("work");
        var actions = new MockControlActions { FocusStore = store };
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceFilter, Args = new ControlArgs { Mode = "on" },
        });

        Assert.True(response?.Ok);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.WorkspaceFilter(null, ControlToggleMode.On),
        }, actions.Calls); // `on` reached the arm, and changed nothing
        Assert.False(store.FocusEnabled);
        var tree = store.ControlTree();
        Assert.False(tree.WorkspaceFilter);
        Assert.All(tree.Workspaces, w => Assert.Null(w.Focused));
    }

    [Fact]
    public async Task WorkspaceFocusModesDriveTheStoreThroughTheControlPath()
    {
        // the four modes' semantics run through the dispatcher's parse into AppStore.ApplyFocusMode, the
        // host-free half of the app-side arm. `toggle` is the DEFAULT mode (an omitted `mode` arg), and
        // its replace-toggle against a two-member set (narrow to the target, then clear on a repeat) is
        // the behavior nothing else pins.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var actions = new MockControlActions { FocusStore = store };
        var dispatcher = new ControlDispatcher(actions);
        async Task<ControlResponse?> Focus(Guid id, string? mode) => await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceFocus, Target = id.ToString(),
            Args = mode is null ? null : new ControlArgs { Mode = mode },
        });

        Assert.True((await Focus(work.Id, "add"))?.Ok);
        Assert.True((await Focus(personal.Id, "add"))?.Ok);
        AssertFocus(store, false, work.Id, personal.Id); // add never applies

        Assert.True((await Focus(work.Id, null))?.Ok); // omitted mode = toggle
        AssertFocus(store, true, work.Id); // replace-toggle NARROWS
        Assert.Equal(new[] { work.Id }, Visible(store));

        Assert.True((await Focus(work.Id, "toggle"))?.Ok);
        AssertFocus(store, false); // a repeat on the sole member clears

        Assert.True((await Focus(personal.Id, "on"))?.Ok);
        AssertFocus(store, true, personal.Id);
        Assert.True((await Focus(personal.Id, "off"))?.Ok);
        AssertFocus(store, false);
        Assert.Equal(6, actions.Calls.Count); // every mode routed, none swallowed by the dispatcher
        // the double resolves only full-id targets, so an unresolved one would make every "the store
        // changed" assertion above pass vacuously against a store nothing ever touched.
        Assert.Empty(actions.UnresolvedFocusTargets);
    }

    [Fact]
    public async Task ATargetTheStoreDrivingDoubleCannotResolveIsRecordedRatherThanSwallowed()
    {
        // the double stands in for the app-side arm's target resolution but implements only its
        // id-spelling half: `active` and prefixes need ControlTargetResolver. Silently no-opping on one
        // would let a "the store did not change" assertion pass without the command ever reaching the
        // store.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        store.SetFocusMembership(work.Id, true);
        var actions = new MockControlActions { FocusStore = store };
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceFocus, Args = new ControlArgs { Mode = "off" },
        });

        Assert.True(response?.Ok);
        Assert.Equal(new[] { "active" }, actions.UnresolvedFocusTargets);
        AssertFocus(store, false, work.Id); // unchanged, but now provably for the wrong reason
    }

    [Fact]
    public async Task WorkspaceFilterTogglesTheFlagThroughTheControlPathOnceAWorkspaceIsMarked()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        store.SetFocusMembership(work.Id, true);
        var actions = new MockControlActions { FocusStore = store };
        var dispatcher = new ControlDispatcher(actions);
        Task<ControlResponse?> Filter(string mode) => dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceFilter, Args = new ControlArgs { Mode = mode },
        });

        // marking left the filter off, so `on` is what applies the set: the two-call build-then-apply
        // shape a script uses after a run of `workspace.focus add`.
        await Filter("on");
        Assert.True(store.ControlTree().WorkspaceFilter);

        await Filter("off");
        Assert.False(store.ControlTree().WorkspaceFilter);
        AssertFocus(store, false, work.Id); // turning the filter off keeps the marked set

        await Filter("toggle");
        Assert.True(store.ControlTree().WorkspaceFilter);
    }
}
