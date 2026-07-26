using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported 1:1 from agtermCore AppStoreNavigationTests.swift.</summary>
public sealed class AppStoreNavigationTests
{
    [Fact]
    public void NavigateSessionStaysWithinFocusedWorkspace()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var a = store.AddSession(work.Id, "/a")!;
        _ = store.AddSession(personal.Id, "/b")!;
        store.SelectSession(a.Id);
        store.SetFocusedWorkspace(work.Id);
        store.NavigateSession(SessionNavigation.Next); // nav is scoped to the focused workspace (only a); wrap cycles to itself
        Assert.Equal(a.Id, store.SelectedSessionId); // stays on a — the off-focus session is never revealed
        Assert.Equal(work.Id, store.FocusedWorkspaceId); // nav never crosses the focus boundary, so focus stands
    }

    /// <summary>Builds a two-workspace tree (work: a, b; personal: c, d) so flattened order is [a, b, c, d].</summary>
    private static (AppStore Store, List<Guid> Ids) MakeNavTree()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var a = store.AddSession(work.Id, "/a")!;
        var b = store.AddSession(work.Id, "/b")!;
        var c = store.AddSession(personal.Id, "/c")!;
        var d = store.AddSession(personal.Id, "/d")!;
        return (store, new List<Guid> { a.Id, b.Id, c.Id, d.Id });
    }

    [Fact]
    public void NavigateNextStepsForwardCrossingWorkspaces()
    {
        var (store, ids) = MakeNavTree();
        store.SelectSession(ids[0]);
        store.NavigateSession(SessionNavigation.Next);
        Assert.Equal(ids[1], store.SelectedSessionId);
        store.NavigateSession(SessionNavigation.Next); // crosses from work into personal
        Assert.Equal(ids[2], store.SelectedSessionId);
        store.NavigateSession(SessionNavigation.Next);
        Assert.Equal(ids[3], store.SelectedSessionId);
    }

    [Fact]
    public void NavigatePreviousStepsBackwardCrossingWorkspaces()
    {
        var (store, ids) = MakeNavTree();
        store.SelectSession(ids[3]);
        store.NavigateSession(SessionNavigation.Previous);
        Assert.Equal(ids[2], store.SelectedSessionId);
        store.NavigateSession(SessionNavigation.Previous); // crosses from personal into work
        Assert.Equal(ids[1], store.SelectedSessionId);
        store.NavigateSession(SessionNavigation.Previous);
        Assert.Equal(ids[0], store.SelectedSessionId);
    }

    [Fact]
    public void NavigateNextAtLastWrapsToFirst()
    {
        var (store, ids) = MakeNavTree();
        store.SelectSession(ids[^1]);
        store.NavigateSession(SessionNavigation.Next); // at the end: wraps around to the first
        Assert.Equal(ids[0], store.SelectedSessionId);
    }

    [Fact]
    public void NavigatePreviousAtFirstWrapsToLast()
    {
        var (store, ids) = MakeNavTree();
        store.SelectSession(ids[0]);
        store.NavigateSession(SessionNavigation.Previous); // at the start: wraps around to the last
        Assert.Equal(ids[^1], store.SelectedSessionId);
    }

    [Fact]
    public void NavigateFirstAndLastJumpToEnds()
    {
        var (store, ids) = MakeNavTree();
        store.SelectSession(ids[1]);
        store.NavigateSession(SessionNavigation.Last);
        Assert.Equal(ids[^1], store.SelectedSessionId);
        store.NavigateSession(SessionNavigation.First);
        Assert.Equal(ids[0], store.SelectedSessionId);
    }

    [Fact]
    public void NavigateWithNoSelectionLandsOnFirst()
    {
        var (store, ids) = MakeNavTree();
        store.SelectSession(null);
        store.NavigateSession(SessionNavigation.Next);
        Assert.Equal(ids[0], store.SelectedSessionId);
        store.SelectSession(null);
        store.NavigateSession(SessionNavigation.Previous);
        Assert.Equal(ids[0], store.SelectedSessionId);
    }

    [Fact]
    public void NavigateFirstAndLastWithNoSelectionLandOnEnds()
    {
        var (store, ids) = MakeNavTree();
        store.SelectSession(null);
        store.NavigateSession(SessionNavigation.Last);
        Assert.Equal(ids[^1], store.SelectedSessionId); // .last ignores the (absent) selection
        store.SelectSession(null);
        store.NavigateSession(SessionNavigation.First);
        Assert.Equal(ids[0], store.SelectedSessionId); // .first ignores the (absent) selection
    }

    [Fact]
    public void NavigateSingleSessionStaysSelected()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var only = store.AddSession(ws.Id, "/only")!;
        store.SelectSession(only.Id);
        foreach (var direction in new[]
                 {
                     SessionNavigation.Next, SessionNavigation.Previous,
                     SessionNavigation.First, SessionNavigation.Last,
                 })
        {
            store.NavigateSession(direction);
            Assert.Equal(only.Id, store.SelectedSessionId);
        }
    }

    [Fact]
    public void NavigateEmptyTreeIsNoOp()
    {
        var store = AppStoreFixtures.MakeStore();
        store.AddWorkspace("work"); // no sessions
        store.NavigateSession(SessionNavigation.Next);
        Assert.Null(store.SelectedSessionId);
        store.NavigateSession(SessionNavigation.First);
        Assert.Null(store.SelectedSessionId);
    }

    [Fact]
    public void NavigateRoutesThroughSelectSession()
    {
        var (store, ids) = MakeNavTree();
        store.SessionWithId(ids[1])!.UnseenCount = 5;
        store.SelectSession(ids[0]);
        store.NavigateSession(SessionNavigation.Next);
        Assert.Equal(ids[1], store.SelectedSessionId);
        Assert.Equal(0, store.SessionWithId(ids[1])!.UnseenCount); // selectSession cleared the badge
        // the navigation pushed a NEW recency entry on top of the prior selection (most-recent first).
        Assert.Equal(new[] { ids[1], ids[0] }, store.SessionRecency.Items.Take(2));
    }

    [Fact]
    public void SessionNavigationWireMapping()
    {
        Assert.Equal(SessionNavigation.Next, SessionNavigations.FromWire("next"));
        Assert.Equal(SessionNavigation.Previous, SessionNavigations.FromWire("prev"));
        Assert.Equal(SessionNavigation.Previous, SessionNavigations.FromWire("previous"));
        Assert.Equal(SessionNavigation.First, SessionNavigations.FromWire("first"));
        Assert.Equal(SessionNavigation.Last, SessionNavigations.FromWire("last"));
        Assert.Equal(SessionNavigation.NextAttention, SessionNavigations.FromWire("next-attention"));
        Assert.Equal(SessionNavigation.PreviousAttention, SessionNavigations.FromWire("prev-attention"));
        Assert.Equal(SessionNavigation.PreviousAttention, SessionNavigations.FromWire("previous-attention"));
        Assert.Null(SessionNavigations.FromWire("sideways"));
    }

    [Fact]
    public void NavigateAttentionStepsThroughAttentionSessionsOnly()
    {
        var (store, ids) = MakeNavTree(); // a, b, c, d
        store.SessionWithId(ids[1])!.AgentIndicator = new AgentIndicator(AgentStatus.Blocked);   // b
        store.SessionWithId(ids[3])!.AgentIndicator = new AgentIndicator(AgentStatus.Completed); // d
        store.SelectSession(ids[0]); // a (idle)
        store.NavigateSession(SessionNavigation.NextAttention);
        Assert.Equal(ids[1], store.SelectedSessionId); // skips to blocked b
        store.NavigateSession(SessionNavigation.NextAttention);
        Assert.Equal(ids[3], store.SelectedSessionId); // skips idle c, lands on completed d
    }

    [Fact]
    public void NavigateAttentionWrapsAround()
    {
        var (store, ids) = MakeNavTree();
        store.SessionWithId(ids[1])!.AgentIndicator = new AgentIndicator(AgentStatus.Blocked);   // b
        store.SessionWithId(ids[3])!.AgentIndicator = new AgentIndicator(AgentStatus.Completed); // d
        store.SelectSession(ids[3]); // d (last attention)
        store.NavigateSession(SessionNavigation.NextAttention);
        Assert.Equal(ids[1], store.SelectedSessionId); // wraps forward to b
        store.NavigateSession(SessionNavigation.PreviousAttention);
        Assert.Equal(ids[3], store.SelectedSessionId); // wraps backward to d
    }

    [Fact]
    public void NavigateAttentionSkipsActiveAndIdle()
    {
        var (store, ids) = MakeNavTree();
        store.SessionWithId(ids[1])!.AgentIndicator = new AgentIndicator(AgentStatus.Active);  // b active - excluded
        store.SessionWithId(ids[2])!.AgentIndicator = new AgentIndicator(AgentStatus.Blocked); // c blocked - included
        store.SelectSession(ids[0]);
        store.NavigateSession(SessionNavigation.NextAttention);
        Assert.Equal(ids[2], store.SelectedSessionId); // skips active b, lands on blocked c
    }

    [Fact]
    public void NavigateAttentionWithSingleAttentionSessionStaysPut()
    {
        var (store, ids) = MakeNavTree();
        store.SessionWithId(ids[2])!.AgentIndicator = new AgentIndicator(AgentStatus.Completed); // c, the only one
        store.SelectSession(ids[2]);
        store.NavigateSession(SessionNavigation.NextAttention);
        Assert.Equal(ids[2], store.SelectedSessionId); // no other attention session -> no-op
        store.NavigateSession(SessionNavigation.PreviousAttention);
        Assert.Equal(ids[2], store.SelectedSessionId);
    }

    [Fact]
    public void NavigateAttentionWithNoAttentionSessionsIsNoOp()
    {
        var (store, ids) = MakeNavTree();
        store.SelectSession(ids[0]);
        store.NavigateSession(SessionNavigation.NextAttention);
        Assert.Equal(ids[0], store.SelectedSessionId);
    }

    [Fact]
    public void NavigateAttentionWithNoSelectionLandsOnAnAttentionEnd()
    {
        var (store, ids) = MakeNavTree();
        store.SessionWithId(ids[1])!.AgentIndicator = new AgentIndicator(AgentStatus.Blocked);   // b
        store.SessionWithId(ids[3])!.AgentIndicator = new AgentIndicator(AgentStatus.Completed); // d
        store.SelectSession(null);
        store.NavigateSession(SessionNavigation.NextAttention);
        Assert.Equal(ids[1], store.SelectedSessionId); // forward from before-first -> first attention
        store.SelectSession(null);
        store.NavigateSession(SessionNavigation.PreviousAttention);
        Assert.Equal(ids[3], store.SelectedSessionId); // backward from after-last -> last attention
    }

    // MARK: - navigation scoping (filtered set)

    [Fact]
    public void NavigableSessionsReflectsFlaggedFocusAndUnfocused()
    {
        var (store, ids) = MakeNavTree(); // work={a,b}, personal={c,d}
        Assert.Equal(ids, store.NavigableSessions.Select(s => s.Id)); // unfocused tree mode -> all sessions
        var work = store.WorkspaceForSession(ids[0])!;
        store.SetFocusedWorkspace(work.Id);
        Assert.Equal(new[] { ids[0], ids[1] }, store.NavigableSessions.Select(s => s.Id)); // focused -> only that workspace
        store.SetFocusedWorkspace(Guid.NewGuid()); // a stale focus id falls back to all
        Assert.Equal(ids, store.NavigableSessions.Select(s => s.Id));
        store.SetFocusedWorkspace(null);
        store.SetFlag(true, ids[1]);
        store.SetFlag(true, ids[2]);
        store.SetSidebarMode(SidebarMode.Flagged);
        Assert.Equal(new[] { ids[1], ids[2] }, store.NavigableSessions.Select(s => s.Id)); // flagged mode -> the flagged set
    }

    [Fact]
    public void NavigateScopesToFocusedWorkspace()
    {
        var (store, ids) = MakeNavTree(); // work={a,b}, personal={c,d}
        var work = store.WorkspaceForSession(ids[0])!;
        store.SetFocusedWorkspace(work.Id);
        store.SelectSession(ids[0]);
        store.NavigateSession(SessionNavigation.Next);
        Assert.Equal(ids[1], store.SelectedSessionId); // a -> b within the focused workspace
        store.NavigateSession(SessionNavigation.Next);
        Assert.Equal(ids[0], store.SelectedSessionId); // b is the in-set last: wraps within {a,b} to a, never crosses to c
        store.NavigateSession(SessionNavigation.Last);
        Assert.Equal(ids[1], store.SelectedSessionId); // .last is the focused workspace's last, not the tree's
        store.NavigateSession(SessionNavigation.First);
        Assert.Equal(ids[0], store.SelectedSessionId); // .first is the focused workspace's first
        Assert.Equal(work.Id, store.FocusedWorkspaceId); // never auto-unfocuses — every target was in-set
    }

    [Fact]
    public void NavigateScopesToFlaggedSet()
    {
        var (store, ids) = MakeNavTree(); // a, b, c, d
        store.SetFlag(true, ids[0]); // a
        store.SetFlag(true, ids[2]); // c
        store.SetSidebarMode(SidebarMode.Flagged);
        store.SelectSession(ids[0]);
        store.NavigateSession(SessionNavigation.Next);
        Assert.Equal(ids[2], store.SelectedSessionId); // a -> c, skipping the unflagged b
        store.NavigateSession(SessionNavigation.Next);
        Assert.Equal(ids[0], store.SelectedSessionId); // c is the flagged-set last: wraps to a, the flagged-set first
        store.NavigateSession(SessionNavigation.First);
        Assert.Equal(ids[0], store.SelectedSessionId); // .first is the flagged set's first
    }

    [Fact]
    public void NavigateAttentionScopesToFocusedWorkspace()
    {
        var (store, ids) = MakeNavTree(); // work={a,b}, personal={c,d}
        store.SessionWithId(ids[1])!.AgentIndicator = new AgentIndicator(AgentStatus.Blocked);   // b (in focus)
        store.SessionWithId(ids[3])!.AgentIndicator = new AgentIndicator(AgentStatus.Completed); // d (off focus)
        var work = store.WorkspaceForSession(ids[0])!;
        store.SetFocusedWorkspace(work.Id);
        store.SelectSession(ids[0]);
        store.NavigateSession(SessionNavigation.NextAttention);
        Assert.Equal(ids[1], store.SelectedSessionId); // only b is in the focused set; d is never reached
        store.NavigateSession(SessionNavigation.NextAttention);
        Assert.Equal(ids[1], store.SelectedSessionId); // b is the single in-set attention session: stays
    }

    [Fact]
    public void NavigateRestoresFullSetWhenFocusCleared()
    {
        var (store, ids) = MakeNavTree();
        var work = store.WorkspaceForSession(ids[0])!;
        store.SetFocusedWorkspace(work.Id);
        store.SelectSession(ids[1]); // b, the in-set last of {a,b}
        store.NavigateSession(SessionNavigation.Next);
        Assert.Equal(ids[0], store.SelectedSessionId); // scoped: wraps within {a,b} back to a, never crosses to c
        store.SetFocusedWorkspace(null); // clearing focus restores the full navigable set
        store.SelectSession(ids[1]); // b again, now in the full set [a,b,c,d]
        store.NavigateSession(SessionNavigation.Next);
        Assert.Equal(ids[2], store.SelectedSessionId); // now crosses into the personal workspace
    }

    // MARK: - attentionSessions

    [Fact]
    public void AttentionSessionsFiltersOutIdle()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        _ = store.AddSession(ws.Id, "/idle"); // stays idle
        var active = store.AddSession(ws.Id, "/active")!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), active.Id);
        // idle sessions are dropped; only the non-idle one is listed
        Assert.Equal(new[] { active.Id }, store.AttentionSessions.Select(s => s.Id));
    }

    [Fact]
    public void AttentionSessionsOrderBlockedActiveCompleted()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var completed = store.AddSession(ws.Id, "/c")!;
        var active = store.AddSession(ws.Id, "/a")!;
        var blocked = store.AddSession(ws.Id, "/b")!;
        // set in a non-rank order; the list must still sort blocked -> active -> completed
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Completed), completed.Id);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), active.Id);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), blocked.Id);
        Assert.Equal(new[] { blocked.Id, active.Id, completed.Id },
            store.AttentionSessions.Select(s => s.Id));
    }

    [Fact]
    public void AttentionSessionsWithinRankOrderNewestFirstNilLast()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var oldest = store.AddSession(ws.Id, "/old")!;
        var newest = store.AddSession(ws.Id, "/new")!;
        var unstamped = store.AddSession(ws.Id, "/none")!;
        // all three are blocked (same rank), so the tie-break is statusChangedAt descending, nil last
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), oldest.Id);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), newest.Id);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), unstamped.Id);
        oldest.StatusChangedAt = DateTimeOffset.FromUnixTimeSeconds(100);
        newest.StatusChangedAt = DateTimeOffset.FromUnixTimeSeconds(200);
        unstamped.StatusChangedAt = null; // a missing stamp sorts last within the rank group
        Assert.Equal(new[] { newest.Id, oldest.Id, unstamped.Id },
            store.AttentionSessions.Select(s => s.Id));
    }

    [Fact]
    public void AttentionSessionsTieBreakStableForEqualAndNilStamps()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var stampedA = store.AddSession(ws.Id, "/sa")!;
        var stampedB = store.AddSession(ws.Id, "/sb")!;
        var nilA = store.AddSession(ws.Id, "/na")!;
        var nilB = store.AddSession(ws.Id, "/nb")!;
        // all blocked (same rank); two share an equal stamp, two share a nil stamp — exercising the
        // comparator's (l?, r?)-equal and (nil, nil) branches (both return false). the stamped pair still
        // precedes the nil pair, and within each tie group the stable sort keeps insertion order.
        foreach (var s in new[] { stampedA, stampedB, nilA, nilB })
            store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), s.Id);
        stampedA.StatusChangedAt = DateTimeOffset.FromUnixTimeSeconds(500);
        stampedB.StatusChangedAt = DateTimeOffset.FromUnixTimeSeconds(500); // equal stamp -> stable, keeps order
        nilA.StatusChangedAt = null;
        nilB.StatusChangedAt = null;
        Assert.Equal(new[] { stampedA.Id, stampedB.Id, nilA.Id, nilB.Id },
            store.AttentionSessions.Select(s => s.Id));
    }

    [Fact]
    public void AttentionSessionsSpanAllWorkspacesIgnoringFocusAndFlagged()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var other = store.AddWorkspace("other");
        var here = store.AddSession(work.Id, "/here")!;
        var away = store.AddSession(other.Id, "/away")!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), here.Id);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), away.Id);
        // focusing one workspace must NOT shrink the list — a blocked session in another workspace still shows
        store.SetFocusedWorkspace(work.Id);
        Assert.Equal(new[] { away.Id, here.Id }, store.AttentionSessions.Select(s => s.Id)); // blocked(away) before active(here)
        // flagged mode is likewise ignored: nothing is flagged, but the non-idle sessions still list
        store.SetSidebarMode(SidebarMode.Flagged);
        Assert.Equal(new[] { away.Id, here.Id }, store.AttentionSessions.Select(s => s.Id));
    }

    [Fact]
    public void AttentionSessionsEmptyWhenAllIdle()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        _ = store.AddSession(ws.Id, "/a");
        _ = store.AddSession(ws.Id, "/b");
        // no statuses set: every session is idle, so the attention list is empty
        Assert.Empty(store.AttentionSessions);
    }
}
