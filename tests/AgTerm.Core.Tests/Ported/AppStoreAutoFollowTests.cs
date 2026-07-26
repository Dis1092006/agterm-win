using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported 1:1 from agtermCore AppStoreAutoFollowTests.swift.</summary>
public sealed class AppStoreAutoFollowTests
{
    /// <summary>Adds a session to <paramref name="workspace"/>, marks it blocked, and pins its
    /// StatusChangedAt to a fixed time so FIFO ordering is deterministic (SetAgentIndicator stamps
    /// the current time, which is then overridden).</summary>
    private static Session AddBlocked(AppStore store, Guid workspace, string cwd, double time)
    {
        var session = store.AddSession(workspace, cwd)!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), session.Id);
        session.StatusChangedAt = DateTimeOffset.FromUnixTimeSeconds((long)time);
        return session;
    }

    // MARK: - AutoFollowTarget (pure decision)

    [Fact]
    public void AutoFollowTargetPicksOldestBlocked()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var older = AddBlocked(store, ws.Id, "/older", 100);
        var newer = AddBlocked(store, ws.Id, "/newer", 200);
        // order of the input array must not matter: min picks the earliest statusChangedAt (FIFO)
        Assert.Equal(older.Id, store.AutoFollowTarget(null, new[] { newer, older }, stayOnActive: false));
    }

    [Fact]
    public void AutoFollowTargetSuppressesWhenCurrentBlocked()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var current = AddBlocked(store, ws.Id, "/cur", 50);
        var other = AddBlocked(store, ws.Id, "/other", 200);
        // parked on a blocked session -> stay put, regardless of another older/newer block
        Assert.Null(store.AutoFollowTarget(current, new[] { current, other }, stayOnActive: false));
        Assert.Null(store.AutoFollowTarget(current, new[] { current, other }, stayOnActive: true));
    }

    [Fact]
    public void AutoFollowTargetSuppressesActiveOnlyWhenStayOnActive()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var current = store.AddSession(ws.Id, "/cur")!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), current.Id);
        var blocked = AddBlocked(store, ws.Id, "/b", 100);
        // opt-in on: don't leave a running agent
        Assert.Null(store.AutoFollowTarget(current, new[] { blocked }, stayOnActive: true));
        // opt-in off: an active current does not suppress
        Assert.Equal(blocked.Id, store.AutoFollowTarget(current, new[] { blocked }, stayOnActive: false));
    }

    [Fact]
    public void AutoFollowTargetEmptyBlockedIsNil()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        Assert.Null(store.AutoFollowTarget(null, Array.Empty<Session>(), stayOnActive: false));
        Assert.Null(store.AutoFollowTarget(idle, Array.Empty<Session>(), stayOnActive: false));
    }

    [Fact]
    public void AutoFollowTargetMissingStampSortsLast()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var stamped = AddBlocked(store, ws.Id, "/stamped", 500);
        var unstamped = store.AddSession(ws.Id, "/unstamped")!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), unstamped.Id);
        unstamped.StatusChangedAt = null; // a missing stamp is treated as newest, so the stamped one wins
        Assert.Equal(stamped.Id, store.AutoFollowTarget(null, new[] { unstamped, stamped }, stayOnActive: false));
    }

    // MARK: - AutoFollowFire (window-wide filter + select)

    [Fact]
    public void AutoFollowFireSelectsOldestBlockedAcrossWorkspaces()
    {
        var store = AppStoreFixtures.MakeStore();
        var here = store.AddWorkspace("here");
        var away = store.AddWorkspace("away");
        var idle = store.AddSession(here.Id, "/idle")!;
        var older = AddBlocked(store, away.Id, "/older", 100); // window-wide: another workspace
        _ = AddBlocked(store, here.Id, "/newer", 200);
        store.SelectSession(idle.Id);
        store.AutoFollowFire();
        Assert.Equal(older.Id, store.SelectedSessionId); // jumps to the oldest blocked, crossing workspaces
    }

    [Fact]
    public void AutoFollowFireSuppressedWhenParkedOnBlocked()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var current = AddBlocked(store, ws.Id, "/cur", 50);
        _ = AddBlocked(store, ws.Id, "/other", 200);
        store.SelectSession(current.Id);
        store.AutoFollowFire();
        Assert.Equal(current.Id, store.SelectedSessionId); // stays; being on a blocked session is the suppressor
    }

    [Fact]
    public void AutoFollowFireNoOpWhenNoBlocked()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), b.Id);
        store.SelectSession(a.Id);
        store.AutoFollowFire();
        Assert.Equal(a.Id, store.SelectedSessionId); // no blocked session -> no jump
    }

    [Fact]
    public void AutoFollowFireAdvancesAfterCurrentCleared()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        var older = AddBlocked(store, ws.Id, "/older", 100);
        var newer = AddBlocked(store, ws.Id, "/newer", 200);
        store.SelectSession(idle.Id);
        store.AutoFollowFire();
        Assert.Equal(older.Id, store.SelectedSessionId); // first fire -> oldest
        store.SetAgentIndicator(new AgentIndicator(), older.Id); // typing a reply clears the block
        store.AutoFollowFire();
        Assert.Equal(newer.Id, store.SelectedSessionId); // next fire advances to the next oldest
    }

    [Fact]
    public void AutoFollowFireDoesNotNoteActivity()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        _ = AddBlocked(store, ws.Id, "/b", 100);
        store.SelectSession(idle.Id);
        store.AutoFollowFire();
        Assert.Null(store.LastActivityAt); // the app's own jump must not stamp activity (no self-reset)
    }

    // MARK: - mute a block already followed (AutoFollowConsumed)

    [Fact]
    public void AutoFollowTargetSkipsConsumedBlocked()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var older = AddBlocked(store, ws.Id, "/older", 100);
        var newer = AddBlocked(store, ws.Id, "/newer", 200);
        older.AutoFollowConsumed = true; // already pulled to the older block once
        Assert.Equal(newer.Id, store.AutoFollowTarget(null, new[] { older, newer }, stayOnActive: false));
        newer.AutoFollowConsumed = true; // both consumed -> nothing left to follow
        Assert.Null(store.AutoFollowTarget(null, new[] { older, newer }, stayOnActive: false));
    }

    [Fact]
    public void AutoFollowFireMarksConsumedAndDoesNotReturn()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        var x = AddBlocked(store, ws.Id, "/x", 100);
        store.SelectSession(idle.Id);
        store.AutoFollowFire();
        Assert.Equal(x.Id, store.SelectedSessionId); // pulled to the block once
        Assert.True(x.AutoFollowConsumed);
        // user looks, decides to do nothing, navigates back to another session, then goes idle again
        store.SelectSession(idle.Id);
        store.AutoFollowFire();
        Assert.Equal(idle.Id, store.SelectedSessionId); // NOT yanked back to the same still-blocked session
    }

    [Fact]
    public void AutoFollowFireWalksEachBlockOnceThenStops()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        var x = AddBlocked(store, ws.Id, "/x", 100);
        var z = AddBlocked(store, ws.Id, "/z", 200);
        store.SelectSession(idle.Id);
        store.AutoFollowFire();
        Assert.Equal(x.Id, store.SelectedSessionId); // oldest unseen block first
        store.SelectSession(idle.Id); // leave without acting
        store.AutoFollowFire();
        Assert.Equal(z.Id, store.SelectedSessionId); // next unseen block
        store.SelectSession(idle.Id); // leave again
        store.AutoFollowFire();
        Assert.Equal(idle.Id, store.SelectedSessionId); // both blocks already followed -> quiet
    }

    [Fact]
    public void AutoFollowConsumedSurvivesBlockedReassert()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        var x = AddBlocked(store, ws.Id, "/x", 100);
        store.SelectSession(idle.Id);
        store.AutoFollowFire();
        Assert.True(x.AutoFollowConsumed);
        store.SelectSession(idle.Id);
        // a hook re-asserts blocked OVER blocked (same value) -- not a new episode, so the mute holds
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), x.Id);
        Assert.True(x.AutoFollowConsumed);
        store.AutoFollowFire();
        Assert.Equal(idle.Id, store.SelectedSessionId); // still muted, no re-jump
    }

    [Fact]
    public void AutoFollowFireReturnsAfterBlockReenters()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        var x = AddBlocked(store, ws.Id, "/x", 100);
        store.SelectSession(idle.Id);
        store.AutoFollowFire();
        store.SelectSession(idle.Id);
        store.AutoFollowFire();
        Assert.Equal(idle.Id, store.SelectedSessionId); // muted while it stays the same block
        // the agent resumes (blocked -> active) then blocks again (active -> blocked): a new episode
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), x.Id);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), x.Id);
        Assert.False(x.AutoFollowConsumed); // re-entering blocked cleared the mute
        store.AutoFollowFire();
        Assert.Equal(x.Id, store.SelectedSessionId); // pulled once more for the fresh block
    }

    [Fact]
    public void SelectSessionAloneDoesNotNoteActivity()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        store.SelectSession(a.Id);
        store.SelectSession(b.Id);
        // selectSession is the shared seam auto-follow also drives; it must stay silent so the app's own
        // jump never resets the idle timer. only the user entry points (which pair it with noteUserActivity)
        // count -- Task 4 wires those, not selectSession itself.
        Assert.Null(store.LastActivityAt);
        // a user-initiated selection (the AppActions nav wrappers / sidebar row click) additionally calls
        // noteUserActivity, which DOES stamp -- this is what buys the idle grace.
        store.NoteUserActivity();
        store.SelectSession(a.Id);
        Assert.NotNull(store.LastActivityAt);
    }

    // MARK: - suppression (non-terminal editor / palette owns first responder)

    [Fact]
    public void AutoFollowFireSuppressedWhileEditorActive()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        var blocked = AddBlocked(store, ws.Id, "/b", 100);
        store.SelectSession(idle.Id);
        store.SuppressAutoFollow(); // a sidebar rename / command palette owns first responder
        store.AutoFollowFire();
        Assert.Equal(idle.Id, store.SelectedSessionId); // suppressed: the armed jump no-ops, selection stays put
        store.ResumeAutoFollow(); // editor/overlay closed
        store.AutoFollowFire();
        Assert.Equal(blocked.Id, store.SelectedSessionId); // resumed: the next fire follows the waiting block
    }

    [Fact]
    public void AutoFollowSuppressionNestsAndClampsAtZero()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        var blocked = AddBlocked(store, ws.Id, "/b", 100);
        store.SelectSession(idle.Id);
        store.SuppressAutoFollow(); // two overlapping suppressors (e.g. a palette opened over a rename)
        store.SuppressAutoFollow();
        store.ResumeAutoFollow(); // one lifts; the other still holds
        store.AutoFollowFire();
        Assert.Equal(idle.Id, store.SelectedSessionId); // still suppressed while any suppressor holds
        store.ResumeAutoFollow(); // both lifted
        store.ResumeAutoFollow(); // an extra unbalanced resume clamps at zero (no underflow wedging the gate)
        store.AutoFollowFire();
        Assert.Equal(blocked.Id, store.SelectedSessionId); // fully resumed -> follows the block
    }

    // MARK: - NoteUserActivity + IdleMs

    [Fact]
    public void NoteUserActivityStampsLastActivity()
    {
        var store = AppStoreFixtures.MakeStore();
        Assert.Null(store.LastActivityAt);
        Assert.Null(store.IdleMs()); // null before any activity
        store.NoteUserActivity();
        Assert.NotNull(store.LastActivityAt);
        Assert.NotNull(store.IdleMs());
    }

    [Fact]
    public void IdleMsComputesElapsedFromInjectedNow()
    {
        var store = AppStoreFixtures.MakeStore();
        store.LastActivityAt = DateTimeOffset.FromUnixTimeSeconds(1000);
        Assert.Equal(2500, store.IdleMs(DateTimeOffset.FromUnixTimeMilliseconds((long)(1002.5 * 1000))));
        Assert.Equal(0, store.IdleMs(DateTimeOffset.FromUnixTimeSeconds(1000)));
        // clock skew (now before last activity) clamps to zero rather than going negative
        Assert.Equal(0, store.IdleMs(DateTimeOffset.FromUnixTimeSeconds(999)));
    }

    [Fact]
    public void NoteUserActivityWithTimeoutSchedulesFire()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        var blocked = AddBlocked(store, ws.Id, "/b", 100);
        store.SelectSession(idle.Id);
        store.AutoFollowTimeout = 100; // long delay so only the manual flush drives the fire
        store.NoteUserActivity();
        store.AutoFollowDebouncer.Flush(); // drive the scheduled fire deterministically
        Assert.Equal(blocked.Id, store.SelectedSessionId);
    }

    [Fact]
    public void NoteUserActivityWithoutTimeoutCancelsFire()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        _ = AddBlocked(store, ws.Id, "/b", 100);
        store.SelectSession(idle.Id);
        store.AutoFollowTimeout = 100;
        store.NoteUserActivity(); // schedules a fire
        store.AutoFollowTimeout = null;
        store.NoteUserActivity(); // disabled now -> cancels the pending fire
        store.AutoFollowDebouncer.Flush(); // nothing pending
        Assert.Equal(idle.Id, store.SelectedSessionId); // no jump when disabled
    }

    // MARK: - SetAutoFollow lifecycle + status-change arming

    [Fact]
    public void SetAutoFollowEnableStoresStateAndArmsFromCurrent()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        var blocked = AddBlocked(store, ws.Id, "/b", 100);
        store.SelectSession(idle.Id);
        store.SetAutoFollow(100, stayOnActive: false); // enable arms a fire from the current state
        Assert.Equal(100d, store.AutoFollowTimeout);
        Assert.False(store.AutoFollowStayOnActive);
        store.AutoFollowDebouncer.Flush(); // drive the enable's arm deterministically
        Assert.Equal(blocked.Id, store.SelectedSessionId); // already-idle user is pulled to the waiting block
    }

    [Fact]
    public void SetAutoFollowEnableStoresStayOnActive()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var active = store.AddSession(ws.Id, "/active")!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), active.Id);
        _ = AddBlocked(store, ws.Id, "/b", 100);
        store.SelectSession(active.Id);
        store.SetAutoFollow(100, stayOnActive: true);
        Assert.True(store.AutoFollowStayOnActive);
        store.AutoFollowDebouncer.Flush();
        Assert.Equal(active.Id, store.SelectedSessionId); // stayOnActive suppresses leaving a running agent
    }

    [Fact]
    public void SetAutoFollowDisableCancelsPendingFire()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        _ = AddBlocked(store, ws.Id, "/b", 100);
        store.SelectSession(idle.Id);
        store.SetAutoFollow(100, stayOnActive: false); // arms a fire
        store.SetAutoFollow(null, stayOnActive: false); // disable cancels it
        Assert.Null(store.AutoFollowTimeout);
        store.AutoFollowDebouncer.Flush(); // nothing pending after cancel
        Assert.Equal(idle.Id, store.SelectedSessionId); // no jump once disabled
    }

    [Fact]
    public void AutoFollowFireSelfTriggerTerminates()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var completed = store.AddSession(ws.Id, "/done")!;
        var blocked = AddBlocked(store, ws.Id, "/b", 100);
        store.SelectSession(completed.Id); // select first, THEN stamp the one-time glyph so it rides selected
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Completed, AutoReset: true), completed.Id);
        store.AutoFollowFire();
        Assert.Equal(blocked.Id, store.SelectedSessionId); // jumps to the block
        // moving away cleared completed's autoReset glyph — the agentIndicator change the observer re-arms
        // on. that self-trigger must not loop: the next fire is now parked on the block, so it no-ops.
        Assert.Equal(AgentStatus.Idle, completed.AgentIndicator.Status);
        store.AutoFollowFire();
        Assert.Equal(blocked.Id, store.SelectedSessionId); // stays put -> the cycle terminates, no loop
    }

    [Fact]
    public void StatusChangeWhileEnabledArmsAutoFollow()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        var s = store.AddSession(ws.Id, "/s")!; // idle at enable, so the observer tracks it
        store.SelectSession(idle.Id);
        store.SetAutoFollow(100, stayOnActive: false);
        store.AutoFollowDebouncer.Flush(); // consume the enable's arm (no-op: nothing blocked yet)
        Assert.Equal(idle.Id, store.SelectedSessionId);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), s.Id); // a block LANDS while idle
        // upstream, the block landing re-arms the debouncer through a coalesced, deferred observer chain
        // (observation tracking onChange -> scheduleAutoFollowRearm, two main-queue hops), so the Swift test
        // drains the main queue in a bounded loop. here the re-arm is DIRECT inside SetAgentIndicator (the
        // documented adaptation), so a single flush drives the re-armed fire deterministically.
        store.AutoFollowDebouncer.Flush();
        Assert.Equal(s.Id, store.SelectedSessionId); // the status change armed the follow, which then jumped
    }

    [Fact]
    public void SetAutoFollowTimeoutChangeWhileEnabledRearms()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        var blocked = AddBlocked(store, ws.Id, "/b", 100);
        store.SelectSession(idle.Id);
        store.SetAutoFollow(100, stayOnActive: false); // enable at a long grace
        store.SetAutoFollow(30, stayOnActive: false); // change the grace while still enabled -> re-arm
        Assert.Equal(30d, store.AutoFollowTimeout);
        // the changed grace re-armed the debouncer from the current state (previousTimeout != null, so the
        // status observer is NOT re-registered — only the debouncer re-arms); the flush drives that fire.
        store.AutoFollowDebouncer.Flush();
        Assert.Equal(blocked.Id, store.SelectedSessionId);
    }

    [Fact]
    public void SetAutoFollowStayOnActiveChangeWhileEnabledRearms()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var active = store.AddSession(ws.Id, "/active")!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), active.Id);
        var blocked = AddBlocked(store, ws.Id, "/b", 100);
        store.SelectSession(active.Id);
        store.SetAutoFollow(100, stayOnActive: true); // enable: an active current suppresses the block
        store.AutoFollowDebouncer.Flush();
        Assert.Equal(active.Id, store.SelectedSessionId); // stayOnActive keeps us on the running agent
        // toggle stayOnActive OFF at the SAME grace: no pending fire remains (the last one no-op'd), so the
        // config change alone must re-arm and re-decide, else the waiting block is never followed.
        store.SetAutoFollow(100, stayOnActive: false);
        store.AutoFollowDebouncer.Flush();
        Assert.Equal(blocked.Id, store.SelectedSessionId); // no longer suppressed -> follows the waiting block
    }

    [Fact]
    public void SetAutoFollowDisableStopsStatusRearm()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        var s = store.AddSession(ws.Id, "/s")!; // idle at enable, so the observer tracks it
        store.SelectSession(idle.Id);
        store.SetAutoFollow(100, stayOnActive: false); // enable + start observing
        store.AutoFollowDebouncer.Flush(); // consume the enable's arm (nothing blocked yet)
        store.SetAutoFollow(null, stayOnActive: false); // DISABLE before any block lands
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), s.Id); // a block lands AFTER disable
        // upstream, the surviving one-shot observation tracker must self-tear-down WITHOUT re-arming, and
        // the Swift test drains the main queue generously to give any stray re-arm every chance to fire.
        // here the re-arm is DIRECT inside SetAgentIndicator and gated on AutoFollowTimeout being non-null
        // (the documented adaptation), so the disabled mutation must not have armed anything: the flush
        // stays a no-op and the selection never moves.
        store.AutoFollowDebouncer.Flush(); // nothing should be pending
        Assert.Equal(idle.Id, store.SelectedSessionId); // disabled: no re-arm from the status change
    }

    // MARK: - Control tree projection (idleMs live, autoFollowMs config) + the focus-bridge notification

    [Fact]
    public void ControlTreeProjectsIdleAndAutoFollowMs()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        _ = store.AddSession(ws.Id, "/a")!;
        // before any activity + disabled: both live fields are null.
        var tree = store.ControlTree();
        Assert.Null(tree.IdleMs);
        Assert.Null(tree.AutoFollowMs);
        // a configured timeout projects as ms (the * 1000 scaling) and activity makes IdleMs live (>= 0).
        store.AutoFollowTimeout = 30;
        store.LastActivityAt = DateTimeOffset.UtcNow;
        tree = store.ControlTree();
        Assert.Equal(30_000, tree.AutoFollowMs);
        Assert.True((tree.IdleMs ?? -1) >= 0);
        // disabling clears AutoFollowMs back to null.
        store.AutoFollowTimeout = null;
        Assert.Null(store.ControlTree().AutoFollowMs);
    }

    [Fact]
    public void AutoFollowFirePostsFollowedNotification()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("w");
        var idle = store.AddSession(ws.Id, "/idle")!;
        var blocked = AddBlocked(store, ws.Id, "/b", 100);
        store.SelectSession(idle.Id);
        // the app-target focus bridge relies on this event carrying the auto-followed session id: observe
        // it (the C# AutoFollowed event is the analogue of the synchronous .agtermAutoFollowed post, so it
        // delivers inline, before the fire returns) and assert the id.
        Guid? followedId = null;
        store.AutoFollowed += id => followedId = id;
        store.AutoFollowFire();
        Assert.Equal(blocked.Id, store.SelectedSessionId);
        Assert.Equal(blocked.Id, followedId);
    }
}
