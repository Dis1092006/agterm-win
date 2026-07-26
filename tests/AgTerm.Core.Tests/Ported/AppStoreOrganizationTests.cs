using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported 1:1 from agtermCore AppStoreOrganizationTests.swift (names preserved).</summary>
public class AppStoreOrganizationTests
{
    [Fact]
    public void MoveSessionDoesNotTearDownSurface()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var session = store.AddSession(work.Id, "/a")!;
        var surface = new SpySurface();
        session.Surface = surface;
        store.MoveSession(session.Id, personal.Id);
        Assert.Equal(0, surface.TeardownCount);
        Assert.Same(surface, store.Workspaces[1].Sessions[0].Surface);
    }

    [Fact]
    public void MoveSessionClampsNegativeIndex()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var moved = store.AddSession(work.Id, "/moved")!;
        var x = store.AddSession(personal.Id, "/x")!;
        store.MoveSession(moved.Id, personal.Id, at: -5);
        Assert.Equal(new[] { moved.Id, x.Id }, store.Workspaces[1].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void MoveSessionAppendsToTargetWorkspace()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var a = store.AddSession(work.Id, "/a")!;
        var b = store.AddSession(personal.Id, "/b")!;
        store.MoveSession(a.Id, personal.Id);
        Assert.Empty(store.Workspaces[0].Sessions);
        Assert.Equal(new[] { b.Id, a.Id }, store.Workspaces[1].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void MoveSessionInsertsAtIndex()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var moved = store.AddSession(work.Id, "/moved")!;
        var x = store.AddSession(personal.Id, "/x")!;
        var y = store.AddSession(personal.Id, "/y")!;
        store.MoveSession(moved.Id, personal.Id, at: 1);
        Assert.Equal(new[] { x.Id, moved.Id, y.Id }, store.Workspaces[1].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void MoveSessionClampsOutOfRangeIndex()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var moved = store.AddSession(work.Id, "/moved")!;
        var x = store.AddSession(personal.Id, "/x")!;
        store.MoveSession(moved.Id, personal.Id, at: 99);
        Assert.Equal(new[] { x.Id, moved.Id }, store.Workspaces[1].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void MoveSessionPreservesSameInstance()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var session = store.AddSession(work.Id, "/a")!;
        session.CustomName = "build";
        store.MoveSession(session.Id, personal.Id);
        var movedRef = store.Workspaces[1].Sessions[0];
        Assert.Same(session, movedRef);
        Assert.Equal("build", movedRef.CustomName);
    }

    [Fact]
    public void AddSessionAppendsWhenIndexNil()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var a = store.AddSession(work.Id, "/a")!;
        var b = store.AddSession(work.Id, "/b")!;
        Assert.Equal(new[] { a.Id, b.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void AddSessionInsertsAtHead()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var a = store.AddSession(work.Id, "/a")!;
        var head = store.AddSession(work.Id, "/head", at: 0)!;
        Assert.Equal(new[] { head.Id, a.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void AddSessionInsertsAtMiddle()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var a = store.AddSession(work.Id, "/a")!;
        var b = store.AddSession(work.Id, "/b")!;
        var mid = store.AddSession(work.Id, "/mid", at: 1)!;
        Assert.Equal(new[] { a.Id, mid.Id, b.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void AddSessionInsertsAtTail()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var a = store.AddSession(work.Id, "/a")!;
        var tail = store.AddSession(work.Id, "/tail", at: 1)!;
        Assert.Equal(new[] { a.Id, tail.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void AddSessionClampsNegativeIndexToHead()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var a = store.AddSession(work.Id, "/a")!;
        var clamped = store.AddSession(work.Id, "/clamped", at: -5)!;
        Assert.Equal(new[] { clamped.Id, a.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void AddSessionClampsOutOfRangeIndexToTail()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var a = store.AddSession(work.Id, "/a")!;
        var clamped = store.AddSession(work.Id, "/clamped", at: 99)!;
        Assert.Equal(new[] { a.Id, clamped.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void AddSessionUnknownWorkspaceReturnsNilWithIndex()
    {
        var store = AppStoreFixtures.MakeStore();
        Assert.Null(store.AddSession(Guid.NewGuid(), "/a", at: 0));
    }

    [Fact]
    public void SetFlagTogglesAndPersists()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "agterm-tests-" + Guid.NewGuid().ToString("D").ToUpperInvariant());
        var persistence = new PersistenceStore(directory: dir);
        var store = new AppStore(persistence: persistence);
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        Assert.False(a.Flagged);
        store.SetFlag(true, a.Id);
        Assert.True(a.Flagged);
        Assert.Equal(true, persistence.Load().Workspaces[0].Sessions[0].Flagged); // structural save hit disk
        store.SetFlag(false, a.Id);
        Assert.False(a.Flagged);
        Assert.Equal(false, persistence.Load().Workspaces[0].Sessions[0].Flagged);
    }

    [Fact]
    public void SetFlagUnknownIdIsNoOp()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        store.SetFlag(true, Guid.NewGuid()); // unknown id
        Assert.False(a.Flagged);
    }

    [Fact]
    public void ClearFlagsEmptiesTheSet()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var a = store.AddSession(work.Id, "/a")!;
        var b = store.AddSession(personal.Id, "/b")!;
        store.SetFlag(true, a.Id);
        store.SetFlag(true, b.Id);
        Assert.Equal(2, store.FlaggedSessions.Count);
        store.ClearFlags();
        Assert.Empty(store.FlaggedSessions);
        Assert.False(a.Flagged);
        Assert.False(b.Flagged);
    }

    [Fact]
    public void FlaggedSessionsReturnsMatchesInTreeOrder()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var a = store.AddSession(work.Id, "/a")!;
        store.AddSession(work.Id, "/b"); // unflagged, skipped
        var c = store.AddSession(personal.Id, "/c")!;
        store.SetFlag(true, c.Id);
        store.SetFlag(true, a.Id);
        // workspace-then-session order, regardless of flag-setting order
        Assert.Equal(new[] { a.Id, c.Id }, store.FlaggedSessions.Select(s => s.Id));
    }

    [Fact]
    public void FlaggedSessionMovedToOtherWorkspaceResorts()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var a = store.AddSession(work.Id, "/a")!;
        var b = store.AddSession(personal.Id, "/b")!;
        store.SetFlag(true, a.Id);
        store.SetFlag(true, b.Id);
        Assert.Equal(new[] { a.Id, b.Id }, store.FlaggedSessions.Select(s => s.Id));
        // moving a into personal (after b) keeps its flag and re-sorts the derived list
        store.MoveSession(a.Id, personal.Id);
        Assert.True(a.Flagged);
        Assert.Equal(new[] { b.Id, a.Id }, store.FlaggedSessions.Select(s => s.Id));
    }

    [Fact]
    public void SetFocusedWorkspaceSetsAndClears()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        Assert.Null(store.FocusedWorkspaceId);
        store.SetFocusedWorkspace(work.Id);
        Assert.Equal(work.Id, store.FocusedWorkspaceId);
        store.SetFocusedWorkspace(null);
        Assert.Null(store.FocusedWorkspaceId);
    }

    [Fact]
    public void EnsureWorkspaceClearFocusFalsePreservesFocusOnCreate()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws1 = store.AddWorkspace("one");
        store.SetFocusedWorkspace(ws1.Id);
        Assert.Equal(ws1.Id, store.FocusedWorkspaceId);
        // a normal create clears focus so the new workspace is revealed (addWorkspace auto-reveal contract).
        store.AddWorkspace("two");
        Assert.Null(store.FocusedWorkspaceId);
        // re-focus, then a background create (clearFocus: false, backing session.new --no-select
        // --create-workspace) leaves the focus filter untouched.
        store.SetFocusedWorkspace(ws1.Id);
        var created = store.EnsureWorkspace("bg", clearFocus: false);
        Assert.NotNull(created);
        Assert.Equal(ws1.Id, store.FocusedWorkspaceId);
        // reusing an existing workspace never touches focus regardless of clearFocus.
        var reused = store.EnsureWorkspace("bg", clearFocus: true);
        Assert.Equal(created?.Id, reused?.Id);
        Assert.Equal(ws1.Id, store.FocusedWorkspaceId);
    }

    [Fact]
    public void RemoveFocusedWorkspaceClearsFocus()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var doomed = store.AddWorkspace("doomed");
        store.SetFocusedWorkspace(doomed.Id);
        store.RemoveWorkspace(doomed.Id);
        Assert.Null(store.FocusedWorkspaceId);
        _ = work;
    }

    [Fact]
    public void RemoveNonFocusedWorkspaceKeepsFocus()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var doomed = store.AddWorkspace("doomed");
        store.SetFocusedWorkspace(work.Id);
        store.RemoveWorkspace(doomed.Id);
        Assert.Equal(work.Id, store.FocusedWorkspaceId);
    }

    [Fact]
    public void NewWorkspaceStartsExpanded()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        Assert.True(work.IsExpanded);
        Assert.True(store.Workspaces[0].IsExpanded);
    }

    [Fact]
    public void NewWorkspaceCollapsedStartsCollapsed()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work", collapsed: true);
        Assert.False(work.IsExpanded);
        Assert.False(store.Workspaces[0].IsExpanded);
    }

    [Fact]
    public void SetWorkspacesExpandedTogglesAndPersists()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "agterm-tests-" + Guid.NewGuid().ToString("D").ToUpperInvariant());
        var persistence = new PersistenceStore(directory: dir);
        var store = new AppStore(persistence: persistence);
        var a = store.AddWorkspace("a");
        var b = store.AddWorkspace("b");
        // only `a` stays expanded; `b` collapses.
        store.SetWorkspacesExpanded(new HashSet<Guid> { a.Id });
        Assert.True(store.Workspaces[0].IsExpanded);
        Assert.False(store.Workspaces[1].IsExpanded);
        var loaded = persistence.Load();
        Assert.Null(loaded.Workspaces[0].Collapsed);         // expanded → omitted
        Assert.Equal(true, loaded.Workspaces[1].Collapsed);  // collapsed → written
        _ = b;
    }

    [Fact]
    public void SetWorkspacesExpandedUnchangedDoesNotWrite()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "agterm-tests-" + Guid.NewGuid().ToString("D").ToUpperInvariant());
        var persistence = new PersistenceStore(directory: dir);
        var store = new AppStore(persistence: persistence);
        var a = store.AddWorkspace("a");
        // collapse a, flush to disk, then hand-edit the on-disk file to a sentinel and re-issue the SAME
        // state: a no-op mutator must not overwrite the sentinel (proving it skipped save()).
        store.SetWorkspacesExpanded(new HashSet<Guid>()); // a collapsed, saved
        var sentinelPath = Path.Combine(dir, "workspaces.json");
        File.WriteAllText(sentinelPath, "{ not json }");
        store.SetWorkspacesExpanded(new HashSet<Guid>()); // same state → no save, sentinel survives
        Assert.Equal("{ not json }", File.ReadAllText(sentinelPath));
        _ = a;
    }

    [Fact]
    public void SetWorkspaceExpandedTogglesSingleWorkspaceAndPersists()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "agterm-tests-" + Guid.NewGuid().ToString("D").ToUpperInvariant());
        var persistence = new PersistenceStore(directory: dir);
        var store = new AppStore(persistence: persistence);
        var a = store.AddWorkspace("a");
        var b = store.AddWorkspace("b");
        store.SetWorkspaceExpanded(a.Id, expanded: false);
        Assert.False(store.Workspaces[0].IsExpanded);
        Assert.True(store.Workspaces[1].IsExpanded); // b untouched — per-workspace, not whole-tree
        var loaded = persistence.Load();
        Assert.Equal(true, loaded.Workspaces[0].Collapsed);
        Assert.Null(loaded.Workspaces[1].Collapsed);
        _ = b;
    }

    [Fact]
    public void SetWorkspaceExpandedUnknownOrUnchangedIsNoOp()
    {
        var store = AppStoreFixtures.MakeStore();
        var a = store.AddWorkspace("a");
        store.SetWorkspaceExpanded(Guid.NewGuid(), expanded: false); // unknown id
        Assert.True(store.Workspaces[0].IsExpanded);
        store.SetWorkspaceExpanded(a.Id, expanded: true); // already expanded
        Assert.True(store.Workspaces[0].IsExpanded);
    }

    [Fact]
    public void VisibleWorkspacesReturnsAllWhenUnfocused()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        Assert.Equal(new[] { work.Id, personal.Id }, store.VisibleWorkspaces.Select(w => w.Id));
    }

    [Fact]
    public void VisibleWorkspacesReturnsOneWhenFocused()
    {
        var store = AppStoreFixtures.MakeStore();
        store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        store.SetFocusedWorkspace(personal.Id);
        Assert.Equal(new[] { personal.Id }, store.VisibleWorkspaces.Select(w => w.Id));
    }

    [Fact]
    public void VisibleWorkspacesFallsBackToAllForStaleFocusID()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        store.FocusedWorkspaceId = Guid.NewGuid(); // stale id, no matching workspace
        Assert.Equal(new[] { work.Id, personal.Id }, store.VisibleWorkspaces.Select(w => w.Id));
    }

    [Fact]
    public void SelectSessionOutsideFocusClearsFocus()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        store.AddSession(work.Id, "/a");
        var outside = store.AddSession(personal.Id, "/b")!;
        store.SetFocusedWorkspace(work.Id);
        store.SelectSession(outside.Id);
        Assert.Null(store.FocusedWorkspaceId); // auto-unfocus reveals the off-focus target
    }

    [Fact]
    public void SelectSessionInsideFocusKeepsFocus()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var a = store.AddSession(work.Id, "/a")!;
        var inside = store.AddSession(work.Id, "/b")!;
        store.SetFocusedWorkspace(work.Id);
        store.SelectSession(inside.Id);
        Assert.Equal(work.Id, store.FocusedWorkspaceId);
        _ = a;
    }

    [Fact]
    public void SelectSessionWhileUnfocusedIsNoOpOnFocus()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var a = store.AddSession(work.Id, "/a")!;
        store.SelectSession(a.Id);
        Assert.Null(store.FocusedWorkspaceId);
    }

    [Fact]
    public void CloseFocusedSessionRevealingOtherWorkspaceClearsFocus()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var only = store.AddSession(work.Id, "/only")!;
        store.AddSession(personal.Id, "/other");
        store.SelectSession(only.Id);
        store.SetFocusedWorkspace(work.Id);
        store.CloseSession(only.Id); // reselects the personal session — outside the now-empty focused work
        Assert.NotNull(store.ActiveSession);
        Assert.Equal(personal.Id, store.WorkspaceForSession(store.SelectedSessionId!.Value)?.Id);
        Assert.Null(store.FocusedWorkspaceId); // auto-unfocus reveals the new active session
    }

    [Fact]
    public void RemoveWorkspaceReselectingNonFocusedClearsFocus()
    {
        var store = AppStoreFixtures.MakeStore();
        var a = store.AddWorkspace("a");
        var b = store.AddWorkspace("b");
        var c = store.AddWorkspace("c");
        store.AddSession(a.Id, "/a");
        store.AddSession(b.Id, "/b");
        var activeInC = store.AddSession(c.Id, "/c")!;
        store.SelectSession(activeInC.Id);
        store.SetFocusedWorkspace(a.Id);
        store.RemoveWorkspace(c.Id); // reselects into b (the fallback slot), outside the focused a
        Assert.Equal(b.Id, store.WorkspaceForSession(store.SelectedSessionId!.Value)?.Id);
        Assert.Null(store.FocusedWorkspaceId); // auto-unfocus reveals the reselected session
    }

    [Fact]
    public void AddSessionToOtherWorkspaceWhileFocusedClearsFocus()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var other = store.AddWorkspace("other");
        store.AddSession(work.Id, "/w");
        store.SetFocusedWorkspace(work.Id);
        var created = store.AddSession(other.Id, "/o")!; // a control add into another workspace
        Assert.Equal(created.Id, store.SelectedSessionId);
        Assert.Null(store.FocusedWorkspaceId); // auto-unfocus reveals the just-created off-focus session
    }

    [Fact]
    public void AddSessionInsideFocusedWorkspaceKeepsFocus()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        store.AddSession(work.Id, "/a");
        store.SetFocusedWorkspace(work.Id);
        var created = store.AddSession(work.Id, "/b")!; // the GUI new-session path lands here
        Assert.Equal(created.Id, store.SelectedSessionId);
        Assert.Equal(work.Id, store.FocusedWorkspaceId);
    }

    [Fact]
    public void SelectNilWhileFocusedKeepsFocus()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var a = store.AddSession(work.Id, "/a")!;
        store.SelectSession(a.Id);
        store.SetFocusedWorkspace(work.Id);
        store.SelectSession(null); // deselect reveals nothing, so focus is retained
        Assert.Equal(work.Id, store.FocusedWorkspaceId);
    }

    [Fact]
    public void MoveActiveSessionOutOfFocusedWorkspaceClearsFocus()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var other = store.AddWorkspace("other");
        var a = store.AddSession(work.Id, "/a")!;
        store.SelectSession(a.Id);
        store.SetFocusedWorkspace(work.Id);
        store.MoveSession(a.Id, other.Id); // the active session leaves the focused workspace
        Assert.Equal(a.Id, store.SelectedSessionId);
        Assert.Null(store.FocusedWorkspaceId); // auto-unfocus reveals the moved active session
    }

    [Fact]
    public void MoveNonActiveSessionOutOfFocusedWorkspaceKeepsFocus()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var other = store.AddWorkspace("other");
        var a = store.AddSession(work.Id, "/a")!;
        var b = store.AddSession(work.Id, "/b")!;
        store.SelectSession(a.Id);
        store.SetFocusedWorkspace(work.Id);
        store.MoveSession(b.Id, other.Id); // a non-active session leaves; focus must stand
        Assert.Equal(a.Id, store.SelectedSessionId);
        Assert.Equal(work.Id, store.FocusedWorkspaceId);
    }

    [Fact]
    public void AddWorkspaceWhileFocusedClearsFocusAndRevealsNew()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        store.AddSession(work.Id, "/a");
        store.SetFocusedWorkspace(work.Id);
        var fresh = store.AddWorkspace("fresh"); // a new (empty) workspace must become visible
        Assert.Null(store.FocusedWorkspaceId);
        Assert.Equal(new[] { work.Id, fresh.Id }, store.VisibleWorkspaces.Select(w => w.Id));
    }

    [Fact]
    public void FlaggedSessionsIgnoreFocus()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var a = store.AddSession(work.Id, "/a")!;
        var b = store.AddSession(personal.Id, "/b")!;
        store.SetFlag(true, a.Id);
        store.SetFlag(true, b.Id);
        store.SetFocusedWorkspace(work.Id); // focus is orthogonal — it must NOT shrink the flagged set
        Assert.Equal(new[] { a.Id, b.Id }, store.FlaggedSessions.Select(s => s.Id)); // spans both workspaces, not just the focused one
    }

    [Fact]
    public void SetSameValuesAreNoOpWritesAndStable()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "agterm-tests-" + Guid.NewGuid().ToString("D").ToUpperInvariant());
        var persistence = new PersistenceStore(directory: dir);
        var store = new AppStore(persistence: persistence);
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        store.SetFlag(true, a.Id);
        store.SetSidebarMode(SidebarMode.Flagged);
        store.SetFocusedWorkspace(ws.Id);
        var file = Path.Combine(dir, "workspaces.json");
        try { File.Delete(file); } catch { } // a no-op setter must NOT recreate the file
        store.SetFlag(true, a.Id);                 // unchanged
        store.SetSidebarMode(SidebarMode.Flagged); // unchanged
        store.SetFocusedWorkspace(ws.Id);          // unchanged
        Assert.False(File.Exists(file)); // no write happened
        Assert.True(a.Flagged);
        Assert.Equal(SidebarMode.Flagged, store.SidebarMode);
        Assert.Equal(ws.Id, store.FocusedWorkspaceId); // state stable across the no-op setters
    }

    [Fact]
    public void MoveActiveSessionKeepsItSelected()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var a = store.AddSession(work.Id, "/a")!;
        store.SelectSession(a.Id);
        store.MoveSession(a.Id, personal.Id);
        Assert.Equal(a.Id, store.SelectedSessionId);
        Assert.Equal(a.Id, store.ActiveSession?.Id);
        Assert.Equal(personal.Id, store.WorkspaceForSession(a.Id)?.Id);
    }

    [Fact]
    public void MoveNonActiveSessionLeavesSelectionUntouched()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var a = store.AddSession(work.Id, "/a")!;
        var b = store.AddSession(work.Id, "/b")!;
        store.SelectSession(b.Id);
        store.MoveSession(a.Id, personal.Id);
        Assert.Equal(b.Id, store.SelectedSessionId);
    }

    [Fact]
    public void MoveLastSessionLeavesSourceEmpty()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var only = store.AddSession(work.Id, "/only")!;
        store.SelectSession(only.Id);
        store.MoveSession(only.Id, personal.Id);
        Assert.Empty(store.Workspaces[0].Sessions);
        Assert.Equal(new[] { only.Id }, store.Workspaces[1].Sessions.Select(s => s.Id));
        Assert.Equal(only.Id, store.SelectedSessionId);
    }

    [Fact]
    public void MoveSessionWithinSameWorkspaceReorders()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        var c = store.AddSession(ws.Id, "/c")!;
        store.MoveSession(a.Id, ws.Id, at: 2);
        Assert.Equal(new[] { b.Id, c.Id, a.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void MoveSessionWithinSameWorkspaceToCurrentSlotIsNoOp()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        store.MoveSession(a.Id, ws.Id, at: 0);
        Assert.Equal(new[] { a.Id, b.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void MoveUnknownSessionIsIgnored()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var a = store.AddSession(work.Id, "/a")!;
        store.MoveSession(Guid.NewGuid(), personal.Id);
        Assert.Equal(new[] { a.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.Empty(store.Workspaces[1].Sessions);
    }

    [Fact]
    public void MoveToUnknownWorkspaceIsIgnored()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var a = store.AddSession(work.Id, "/a")!;
        store.MoveSession(a.Id, Guid.NewGuid());
        Assert.Equal(new[] { a.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    /// <summary>Builds a single-workspace tree (a, b, c) with the middle session (b) selected.</summary>
    private static (AppStore Store, Workspace Ws, Guid[] Ids) MakeReorderTree()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        var c = store.AddSession(ws.Id, "/c")!;
        store.SelectSession(b.Id);
        return (store, ws, new[] { a.Id, b.Id, c.Id });
    }

    [Fact]
    public void ReorderSessionUp()
    {
        var (store, _, ids) = MakeReorderTree();
        store.ReorderSession(ids[1], ReorderDirection.Up);
        Assert.Equal(new[] { ids[1], ids[0], ids[2] }, store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.Equal(ids[1], store.SelectedSessionId);
    }

    [Fact]
    public void ReorderSessionDown()
    {
        var (store, _, ids) = MakeReorderTree();
        store.ReorderSession(ids[1], ReorderDirection.Down);
        Assert.Equal(new[] { ids[0], ids[2], ids[1] }, store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.Equal(ids[1], store.SelectedSessionId);
    }

    [Fact]
    public void ReorderSessionTop()
    {
        var (store, _, ids) = MakeReorderTree();
        store.ReorderSession(ids[2], ReorderDirection.Top);
        Assert.Equal(new[] { ids[2], ids[0], ids[1] }, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void ReorderSessionBottom()
    {
        var (store, _, ids) = MakeReorderTree();
        store.ReorderSession(ids[0], ReorderDirection.Bottom);
        Assert.Equal(new[] { ids[1], ids[2], ids[0] }, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void ReorderSessionUpAtTopIsNoOp()
    {
        var (store, _, ids) = MakeReorderTree();
        store.ReorderSession(ids[0], ReorderDirection.Up);
        Assert.Equal(ids, store.Workspaces[0].Sessions.Select(s => s.Id));
        store.ReorderSession(ids[0], ReorderDirection.Top);
        Assert.Equal(ids, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void ReorderSessionDownAtBottomIsNoOp()
    {
        var (store, _, ids) = MakeReorderTree();
        store.ReorderSession(ids[2], ReorderDirection.Down);
        Assert.Equal(ids, store.Workspaces[0].Sessions.Select(s => s.Id));
        store.ReorderSession(ids[2], ReorderDirection.Bottom);
        Assert.Equal(ids, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void ReorderUnknownSessionIsIgnored()
    {
        var (store, _, ids) = MakeReorderTree();
        store.ReorderSession(Guid.NewGuid(), ReorderDirection.Up);
        Assert.Equal(ids, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void SessionLocationReportsWorkspaceIndexAndCount()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var a = store.AddSession(work.Id, "/a")!;
        var b = store.AddSession(work.Id, "/b")!;
        var c = store.AddSession(personal.Id, "/c")!;

        var locA = store.SessionLocation(a.Id);
        Assert.Equal(work.Id, locA?.Workspace);
        Assert.Equal(0, locA?.Index);
        Assert.Equal(2, locA?.Count);

        var locB = store.SessionLocation(b.Id);
        Assert.Equal(work.Id, locB?.Workspace);
        Assert.Equal(1, locB?.Index);
        Assert.Equal(2, locB?.Count);

        var locC = store.SessionLocation(c.Id);
        Assert.Equal(personal.Id, locC?.Workspace);
        Assert.Equal(0, locC?.Index);
        Assert.Equal(1, locC?.Count);
    }

    [Fact]
    public void SessionLocationOfUnknownSessionIsNil()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        store.AddSession(work.Id, "/a");
        Assert.Null(store.SessionLocation(Guid.NewGuid()));
    }

    /// <summary>Builds a three-workspace tree [w0, w1, w2] with no sessions.</summary>
    private static (AppStore Store, Guid[] Ids) MakeWorkspaceReorderTree()
    {
        var store = AppStoreFixtures.MakeStore();
        var w0 = store.AddWorkspace("w0");
        var w1 = store.AddWorkspace("w1");
        var w2 = store.AddWorkspace("w2");
        return (store, new[] { w0.Id, w1.Id, w2.Id });
    }

    [Fact]
    public void MoveWorkspaceReordersWithinBounds()
    {
        var (store, ids) = MakeWorkspaceReorderTree();
        store.MoveWorkspace(ids[0], at: 2);
        Assert.Equal(new[] { ids[1], ids[2], ids[0] }, store.Workspaces.Select(w => w.Id));
    }

    [Fact]
    public void MoveWorkspaceClampsIndexAtBothEnds()
    {
        var (store, ids) = MakeWorkspaceReorderTree();
        store.MoveWorkspace(ids[1], at: 99);
        Assert.Equal(new[] { ids[0], ids[2], ids[1] }, store.Workspaces.Select(w => w.Id));
        store.MoveWorkspace(ids[1], at: -5);
        Assert.Equal(new[] { ids[1], ids[0], ids[2] }, store.Workspaces.Select(w => w.Id));
    }

    [Fact]
    public void MoveUnknownWorkspaceIsIgnored()
    {
        var (store, ids) = MakeWorkspaceReorderTree();
        store.MoveWorkspace(Guid.NewGuid(), at: 0);
        Assert.Equal(ids, store.Workspaces.Select(w => w.Id));
    }

    [Fact]
    public void ReorderWorkspaceUp()
    {
        var (store, ids) = MakeWorkspaceReorderTree();
        store.ReorderWorkspace(ids[1], ReorderDirection.Up);
        Assert.Equal(new[] { ids[1], ids[0], ids[2] }, store.Workspaces.Select(w => w.Id));
    }

    [Fact]
    public void ReorderWorkspaceDown()
    {
        var (store, ids) = MakeWorkspaceReorderTree();
        store.ReorderWorkspace(ids[1], ReorderDirection.Down);
        Assert.Equal(new[] { ids[0], ids[2], ids[1] }, store.Workspaces.Select(w => w.Id));
    }

    [Fact]
    public void ReorderWorkspaceTop()
    {
        var (store, ids) = MakeWorkspaceReorderTree();
        store.ReorderWorkspace(ids[2], ReorderDirection.Top);
        Assert.Equal(new[] { ids[2], ids[0], ids[1] }, store.Workspaces.Select(w => w.Id));
    }

    [Fact]
    public void ReorderWorkspaceBottom()
    {
        var (store, ids) = MakeWorkspaceReorderTree();
        store.ReorderWorkspace(ids[0], ReorderDirection.Bottom);
        Assert.Equal(new[] { ids[1], ids[2], ids[0] }, store.Workspaces.Select(w => w.Id));
    }

    [Fact]
    public void ReorderWorkspaceAtEndsIsNoOp()
    {
        var (store, ids) = MakeWorkspaceReorderTree();
        store.ReorderWorkspace(ids[0], ReorderDirection.Up);
        store.ReorderWorkspace(ids[0], ReorderDirection.Top);
        store.ReorderWorkspace(ids[2], ReorderDirection.Down);
        store.ReorderWorkspace(ids[2], ReorderDirection.Bottom);
        Assert.Equal(ids, store.Workspaces.Select(w => w.Id));
    }

    [Fact]
    public void ReorderWorkspaceKeepsSelectedSession()
    {
        var (store, ids) = MakeWorkspaceReorderTree();
        var session = store.AddSession(ids[0], "/a")!;
        store.SelectSession(session.Id);
        store.ReorderWorkspace(ids[0], ReorderDirection.Bottom);
        Assert.Equal(new[] { ids[1], ids[2], ids[0] }, store.Workspaces.Select(w => w.Id));
        Assert.Equal(session.Id, store.SelectedSessionId);
    }

    [Fact]
    public void MoveWorkspaceKeepsSelectedSession()
    {
        var (store, ids) = MakeWorkspaceReorderTree();
        var session = store.AddSession(ids[1], "/a")!;
        store.SelectSession(session.Id);
        store.MoveWorkspace(ids[1], at: 0);
        Assert.Equal(new[] { ids[1], ids[0], ids[2] }, store.Workspaces.Select(w => w.Id));
        Assert.Equal(session.Id, store.SelectedSessionId);
    }

    [Fact]
    public void ReorderOrderSurvivesSnapshotRestore()
    {
        var store = AppStoreFixtures.MakeStore();
        var w0 = store.AddWorkspace("w0");
        var w1 = store.AddWorkspace("w1");
        var a = store.AddSession(w0.Id, "/a")!;
        var b = store.AddSession(w0.Id, "/b")!;
        var c = store.AddSession(w0.Id, "/c")!;
        store.ReorderSession(a.Id, ReorderDirection.Bottom); // sessions -> [b, c, a]
        store.ReorderWorkspace(w1.Id, ReorderDirection.Top); // workspaces -> [w1, w0]

        var snap = store.Snapshot();
        var restored = AppStoreFixtures.MakeStore();
        restored.Restore(snap);
        Assert.Equal(new[] { w1.Id, w0.Id }, restored.Workspaces.Select(w => w.Id));
        Assert.Equal(new[] { b.Id, c.Id, a.Id }, restored.Workspaces[1].Sessions.Select(s => s.Id));
    }
}
