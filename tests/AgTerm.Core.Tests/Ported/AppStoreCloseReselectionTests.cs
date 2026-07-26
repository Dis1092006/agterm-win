using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Ported;

/// <summary>
/// Ported 1:1 from agtermCore/Tests/agtermCoreTests/AppStoreCloseReselectionTests.swift.
///
/// Closing the ACTIVE session returns to the most-recently-active SURVIVING session, scoped to the
/// closing session's workspace and — in `.flagged` sidebar mode — to the flagged set, with the
/// positional `reselectionTarget` as the fallback (GitHub Discussion #147). The FOCUS filter
/// deliberately does NOT scope the pick; see `CloseReselectionTarget(after:)`.
/// </summary>
public class AppStoreCloseReselectionTests
{
    [Fact]
    public void CloseActiveSessionInsertedAfterCurrentReturnsToTheSessionItCameFrom()
    {
        // discussion example 1: from `1 2 3` on `1`, a new session inserted after the current one
        // (`1 4 2 3`) and closed lands back on `1` — not on the positional neighbor `2`.
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace(name: "work");
        var one = store.AddSession(ws.Id, "/1");
        Assert.NotNull(one);
        Assert.NotNull(store.AddSession(ws.Id, "/2"));
        Assert.NotNull(store.AddSession(ws.Id, "/3"));
        store.SelectSession(one!.Id);
        var four = store.AddSession(ws.Id, "/4", at: 1);
        Assert.NotNull(four);
        Assert.Equal(four!.Id, store.SelectedSessionId);

        store.CloseSession(four.Id);
        Assert.Equal(one.Id, store.SelectedSessionId); // not `2`, which shifted into the removed slot
    }

    [Fact]
    public void CloseActiveSessionAppendedAtTheEndReturnsToTheSessionItCameFrom()
    {
        // discussion example 2: from `1 2 3` on `1`, a new session appended (`1 2 3 4`) and closed
        // lands back on `1` — not on the positional previous `3`.
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace(name: "work");
        var one = store.AddSession(ws.Id, "/1");
        Assert.NotNull(one);
        Assert.NotNull(store.AddSession(ws.Id, "/2"));
        Assert.NotNull(store.AddSession(ws.Id, "/3"));
        store.SelectSession(one!.Id);
        var four = store.AddSession(ws.Id, "/4");
        Assert.NotNull(four);

        store.CloseSession(four!.Id);
        Assert.Equal(one.Id, store.SelectedSessionId); // not `3`, the positional previous
    }

    [Fact]
    public void CloseActiveSessionPrefersTheRecentSurvivorOverThePositionalNeighbor()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace(name: "work");
        var first = store.AddSession(ws.Id, "/a");
        Assert.NotNull(first);
        var second = store.AddSession(ws.Id, "/b");
        Assert.NotNull(second);
        Assert.NotNull(store.AddSession(ws.Id, "/c"));
        var fourth = store.AddSession(ws.Id, "/d");
        Assert.NotNull(fourth);
        store.SelectSession(first!.Id);
        store.SelectSession(second!.Id);
        store.SelectSession(fourth!.Id);

        store.CloseSession(fourth.Id);
        Assert.Equal(second.Id, store.SelectedSessionId); // the MRU survivor, though `c` is the neighbor
    }

    [Fact]
    public void CloseActiveSessionIgnoresAMoreRecentSessionInAnotherWorkspace()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace(name: "work");
        var personal = store.AddWorkspace(name: "personal");
        var inWork = store.AddSession(work.Id, "/a");
        Assert.NotNull(inWork);
        var closing = store.AddSession(work.Id, "/b");
        Assert.NotNull(closing);
        var elsewhere = store.AddSession(personal.Id, "/x");
        Assert.NotNull(elsewhere);
        store.SelectSession(inWork!.Id);
        store.SelectSession(elsewhere!.Id); // more recent than `inWork`, but in another workspace
        store.SelectSession(closing!.Id);

        store.CloseSession(closing.Id);
        Assert.Equal(inWork.Id, store.SelectedSessionId); // stays put, though `elsewhere` is more recent
        Assert.Null(store.FocusedWorkspaceId); // the close must not introduce a focus filter
    }

    [Fact]
    public void CloseActiveSessionEmptyingItsWorkspacePicksTheRecentSurvivorElsewhere()
    {
        // the workspace scope has nothing left to hold on to once the close empties the workspace, so the
        // MRU widens to the whole TREE rather than jumping positionally into the first workspace.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace(name: "work");
        var scratch = store.AddWorkspace(name: "scratch");
        Assert.NotNull(store.AddSession(work.Id, "/a"));
        var cameFrom = store.AddSession(work.Id, "/b");
        Assert.NotNull(cameFrom);
        var onlyInScratch = store.AddSession(scratch.Id, "/x");
        Assert.NotNull(onlyInScratch);
        store.SelectSession(cameFrom!.Id);
        store.SelectSession(onlyInScratch!.Id);

        store.CloseSession(onlyInScratch.Id);
        Assert.Empty(store.Workspaces[1].Sessions);
        Assert.Equal(cameFrom.Id, store.SelectedSessionId); // not `work`'s positionally-first session `/a`
    }

    [Fact]
    public void CloseActiveSessionWithAFocusFilterStaysInsideTheFocusedWorkspace()
    {
        var store = AppStoreFixtures.MakeStore();
        var personal = store.AddWorkspace(name: "personal");
        var work = store.AddWorkspace(name: "work");
        Assert.NotNull(store.AddSession(personal.Id, "/x"));
        var first = store.AddSession(work.Id, "/a");
        Assert.NotNull(first);
        Assert.NotNull(store.AddSession(work.Id, "/b"));
        var closing = store.AddSession(work.Id, "/c");
        Assert.NotNull(closing);
        store.SelectSession(first!.Id);
        store.SelectSession(closing!.Id);
        store.SetFocusedWorkspace(work.Id);

        store.CloseSession(closing.Id);
        Assert.Equal(first.Id, store.SelectedSessionId); // the MRU survivor, not the positional neighbor `/b`
        Assert.Equal(work.Id, store.FocusedWorkspaceId); // the filter survives the close
    }

    [Fact]
    public void CloseTheFocusedWorkspacesLastSessionPicksTheRecentSurvivorElsewhere()
    {
        // the focus filter must NOT scope the widened MRU: under focus, `navigableSessions` collapses to the
        // focused workspace, so scoping by it would widen into an EMPTY set once the close empties that
        // workspace and fall through to a positional jump into the FIRST workspace — the very disorientation
        // this feature removes. `AutoUnfocusIfOutsideFocus` drops the now-meaningless filter instead.
        var store = AppStoreFixtures.MakeStore();
        var personal = store.AddWorkspace(name: "personal");
        var work = store.AddWorkspace(name: "work");
        Assert.NotNull(store.AddSession(personal.Id, "/first"));
        var cameFrom = store.AddSession(personal.Id, "/mru");
        Assert.NotNull(cameFrom);
        var onlyInWork = store.AddSession(work.Id, "/only");
        Assert.NotNull(onlyInWork);
        store.SelectSession(cameFrom!.Id);
        store.SelectSession(onlyInWork!.Id);
        store.SetFocusedWorkspace(work.Id);

        store.CloseSession(onlyInWork.Id);
        Assert.Empty(store.Workspaces[1].Sessions);
        Assert.Equal(cameFrom.Id, store.SelectedSessionId); // not `/first`, the positional first-workspace jump
        Assert.Null(store.FocusedWorkspaceId); // the emptied focus is dropped to reveal the pick
    }

    [Fact]
    public void CloseActiveSessionWhileAnotherWorkspaceIsFocusedStaysInTheClosingWorkspace()
    {
        // `SetFocusedWorkspace` never moves the selection, so focus can sit on a workspace the ACTIVE session
        // doesn't belong to. Scoping the MRU by focus would yank the close into the focused workspace; the
        // pick must stay next to the session that closed, exactly as the positional neighbor used to.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace(name: "work");
        var personal = store.AddWorkspace(name: "personal");
        var cameFrom = store.AddSession(work.Id, "/a");
        Assert.NotNull(cameFrom);
        Assert.NotNull(store.AddSession(work.Id, "/b"));
        var closing = store.AddSession(work.Id, "/c");
        Assert.NotNull(closing);
        var elsewhere = store.AddSession(personal.Id, "/x");
        Assert.NotNull(elsewhere);
        store.SelectSession(elsewhere!.Id); // seeds `personal` into the recency stack
        store.SelectSession(cameFrom!.Id);
        store.SelectSession(closing!.Id);
        store.SetFocusedWorkspace(personal.Id); // focus elsewhere, active session still in `work`

        store.CloseSession(closing.Id);
        Assert.Equal(cameFrom.Id, store.SelectedSessionId); // not `elsewhere`, in the focused workspace
        Assert.Null(store.FocusedWorkspaceId); // the pick is outside the focus, so the filter drops
    }

    [Fact]
    public void CloseActiveSessionInFlaggedModeStaysWithinTheFlaggedSet()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace(name: "work");
        var flagged = store.AddSession(ws.Id, "/a");
        Assert.NotNull(flagged);
        var unflagged = store.AddSession(ws.Id, "/b");
        Assert.NotNull(unflagged);
        var closing = store.AddSession(ws.Id, "/c");
        Assert.NotNull(closing);
        store.SetFlag(true, flagged!.Id);
        store.SetFlag(true, closing!.Id);
        store.SidebarMode = SidebarMode.Flagged;
        store.SelectSession(flagged.Id);
        store.SelectSession(unflagged!.Id); // more recent, but outside the flagged view
        store.SelectSession(closing.Id);

        store.CloseSession(closing.Id);
        Assert.Equal(flagged.Id, store.SelectedSessionId); // not `unflagged`, though it is the more recent
    }

    [Fact]
    public void CloseActiveSessionInFlaggedModeCrossesWorkspacesRatherThanLeavingTheFlaggedSet()
    {
        // `FlaggedSessions` is cross-workspace by definition, so when the close leaves the closing
        // session's workspace with no flagged survivor, the pick follows the FILTER out of the workspace
        // instead of falling back to an unflagged sibling the sidebar isn't even rendering.
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace(name: "work");
        var personal = store.AddWorkspace(name: "personal");
        var unflagged = store.AddSession(work.Id, "/a");
        Assert.NotNull(unflagged);
        var closing = store.AddSession(work.Id, "/b");
        Assert.NotNull(closing);
        var flaggedElsewhere = store.AddSession(personal.Id, "/x");
        Assert.NotNull(flaggedElsewhere);
        store.SetFlag(true, closing!.Id);
        store.SetFlag(true, flaggedElsewhere!.Id);
        store.SidebarMode = SidebarMode.Flagged;
        store.SelectSession(flaggedElsewhere.Id);
        store.SelectSession(unflagged!.Id); // the most recent overall, but outside the flagged view
        store.SelectSession(closing.Id);

        store.CloseSession(closing.Id);
        Assert.Equal(flaggedElsewhere.Id, store.SelectedSessionId); // the only flagged survivor
        Assert.NotNull(store.SelectedSessionId);
        Assert.Contains(store.SelectedSessionId!.Value, store.FlaggedSessions.Select(s => s.Id));
    }

    [Fact]
    public void CloseActiveSessionInFlaggedModeWithAnEmptyScopedRecencyStaysWithinTheFlaggedSet()
    {
        // a cold restore in flagged mode: once the closing session is pruned the scoped recency is empty,
        // so the pick falls back — but the positional neighbor is unflagged and the flagged sidebar has no
        // row for it, so the fallback must stay inside the filter.
        var store = AppStoreFixtures.MakeStore();
        var wsId = Guid.NewGuid();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var sessions = new[]
        {
            new SessionSnapshot { Id = ids[0], Cwd = "/a", Flagged = true },
            new SessionSnapshot { Id = ids[1], Cwd = "/b", Flagged = true },
            new SessionSnapshot { Id = ids[2], Cwd = "/c" },
        };
        store.Restore(new Snapshot
        {
            SelectedSessionId = ids[1],
            Workspaces = new[] { new WorkspaceSnapshot { Id = wsId, Name = "work", Sessions = sessions } },
            SidebarMode = SidebarMode.Flagged,
        });
        Assert.Equal(new[] { ids[1] }, store.SessionRecency.Items); // only the restored selection

        store.CloseSession(ids[1]);
        Assert.Equal(ids[0], store.SelectedSessionId); // the flagged survivor, not the unflagged `ids[2]`
        Assert.NotNull(store.SelectedSessionId);
        Assert.Contains(store.SelectedSessionId!.Value, store.FlaggedSessions.Select(s => s.Id));
    }

    [Fact]
    public void CloseActiveSessionInFlaggedModeWithAnEmptyScopedRecencyPicksTheNEARESTFlaggedSurvivor()
    {
        // the filtered fallback is positional WITHIN the flagged set, not "the first flagged row": closing a
        // session in the middle of the flagged list must land on the flagged row that shifted into its slot,
        // the same locality the unfiltered positional fallback has.
        var store = AppStoreFixtures.MakeStore();
        var wsId = Guid.NewGuid();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var sessions = ids.Select(id => new SessionSnapshot { Id = id, Cwd = $"/{id}", Flagged = true }).ToArray();
        store.Restore(new Snapshot
        {
            SelectedSessionId = ids[1],
            Workspaces = new[] { new WorkspaceSnapshot { Id = wsId, Name = "work", Sessions = sessions } },
            SidebarMode = SidebarMode.Flagged,
        });
        Assert.Equal(new[] { ids[1] }, store.SessionRecency.Items); // only the restored selection

        store.CloseSession(ids[1]);
        Assert.Equal(ids[2], store.SelectedSessionId); // the neighbor that shifted up, not the top of the list
    }

    [Fact]
    public void CloseTheWorkspacesLastFlaggedSessionWithAnEmptyRecencyPicksTheADJACENTFlaggedRow()
    {
        // the scope widens across workspaces once the close leaves the closing workspace with no flagged
        // survivor, and the flagged sidebar renders that widened set as one flat list — so the fallback must
        // stay positional THERE too: the flagged row adjacent to the closed one, not the top of the list.
        var store = AppStoreFixtures.MakeStore();
        var first = Guid.NewGuid();
        var last = Guid.NewGuid();
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var flaggedElsewhere = new[]
        {
            new SessionSnapshot { Id = ids[0], Cwd = "/a", Flagged = true },
            new SessionSnapshot { Id = ids[1], Cwd = "/b", Flagged = true },
        };
        var closing = new[]
        {
            new SessionSnapshot { Id = ids[2], Cwd = "/c" },
            new SessionSnapshot { Id = ids[3], Cwd = "/d", Flagged = true },
        };
        store.Restore(new Snapshot
        {
            SelectedSessionId = ids[3],
            Workspaces = new[]
            {
                new WorkspaceSnapshot { Id = first, Name = "work", Sessions = flaggedElsewhere },
                new WorkspaceSnapshot { Id = last, Name = "personal", Sessions = closing },
            },
            SidebarMode = SidebarMode.Flagged,
        });
        Assert.Equal(new[] { ids[3] }, store.SessionRecency.Items); // only the restored selection

        store.CloseSession(ids[3]);
        Assert.Equal(ids[1], store.SelectedSessionId); // the flagged row before it, not the first flagged row
    }

    [Fact]
    public void CloseTheLastFlaggedSessionFallsThroughToThePositionalTarget()
    {
        // the flagged scope is empty once the only flagged session is the one closing, so the filtered
        // fallback has nothing to return: the positional pick stands rather than selecting nothing (the
        // flagged sidebar renders no rows at all in this state, so there is no in-filter row to land on).
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace(name: "work");
        Assert.NotNull(store.AddSession(ws.Id, "/a"));
        var closing = store.AddSession(ws.Id, "/b");
        Assert.NotNull(closing);
        var after = store.AddSession(ws.Id, "/c");
        Assert.NotNull(after);
        store.SetFlag(true, closing!.Id);
        store.SidebarMode = SidebarMode.Flagged;
        store.SelectSession(closing.Id);

        store.CloseSession(closing.Id);
        Assert.Empty(store.FlaggedSessions);
        Assert.Equal(after!.Id, store.SelectedSessionId); // the positional neighbor; nothing flagged survives
    }

    [Fact]
    public void CloseActiveSessionWithAnEmptyScopedRecencyFallsBackToThePositionalTarget()
    {
        // a cold restore: nothing has been activated, so once the closing session is pruned the scoped
        // recency is empty and the pick is exactly today's positional neighbor.
        var store = AppStoreFixtures.MakeStore();
        var wsId = Guid.NewGuid();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var sessions = ids.Select((id, i) => new SessionSnapshot { Id = id, Cwd = $"/{i}" }).ToArray();
        store.Restore(new Snapshot
        {
            SelectedSessionId = ids[1],
            Workspaces = new[] { new WorkspaceSnapshot { Id = wsId, Name = "work", Sessions = sessions } },
        });
        Assert.Equal(new[] { ids[1] }, store.SessionRecency.Items); // only the restored selection

        store.CloseSession(ids[1]);
        Assert.Equal(ids[2], store.SelectedSessionId); // the session that shifted into the removed slot
    }

    [Fact]
    public void SoftCloseActiveSessionPicksTheRecentSurvivorLikeTheHardClose()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace(name: "work");
        var first = store.AddSession(ws.Id, "/a");
        Assert.NotNull(first);
        Assert.NotNull(store.AddSession(ws.Id, "/b"));
        Assert.NotNull(store.AddSession(ws.Id, "/c"));
        var closing = store.AddSession(ws.Id, "/d");
        Assert.NotNull(closing);
        store.SelectSession(first!.Id);
        store.SelectSession(closing!.Id);

        Assert.True(store.SoftCloseSession(closing.Id, 60));
        Assert.Equal(first.Id, store.SelectedSessionId); // the MRU survivor, not the positional neighbor `/c`
    }

    [Fact]
    public void SoftCloseActiveSessionWithAnEmptyScopedRecencyFallsBackToThePositionalTarget()
    {
        // the single soft close owns its own call into the helper, so pin its fallback leg too: a cold
        // restore leaves nothing in the recency stack but the session being closed.
        var store = AppStoreFixtures.MakeStore();
        var wsId = Guid.NewGuid();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var sessions = ids.Select((id, i) => new SessionSnapshot { Id = id, Cwd = $"/{i}" }).ToArray();
        store.Restore(new Snapshot
        {
            SelectedSessionId = ids[1],
            Workspaces = new[] { new WorkspaceSnapshot { Id = wsId, Name = "work", Sessions = sessions } },
        });

        Assert.True(store.SoftCloseSession(ids[1], 60));
        Assert.Equal(ids[2], store.SelectedSessionId); // the session that shifted into the removed slot
    }

    [Fact]
    public void SoftCloseSessionsNeverPicksAMemberOfTheClosingGroup()
    {
        // the soft-close paths deliberately leave the closing sessions in `SessionRecency` (undo needs
        // them back), so the MRU pick must be kept off them by the TREE-derived scope alone.
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace(name: "work");
        var oldest = store.AddSession(ws.Id, "/a");
        Assert.NotNull(oldest);
        var survivor = store.AddSession(ws.Id, "/b");
        Assert.NotNull(survivor);
        var alsoClosing = store.AddSession(ws.Id, "/c");
        Assert.NotNull(alsoClosing);
        var closing = store.AddSession(ws.Id, "/d");
        Assert.NotNull(closing);
        store.SelectSession(oldest!.Id);
        store.SelectSession(survivor!.Id);
        store.SelectSession(alsoClosing!.Id);
        store.SelectSession(closing!.Id);

        Assert.True(store.SoftCloseSessions(new[] { alsoClosing.Id, closing.Id }, 60));
        Assert.Equal(survivor.Id, store.SelectedSessionId); // the most recent survivor OUTSIDE the group
        // both closed sessions are more recent than `survivor` and still in the stack, yet unpickable
        Assert.Contains(closing.Id, store.SessionRecency.Items);
        Assert.Contains(alsoClosing.Id, store.SessionRecency.Items);
    }

    [Fact]
    public void UndoOfASoftCloseStillRestoresThePreviouslySelectedSession()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace(name: "work");
        var other = store.AddSession(ws.Id, "/a");
        Assert.NotNull(other);
        var closing = store.AddSession(ws.Id, "/b");
        Assert.NotNull(closing);
        store.SelectSession(other!.Id);
        store.SelectSession(closing!.Id);

        Assert.True(store.SoftCloseSession(closing.Id, 60));
        Assert.Equal(other.Id, store.SelectedSessionId);

        var summary = store.PendingCloseSummary;
        Assert.NotNull(summary);
        Assert.True(store.UndoPendingClose(summary!.Id));
        Assert.Same(closing, store.SessionWithId(closing.Id));
        Assert.Equal(closing.Id, store.SelectedSessionId); // undo reselects what was closed
    }

    [Fact]
    public async Task GraceExpiryAfterASoftCloseLeavesTheSelectionAlone()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace(name: "work");
        var survivor = store.AddSession(ws.Id, "/a");
        Assert.NotNull(survivor);
        var closing = store.AddSession(ws.Id, "/b");
        Assert.NotNull(closing);
        var surface = new SpySurface();
        closing!.Surface = surface;
        store.SelectSession(survivor!.Id);
        store.SelectSession(closing.Id);

        Assert.True(store.SoftCloseSession(closing.Id, 0.01));
        Assert.Equal(survivor.Id, store.SelectedSessionId);
        // poll the teardown rather than a flat sleep: the grace timer can land late under parallel load
        // platform adaptation: the finalize runs on a thread-pool timer (not the test's context), so
        // the recency prune can land a beat after the teardown — poll for BOTH before asserting.
        for (var i = 0; i < 200; i++)
        {
            if (surface.TeardownCount == 1 && !store.SessionRecency.Items.Contains(closing.Id)) break;
            await Task.Delay(TimeSpan.FromMilliseconds(5));
        }

        Assert.Equal(1, surface.TeardownCount);
        Assert.Equal(survivor.Id, store.SelectedSessionId); // finalization must not re-run reselection
        Assert.DoesNotContain(closing.Id, store.SessionRecency.Items); // finalize prunes it now
    }

    [Fact]
    public void CloseActiveSessionNeverClearsTheSelectionWhileSessionsSurvive()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace(name: "work");
        var personal = store.AddWorkspace(name: "personal");
        Assert.NotNull(store.AddSession(work.Id, "/a"));
        Assert.NotNull(store.AddSession(work.Id, "/b"));
        Assert.NotNull(store.AddSession(personal.Id, "/x"));

        for (var remaining = 3; remaining >= 2; remaining--)
        {
            var active = store.SelectedSessionId;
            Assert.NotNull(active);
            store.CloseSession(active!.Value);
            Assert.True(store.SelectedSessionId != null,
                $"{remaining - 1} sessions survive, selection must not go nil");
        }
        var last = store.SelectedSessionId;
        Assert.NotNull(last);
        store.CloseSession(last!.Value);
        Assert.Null(store.SelectedSessionId); // the tree is empty now
    }
}
