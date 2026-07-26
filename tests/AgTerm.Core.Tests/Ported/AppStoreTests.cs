using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported 1:1 from agtermCore AppStoreTests.swift.</summary>
public class AppStoreTests
{
    private static AppStore MakeStore() => AppStoreFixtures.MakeStore();

    /// <summary>Swift UUID.uuidString: the canonical uppercase form the tree node ids carry.</summary>
    private static string Upper(Guid id) => id.ToString("D").ToUpperInvariant();

    [Fact]
    public void EmptyStoreHasNoSelectionOrActiveSession()
    {
        var store = MakeStore();
        Assert.Empty(store.Workspaces);
        Assert.Null(store.SelectedSessionId);
        Assert.Null(store.ActiveSession);
    }

    [Fact]
    public void SidebarVisibleDefaultsTrueAndToggles()
    {
        var store = MakeStore();
        Assert.True(store.SidebarVisible);
        store.ToggleSidebarVisible();
        Assert.False(store.SidebarVisible);
        store.SetSidebarVisible(true);
        Assert.True(store.SidebarVisible);
        store.SetSidebarVisible(true); // unchanged: clean no-op
        Assert.True(store.SidebarVisible);
    }

    [Fact]
    public void DefaultWorkspaceNameCountsUp()
    {
        var store = MakeStore();
        Assert.Equal("workspace 1", store.DefaultWorkspaceName);
        store.AddWorkspace("work");
        Assert.Equal("workspace 2", store.DefaultWorkspaceName);
    }

    [Fact]
    public void CurrentWorkspaceFollowsSelectionThenFallsBackToLast()
    {
        var store = MakeStore();
        Assert.Null(store.CurrentWorkspaceId);
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        // no selection -> last workspace.
        Assert.Equal(personal.Id, store.CurrentWorkspaceId);
        // a selected session pins its owning workspace.
        var session = store.AddSession(work.Id, "/a");
        Assert.NotNull(session);
        store.SelectSession(session!.Id);
        Assert.Equal(work.Id, store.CurrentWorkspaceId);
        // deselecting falls back to the last workspace again.
        store.SelectSession(null);
        Assert.Equal(personal.Id, store.CurrentWorkspaceId);
    }

    [Fact]
    public void AddWorkspaceAppends()
    {
        var store = MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        Assert.Equal(new[] { work.Id, personal.Id }, store.Workspaces.Select(w => w.Id));
        Assert.Equal(new[] { "work", "personal" }, store.Workspaces.Select(w => w.Name));
    }

    [Fact]
    public void AddSessionAppendsAndSelects()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/tmp");
        Assert.NotNull(session);
        var unwrapped = session!;
        Assert.Equal(new[] { unwrapped.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.Equal("/tmp", unwrapped.InitialCwd);
        Assert.Equal(unwrapped.Id, store.SelectedSessionId);
        Assert.Equal(unwrapped.Id, store.ActiveSession?.Id);
    }

    [Fact]
    public void AddSessionWithoutSelectLeavesSelectionUntouched()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var first = store.AddSession(ws.Id, "/a");
        Assert.NotNull(first);
        Assert.Equal(first!.Id, store.SelectedSessionId);
        // a background add (control `session.new --no-select`) appends the session but keeps the current
        // selection and recency, so `active` still points at the first session.
        var background = store.AddSession(ws.Id, "/b", select: false);
        Assert.NotNull(background);
        Assert.Equal(new[] { first.Id, background!.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.Equal(first.Id, store.SelectedSessionId);
        Assert.Equal(first.Id, store.ActiveSession?.Id);
        Assert.Equal(new[] { first.Id }, store.SessionRecency.Items); // the background add was not recorded
    }

    [Fact]
    public void AddSessionCarriesInitialCommand()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var withCmd = store.AddSession(ws.Id, "/tmp", command: "ssh host");
        Assert.NotNull(withCmd);
        Assert.Equal("ssh host", withCmd!.InitialCommand);
        // default is nil — a plain session runs the login shell.
        var plain = store.AddSession(ws.Id, "/tmp");
        Assert.NotNull(plain);
        Assert.Null(plain!.InitialCommand);
    }

    [Fact]
    public void AddSessionCarriesCommandWait()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var held = store.AddSession(ws.Id, "/tmp", command: "make test", wait: true);
        Assert.NotNull(held);
        Assert.True(held!.CommandWait);
        // default is false — a command session closes when its command exits.
        var plain = store.AddSession(ws.Id, "/tmp", command: "make test");
        Assert.NotNull(plain);
        Assert.False(plain!.CommandWait);
    }

    [Fact]
    public void AddSessionSeedsCustomName()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var named = store.AddSession(ws.Id, "/tmp", name: "myhost");
        Assert.NotNull(named);
        Assert.Equal("myhost", named!.CustomName);
        Assert.Equal("myhost", named.DisplayName);
        // blank/whitespace name clears to nil, leaving the auto basename (matches renameSession).
        var blank = store.AddSession(ws.Id, "/tmp", name: "  ");
        Assert.NotNull(blank);
        Assert.Null(blank!.CustomName);
        // default is nil — no custom name.
        var plain = store.AddSession(ws.Id, "/tmp");
        Assert.NotNull(plain);
        Assert.Null(plain!.CustomName);
    }

    [Fact]
    public void AddSessionToUnknownWorkspaceReturnsNil()
    {
        var store = MakeStore();
        Assert.Null(store.AddSession(Guid.NewGuid(), "/tmp"));
        Assert.Null(store.SelectedSessionId);
    }

    [Fact]
    public void WorkspaceNamedFindsExactTrimmedMatch()
    {
        var store = MakeStore();
        var servers = store.AddWorkspace("servers");
        store.AddWorkspace("other");
        Assert.Equal(servers.Id, store.WorkspaceNamed("servers")?.Id);
        Assert.Equal(servers.Id, store.WorkspaceNamed("  servers  ")?.Id); // input is trimmed
        Assert.Null(store.WorkspaceNamed("Servers"));                      // case-sensitive
        Assert.Null(store.WorkspaceNamed("missing"));
        Assert.Null(store.WorkspaceNamed("   "));                          // blank
    }

    [Fact]
    public void EnsureWorkspaceReusesExistingElseCreates()
    {
        var store = MakeStore();
        var existing = store.AddWorkspace("servers");
        var before = store.Workspaces.Count;
        // reuse: the same name returns the existing workspace, no new one appended.
        Assert.Equal(existing.Id, store.EnsureWorkspace("servers")?.Id);
        Assert.Equal(before, store.Workspaces.Count);
        // create: a new name appends exactly one workspace, trimmed.
        var created = store.EnsureWorkspace("  fresh  ");
        Assert.NotNull(created);
        Assert.Equal("fresh", created!.Name);
        Assert.Equal(before + 1, store.Workspaces.Count);
        // idempotent: ensuring the just-created name again reuses it.
        Assert.Equal(created.Id, store.EnsureWorkspace("fresh")?.Id);
        Assert.Equal(before + 1, store.Workspaces.Count);
        // a blank name creates nothing.
        Assert.Null(store.EnsureWorkspace("   "));
        Assert.Equal(before + 1, store.Workspaces.Count);
    }

    [Fact]
    public void SelectSessionUpdatesActive()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        store.SelectSession(a.Id);
        Assert.Equal(a.Id, store.ActiveSession?.Id);
        store.SelectSession(b.Id);
        Assert.Equal(b.Id, store.ActiveSession?.Id);
    }

    [Fact]
    public void SelectUnknownSessionIsIgnored()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        store.SelectSession(Guid.NewGuid());
        Assert.Equal(a.Id, store.SelectedSessionId);
    }

    [Fact]
    public void SelectSessionClearsOnlyItsUnseenBadge()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        a.UnseenCount = 3;
        b.UnseenCount = 2;
        store.SelectSession(a.Id);
        Assert.Equal(0, a.UnseenCount); // selecting a session clears its own badge
        Assert.Equal(2, b.UnseenCount); // other sessions are untouched
    }

    [Fact]
    public void ClearUnseenResetsCountAndIgnoresUnknownID()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        a.UnseenCount = 5;
        store.ClearUnseen(a.Id);
        Assert.Equal(0, a.UnseenCount);
        store.ClearUnseen(Guid.NewGuid()); // unknown id is a no-op, no crash
    }

    [Fact]
    public void ClearUnseenDoesNotChangeSelection()
    {
        // the focus-free invariant behind session.seen: clearing a NON-selected session's badge must
        // leave the selection put (markSessionSeen calls clearUnseen and nothing else).
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        a.UnseenCount = 3;
        store.SelectSession(b.Id);
        store.ClearUnseen(a.Id);
        Assert.Equal(b.Id, store.SelectedSessionId); // focus-free: selecting is untouched
        Assert.Equal(0, a.UnseenCount);              // the target's badge is cleared
    }

    [Fact]
    public void SetAgentIndicatorSetsFieldOnRightSession()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active, Blink: true), a.Id);
        Assert.Equal(new AgentIndicator(AgentStatus.Active, Blink: true), a.AgentIndicator);
        Assert.Equal(new AgentIndicator(), b.AgentIndicator); // other sessions are untouched
    }

    [Fact]
    public void SetAgentIndicatorUnknownSessionIsNoop()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), Guid.NewGuid()); // unknown id: no crash
        Assert.Equal(new AgentIndicator(), a.AgentIndicator); // existing session untouched
    }

    [Fact]
    public void SetAgentIndicatorStampsStatusChangedAtOnNonIdle()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        Assert.Null(a.StatusChangedAt); // a fresh session has no stamp
        var before = DateTimeOffset.UtcNow;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), a.Id);
        Assert.NotNull(a.StatusChangedAt); // a non-idle status stamps the change time
        Assert.True(a.StatusChangedAt!.Value >= before);
    }

    [Fact]
    public void SetAgentIndicatorClearsStatusChangedAtOnIdle()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), a.Id);
        Assert.NotNull(a.StatusChangedAt);
        store.SetAgentIndicator(new AgentIndicator(), a.Id); // back to idle clears the stamp
        Assert.Null(a.StatusChangedAt);
    }

    [Fact]
    public void SetAgentIndicatorReassertingNonIdleUpdatesStatusChangedAt()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        a.StatusChangedAt = DateTimeOffset.FromUnixTimeSeconds(0); // pretend a stale stamp
        var before = DateTimeOffset.UtcNow;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), a.Id);
        Assert.NotNull(a.StatusChangedAt);
        // re-asserting a non-idle status moves the stamp to ~now, not just off epoch-0
        Assert.True(a.StatusChangedAt!.Value >= before);
    }

    [Fact]
    public void StatusChangedAtDoesNotSurviveSnapshotRestore()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Completed), session.Id);
        Assert.NotNull(session.StatusChangedAt);
        var restored = MakeStore();
        restored.Restore(store.Snapshot());
        // the stamp is ephemeral like the indicator: a restored session falls back to nil.
        Assert.Null(restored.Workspaces[0].Sessions[0].StatusChangedAt);
    }

    [Fact]
    public void AgentIndicatorDoesNotSurviveSnapshotRestore()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Completed, Blink: true), session.Id);
        var restored = MakeStore();
        restored.Restore(store.Snapshot());
        // the indicator is ephemeral: a restored session falls back to the default idle state.
        Assert.Equal(new AgentIndicator(), restored.Workspaces[0].Sessions[0].AgentIndicator);
    }

    [Fact]
    public void SelectSessionKeepsNonAutoResetIndicator()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), a.Id); // autoReset defaults false
        store.SelectSession(a.Id); // a non-auto-reset indicator survives a visit (keep-state)
        Assert.Equal(new AgentIndicator(AgentStatus.Active), a.AgentIndicator);
        store.SelectSession(b.Id);
        Assert.Equal(new AgentIndicator(AgentStatus.Active), a.AgentIndicator); // still set after switching away
    }

    [Fact]
    public void SelectSessionResetsAutoResetIndicator()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Completed, AutoReset: true), a.Id);
        store.SelectSession(a.Id); // visiting an auto-reset indicator clears it to idle for good
        Assert.Equal(new AgentIndicator(), a.AgentIndicator);
    }

    [Fact]
    public void SelectSessionDoesNotResetUnrelatedAutoResetIndicator()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Completed, AutoReset: true), a.Id);
        store.SelectSession(b.Id); // selecting a different session leaves a background indicator alone
        Assert.Equal(new AgentIndicator(AgentStatus.Completed, AutoReset: true), a.AgentIndicator);
    }

    [Fact]
    public void SwitchingAwayClearsAutoResetIndicatorOnTheLeftSession()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        store.SelectSession(a.Id);
        // the agent finishes WHILE a is the selected session, so no visit fires and its completed flash lingers
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Completed, AutoReset: true), a.Id);
        Assert.Equal(new AgentIndicator(AgentStatus.Completed, AutoReset: true), a.AgentIndicator);
        // switching away from a clears its one-time completed flash (it must not persist on the row you left)
        store.SelectSession(b.Id);
        Assert.Equal(new AgentIndicator(), a.AgentIndicator);
        Assert.Equal(new AgentIndicator(), b.AgentIndicator); // and b (the one moved to) carries nothing
    }

    [Fact]
    public void WorkspaceForSessionDerivesOwner()
    {
        var store = MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var inWork = store.AddSession(work.Id, "/a")!;
        var inPersonal = store.AddSession(personal.Id, "/b")!;
        Assert.Equal(work.Id, store.WorkspaceForSession(inWork.Id)?.Id);
        Assert.Equal(personal.Id, store.WorkspaceForSession(inPersonal.Id)?.Id);
        Assert.Null(store.WorkspaceForSession(Guid.NewGuid()));
    }

    [Fact]
    public void CloseNonActiveSessionKeepsSelection()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        store.SelectSession(b.Id);
        store.CloseSession(a.Id);
        Assert.Equal(new[] { b.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.Equal(b.Id, store.SelectedSessionId);
    }

    [Fact]
    public void CloseActiveSessionReselectsNext()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        store.SelectSession(a.Id);
        store.CloseSession(a.Id);
        Assert.Equal(b.Id, store.SelectedSessionId);
        Assert.Equal(b.Id, store.ActiveSession?.Id);
    }

    [Fact]
    public void CloseActiveLastSessionReselectsPrevious()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        store.SelectSession(b.Id);
        store.CloseSession(b.Id);
        Assert.Equal(a.Id, store.SelectedSessionId);
    }

    [Fact]
    public void CloseActiveSessionFallsBackToOtherWorkspace()
    {
        var store = MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var other = store.AddSession(personal.Id, "/other")!;
        var only = store.AddSession(work.Id, "/only")!;
        store.SelectSession(only.Id);
        store.CloseSession(only.Id);
        Assert.Empty(store.Workspaces[0].Sessions);
        Assert.Equal(other.Id, store.SelectedSessionId);
    }

    [Fact]
    public void CloseLastSessionClearsSelection()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var only = store.AddSession(ws.Id, "/only")!;
        store.CloseSession(only.Id);
        Assert.Null(store.SelectedSessionId);
        Assert.Null(store.ActiveSession);
    }

    [Fact]
    public void CloseSessionTearsDownSurface()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        var surface = new SpySurface();
        session.Surface = surface;
        store.CloseSession(session.Id);
        Assert.Equal(1, surface.TeardownCount);
    }

    [Fact]
    public void CloseSessionTearsDownSplitSurface()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        var split = new SpySurface();
        session.SplitSurface = split;
        store.CloseSession(session.Id);
        Assert.Equal(1, split.TeardownCount);
    }

    [Fact]
    public void SoftCloseSessionHidesWithoutTearingDownAndUndoRestoresSameSession()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var first = store.AddSession(ws.Id, "/a", name: "alpha");
        Assert.NotNull(first);
        var second = store.AddSession(ws.Id, "/b", name: "beta");
        Assert.NotNull(second);
        var surface = new SpySurface();
        first!.Surface = surface;
        store.SelectSession(first.Id);

        Assert.True(store.SoftCloseSession(first.Id, 60));

        Assert.Equal(new[] { second!.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.Equal(second.Id, store.SelectedSessionId);
        Assert.Equal(0, surface.TeardownCount);
        var summary = store.PendingCloseSummary;
        Assert.NotNull(summary);
        Assert.Equal(PendingCloseKind.Session, summary!.Kind);
        Assert.Equal("alpha", summary.Title);

        Assert.True(store.UndoPendingClose(summary.Id));

        Assert.Equal(new[] { first.Id, second.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.Equal(first.Id, store.SelectedSessionId);
        Assert.Same(first, store.Workspaces[0].Sessions[0]);
        Assert.Equal(0, surface.TeardownCount);
        Assert.Null(store.PendingCloseSummary);
    }

    [Fact]
    public void SoftCloseSessionsWithOneTargetKeepsSingleSessionSummary()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var first = store.AddSession(ws.Id, "/a", name: "alpha");
        Assert.NotNull(first);
        var second = store.AddSession(ws.Id, "/b", name: "beta");
        Assert.NotNull(second);

        Assert.True(store.SoftCloseSessions(new[] { first!.Id }, 60));

        var summary = store.PendingCloseSummary;
        Assert.NotNull(summary);
        Assert.Equal(PendingCloseKind.Session, summary!.Kind);
        Assert.Equal("alpha", summary.Title);

        Assert.True(store.UndoPendingClose(summary.Id));
        Assert.Equal(new[] { first.Id, second!.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.Equal(first.Id, store.SelectedSessionId);
    }

    [Fact]
    public void SoftCloseSessionsGroupsUndoAndRestoresEverySession()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var first = store.AddSession(ws.Id, "/a", name: "alpha");
        Assert.NotNull(first);
        var second = store.AddSession(ws.Id, "/b", name: "beta");
        Assert.NotNull(second);
        var third = store.AddSession(ws.Id, "/c", name: "gamma");
        Assert.NotNull(third);
        var firstSurface = new SpySurface();
        first!.Surface = firstSurface;
        var secondSurface = new SpySurface();
        second!.Surface = secondSurface;
        store.SelectSession(second.Id);

        Assert.True(store.SoftCloseSessions(new[] { first.Id, second.Id }, 60));

        Assert.Equal(new[] { third!.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.Equal(third.Id, store.SelectedSessionId);
        Assert.Equal(0, firstSurface.TeardownCount);
        Assert.Equal(0, secondSurface.TeardownCount);
        var summary = store.PendingCloseSummary;
        Assert.NotNull(summary);
        Assert.Equal(PendingCloseKind.Sessions, summary!.Kind);
        Assert.Equal("2 sessions", summary.Title);

        Assert.True(store.UndoPendingClose(summary.Id));

        Assert.Equal(new[] { first.Id, second.Id, third.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.Equal(second.Id, store.SelectedSessionId);
        Assert.Same(first, store.Workspaces[0].Sessions[0]);
        Assert.Same(second, store.Workspaces[0].Sessions[1]);
        Assert.Equal(0, firstSurface.TeardownCount);
        Assert.Equal(0, secondSurface.TeardownCount);
        Assert.Null(store.PendingCloseSummary);
    }

    [Fact]
    public void SoftCloseSessionsSelectsNearestSurvivorAfterActiveClose()
    {
        var store = MakeStore();
        var firstWorkspace = store.AddWorkspace("one");
        var secondWorkspace = store.AddWorkspace("two");
        var distant = store.AddSession(firstWorkspace.Id, "/a");
        Assert.NotNull(distant);
        var active = store.AddSession(secondWorkspace.Id, "/b");
        Assert.NotNull(active);
        var neighbor = store.AddSession(secondWorkspace.Id, "/c");
        Assert.NotNull(neighbor);
        store.SelectSession(active!.Id);

        Assert.True(store.SoftCloseSessions(new[] { active.Id, distant!.Id }, 60));

        Assert.Equal(neighbor!.Id, store.SelectedSessionId);
    }

    [Fact]
    public void SoftCloseSessionsAdjustsReselectionForEarlierBatchRemovals()
    {
        // the index adjustment feeds the POSITIONAL fallback, which only runs when the scoped recency is
        // empty, so drive it through a restore: nothing has been activated but the restored selection, and
        // once that is the session being closed the fallback is the only thing left to pick with.
        var store = MakeStore();
        var wsId = Guid.NewGuid();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var sessions = ids.Select((id, i) => new SessionSnapshot { Id = id, Cwd = $"/{i}" }).ToArray();
        store.Restore(new Snapshot
        {
            SelectedSessionId = ids[1],
            Workspaces = new[] { new WorkspaceSnapshot { Id = wsId, Name = "work", Sessions = sessions } },
        });

        Assert.True(store.SoftCloseSessions(new[] { ids[0], ids[1] }, 60));

        // without the adjustment the stale index 1 would pick the LAST session instead of the neighbor
        Assert.Equal(ids[2], store.SelectedSessionId);
    }

    [Fact]
    public void SoftCloseSessionsFallsBackWhenActiveWorkspaceIsEmptied()
    {
        var store = MakeStore();
        var firstWorkspace = store.AddWorkspace("one");
        var secondWorkspace = store.AddWorkspace("two");
        var distant = store.AddSession(firstWorkspace.Id, "/a");
        Assert.NotNull(distant);
        var active = store.AddSession(secondWorkspace.Id, "/b");
        Assert.NotNull(active);
        var sibling = store.AddSession(secondWorkspace.Id, "/c");
        Assert.NotNull(sibling);
        store.SelectSession(active!.Id);

        Assert.True(store.SoftCloseSessions(new[] { active.Id, sibling!.Id }, 60));

        Assert.Equal(distant!.Id, store.SelectedSessionId);
    }

    [Fact]
    public void FinalizedSoftCloseSessionsTearsDownEverySessionAndCannotUndo()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var first = store.AddSession(ws.Id, "/a");
        Assert.NotNull(first);
        var second = store.AddSession(ws.Id, "/b");
        Assert.NotNull(second);
        var firstSurface = new SpySurface();
        first!.Surface = firstSurface;
        var secondSurface = new SpySurface();
        second!.Surface = secondSurface;

        Assert.True(store.SoftCloseSessions(new[] { first.Id, second.Id }, 60));
        var summary = store.PendingCloseSummary;
        Assert.NotNull(summary);
        store.FinalizePendingClose(summary!.Id);

        Assert.Equal(1, firstSurface.TeardownCount);
        Assert.Equal(1, secondSurface.TeardownCount);
        Assert.False(store.UndoPendingClose(summary.Id));
        Assert.Null(store.PendingCloseSummary);
    }

    [Fact]
    public void FinalizedSoftCloseSessionTearsDownAndCannotUndo()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a");
        Assert.NotNull(session);
        var surface = new SpySurface();
        session!.Surface = surface;

        Assert.True(store.SoftCloseSession(session.Id, 60));
        var summary = store.PendingCloseSummary;
        Assert.NotNull(summary);
        store.FinalizePendingClose(summary!.Id);

        Assert.Equal(1, surface.TeardownCount);
        Assert.False(store.UndoPendingClose(summary.Id));
        Assert.Null(store.PendingCloseSummary);
    }

    [Fact]
    public void UndoingLatestPendingClosePromotesPreviousPendingClose()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var first = store.AddSession(ws.Id, "/a", name: "alpha");
        Assert.NotNull(first);
        var second = store.AddSession(ws.Id, "/b", name: "beta");
        Assert.NotNull(second);
        var third = store.AddSession(ws.Id, "/c", name: "gamma");
        Assert.NotNull(third);

        Assert.True(store.SoftCloseSession(first!.Id, 60));
        Assert.Equal("alpha", store.PendingCloseSummary?.Title);
        Assert.True(store.SoftCloseSession(second!.Id, 60));
        Assert.Equal("beta", store.PendingCloseSummary?.Title);

        Assert.True(store.UndoPendingClose());
        Assert.Equal(new[] { second.Id, third!.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.Equal("alpha", store.PendingCloseSummary?.Title);

        Assert.True(store.UndoPendingClose());
        Assert.Equal(new[] { first.Id, second.Id, third.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.Null(store.PendingCloseSummary);
    }

    [Fact]
    public async Task UndoBeforeScheduledFinalizeMakesTimerFireNoOp()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a");
        Assert.NotNull(session);
        var surface = new SpySurface();
        session!.Surface = surface;

        Assert.True(store.SoftCloseSession(session.Id, 0.01));
        var summary = store.PendingCloseSummary;
        Assert.NotNull(summary);
        Assert.True(store.UndoPendingClose(summary!.Id));
        await Task.Delay(TimeSpan.FromMilliseconds(30));

        Assert.Same(session, store.SessionWithId(session.Id));
        Assert.Equal(0, surface.TeardownCount);
        Assert.Null(store.PendingCloseSummary);
    }

    [Fact]
    public async Task ScheduledFinalizeTearsDownAfterGrace()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a");
        Assert.NotNull(session);
        var surface = new SpySurface();
        session!.Surface = surface;

        Assert.True(store.SoftCloseSession(session.Id, 0.01));
        // poll for the scheduled finalize rather than racing one fixed sleep. the suites run in parallel, so
        // the 10 ms grace timer can land well past a flat 30 ms window under load (reproduced: ~1 run in 6).
        // poll the TEARDOWN, not `session(withID:)`: softCloseSession removes the session from its workspace
        // synchronously and only defers the teardown to the timer, so a session-lookup poll exits immediately
        // and proves nothing. bounded at ~1 s, so a finalize that never fires fails the expectations below.
        // platform adaptation: upstream's finalize runs on the main actor, atomic with the test; here
        // it runs on a thread-pool timer, so the summary clear can land a beat after the teardown —
        // poll for BOTH before asserting.
        for (var i = 0; i < 200; i++)
        {
            if (surface.TeardownCount == 1 && store.PendingCloseSummary is null) break;
            await Task.Delay(TimeSpan.FromMilliseconds(5));
        }

        Assert.Null(store.SessionWithId(session.Id));
        Assert.Equal(1, surface.TeardownCount);
        Assert.Null(store.PendingCloseSummary);
    }

    [Fact]
    public void SoftRemoveWorkspaceHidesWithoutTearingDownAndUndoRestores()
    {
        var store = MakeStore();
        var keep = store.AddWorkspace("keep");
        var doomed = store.AddWorkspace("doomed");
        var session = store.AddSession(doomed.Id, "/a");
        Assert.NotNull(session);
        var surface = new SpySurface();
        session!.Surface = surface;
        _ = store.AddSession(keep.Id, "/b");

        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));

        Assert.Equal(new[] { keep.Id }, store.Workspaces.Select(w => w.Id));
        Assert.Equal(0, surface.TeardownCount);
        var summary = store.PendingCloseSummary;
        Assert.NotNull(summary);
        Assert.Equal(PendingCloseKind.Workspace, summary!.Kind);
        Assert.Equal("doomed", summary.Title);

        Assert.True(store.UndoPendingClose(summary.Id));

        Assert.Equal(new[] { keep.Id, doomed.Id }, store.Workspaces.Select(w => w.Id));
        Assert.Same(session, store.Workspaces[1].Sessions[0]);
        Assert.Equal(session.Id, store.SelectedSessionId);
        Assert.Equal(0, surface.TeardownCount);
    }

    [Fact]
    public void UndoingPendingSessionCloseThenWorkspaceCloseKeepsOneWorkspace()
    {
        var store = MakeStore();
        var doomed = store.AddWorkspace("doomed");
        _ = store.AddWorkspace("keep");
        var first = store.AddSession(doomed.Id, "/a");
        Assert.NotNull(first);
        var second = store.AddSession(doomed.Id, "/b");
        Assert.NotNull(second);
        var firstSurface = new SpySurface();
        var secondSurface = new SpySurface();
        first!.Surface = firstSurface;
        second!.Surface = secondSurface;

        Assert.True(store.SoftCloseSession(first.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var sessionClose = store.PendingCloseSummary!.Id;
        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var workspaceClose = store.PendingCloseSummary!.Id;

        // undoing the session first recreates the workspace shell; the workspace undo must merge into it
        Assert.True(store.UndoPendingClose(sessionClose));
        Assert.True(store.UndoPendingClose(workspaceClose));

        Assert.Equal(1, store.Workspaces.Count(w => w.Id == doomed.Id));
        var restored = store.Workspaces.FirstOrDefault(w => w.Id == doomed.Id);
        Assert.NotNull(restored);
        Assert.Equal(new[] { first.Id, second.Id }, restored!.Sessions.Select(s => s.Id));
        Assert.Same(first, restored.Sessions[0]);
        Assert.Same(second, restored.Sessions[1]);
        Assert.Equal(second.Id, store.SelectedSessionId);
        Assert.Equal(0, firstSurface.TeardownCount);
        Assert.Equal(0, secondSurface.TeardownCount);
    }

    [Fact]
    public void UndoingWorkspaceCloseThenSessionCloseKeepsOneWorkspace()
    {
        var store = MakeStore();
        var doomed = store.AddWorkspace("doomed");
        _ = store.AddWorkspace("keep");
        var first = store.AddSession(doomed.Id, "/a");
        Assert.NotNull(first);
        var second = store.AddSession(doomed.Id, "/b");
        Assert.NotNull(second);

        Assert.True(store.SoftCloseSession(first!.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var sessionClose = store.PendingCloseSummary!.Id;
        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var workspaceClose = store.PendingCloseSummary!.Id;

        // the reverse order takes the insert branch, then restorePendingSession finds the workspace
        Assert.True(store.UndoPendingClose(workspaceClose));
        Assert.True(store.UndoPendingClose(sessionClose));

        Assert.Equal(1, store.Workspaces.Count(w => w.Id == doomed.Id));
        var restored = store.Workspaces.FirstOrDefault(w => w.Id == doomed.Id);
        Assert.NotNull(restored);
        Assert.Equal(new[] { first.Id, second!.Id }, restored!.Sessions.Select(s => s.Id));
    }

    [Fact]
    public void RebuiltShellSeedsNameAndExpansionFromPendingWorkspaceClose()
    {
        var store = MakeStore();
        var doomed = store.AddWorkspace("old");
        _ = store.AddWorkspace("keep");
        var first = store.AddSession(doomed.Id, "/a");
        Assert.NotNull(first);
        var second = store.AddSession(doomed.Id, "/b");
        Assert.NotNull(second);

        Assert.True(store.SoftCloseSession(first!.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var sessionClose = store.PendingCloseSummary!.Id;
        // rename and collapse after the session close, so the session record's captured name is stale
        store.RenameWorkspace(doomed.Id, "renamed");
        store.SetWorkspaceExpanded(doomed.Id, false);
        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var workspaceClose = store.PendingCloseSummary!.Id;

        Assert.True(store.UndoPendingClose(sessionClose));
        // the shell is seeded from the still-pending workspace record, not the stale session record
        var shell = store.Workspaces.FirstOrDefault(w => w.Id == doomed.Id);
        Assert.NotNull(shell);
        Assert.Equal("renamed", shell!.Name);
        Assert.False(shell.IsExpanded);

        Assert.True(store.UndoPendingClose(workspaceClose));
        var restored = store.Workspaces.FirstOrDefault(w => w.Id == doomed.Id);
        Assert.NotNull(restored);
        Assert.Equal("renamed", restored!.Name);
        Assert.False(restored.IsExpanded);
    }

    [Fact]
    public void WorkspaceUndoKeepsEditsMadeToTheRebuiltShell()
    {
        var store = MakeStore();
        var doomed = store.AddWorkspace("old");
        _ = store.AddWorkspace("keep");
        var first = store.AddSession(doomed.Id, "/a");
        Assert.NotNull(first);
        var second = store.AddSession(doomed.Id, "/b");
        Assert.NotNull(second);

        Assert.True(store.SoftCloseSession(first!.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var sessionClose = store.PendingCloseSummary!.Id;
        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var workspaceClose = store.PendingCloseSummary!.Id;

        Assert.True(store.UndoPendingClose(sessionClose));
        // edits to the rebuilt shell are newer than the pending record and must survive the merge
        store.RenameWorkspace(doomed.Id, "new");
        store.SetWorkspaceExpanded(doomed.Id, false);
        Assert.True(store.UndoPendingClose(workspaceClose));

        var restored = store.Workspaces.FirstOrDefault(w => w.Id == doomed.Id);
        Assert.NotNull(restored);
        Assert.Equal("new", restored!.Name);
        Assert.False(restored.IsExpanded);
    }

    [Fact]
    public void RestoringRecentSessionThenUndoingWorkspaceCloseKeepsOneWorkspace()
    {
        var store = MakeStore();
        var doomed = store.AddWorkspace("doomed");
        _ = store.AddWorkspace("keep");
        var first = store.AddSession(doomed.Id, "/a");
        Assert.NotNull(first);
        var second = store.AddSession(doomed.Id, "/b");
        Assert.NotNull(second);

        Assert.True(store.SoftCloseSession(first!.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var sessionClose = store.PendingCloseSummary!.Id;
        store.FinalizePendingClose(sessionClose);
        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var workspaceClose = store.PendingCloseSummary!.Id;

        // a finalized session reopens from Open Recent, which rebuilds the workspace shell on its own
        var recent = new RecentClosedItem
        {
            Id = Guid.NewGuid(),
            Kind = RecentClosedKind.Session,
            Title = "a",
            Subtitle = "doomed",
            Session = new RecentClosedSession
            {
                WorkspaceId = doomed.Id,
                WorkspaceName = "doomed",
                WorkspaceIndex = 0,
                SessionIndex = 0,
                Snapshot = store.SessionSnapshot(first),
            },
        };
        Assert.True(store.RestoreRecentClosed(recent));
        Assert.True(store.UndoPendingClose(workspaceClose));

        Assert.Equal(1, store.Workspaces.Count(w => w.Id == doomed.Id));
        var restored = store.Workspaces.FirstOrDefault(w => w.Id == doomed.Id);
        Assert.NotNull(restored);
        Assert.Equal(new HashSet<Guid> { first.Id, second!.Id }, restored!.Sessions.Select(s => s.Id).ToHashSet());
        Assert.Equal(2, restored.Sessions.Count);
        Assert.Equal(second.Id, store.SelectedSessionId);
        // the reopened session is rebuilt from its snapshot, so it is a fresh object; the merged-in
        // one is the live object the pending record held
        Assert.Contains(restored.Sessions, s => ReferenceEquals(s, second));
        Assert.All(restored.Sessions, s => Assert.NotSame(first, s));
    }

    [Fact]
    public void UndoingWorkspaceCloseMergesDisjointSessionsIntoRebuiltShell()
    {
        var store = MakeStore();
        var doomed = store.AddWorkspace("doomed");
        _ = store.AddWorkspace("keep");
        var first = store.AddSession(doomed.Id, "/a");
        Assert.NotNull(first);
        var second = store.AddSession(doomed.Id, "/b");
        Assert.NotNull(second);

        // close the second session, so the workspace snapshot carries only the first
        Assert.True(store.SoftCloseSession(second!.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var sessionClose = store.PendingCloseSummary!.Id;
        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var workspaceClose = store.PendingCloseSummary!.Id;

        Assert.True(store.UndoPendingClose(sessionClose));
        Assert.True(store.UndoPendingClose(workspaceClose));

        Assert.Equal(1, store.Workspaces.Count(w => w.Id == doomed.Id));
        var restored = store.Workspaces.FirstOrDefault(w => w.Id == doomed.Id);
        Assert.NotNull(restored);
        // the shell keeps its slot, so the merged order trails the shell's session
        Assert.Equal(new[] { second.Id, first!.Id }, restored!.Sessions.Select(s => s.Id));
        Assert.Contains(restored.Sessions, s => ReferenceEquals(s, first));
        Assert.Contains(restored.Sessions, s => ReferenceEquals(s, second));
    }

    [Fact]
    public void ReopeningRecentWorkspaceMergesMissingSessionsIntoRebuiltShell()
    {
        var (store, _, persistence) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var doomed = store.AddWorkspace("doomed");
        _ = store.AddWorkspace("keep");
        var first = store.AddSession(doomed.Id, "/a");
        Assert.NotNull(first);
        var second = store.AddSession(doomed.Id, "/b");
        Assert.NotNull(second);
        var firstSnapshot = store.SessionSnapshot(first!);
        // snapshot the workspace while it still holds both, so it overlaps the shell the session restore
        // rebuilds. a disjoint snapshot would merge cleanly even without the live-session filter.
        var doomedWorkspace = store.Workspaces.FirstOrDefault(w => w.Id == doomed.Id);
        Assert.NotNull(doomedWorkspace);
        var workspaceSnapshot = store.WorkspaceSnapshot(doomedWorkspace!);

        // both closes finalize, so only the recent snapshots remain
        Assert.True(store.SoftCloseSession(first!.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        store.FinalizePendingClose(store.PendingCloseSummary!.Id);
        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        store.FinalizePendingClose(store.PendingCloseSummary!.Id);

        // reopening the session rebuilds the workspace as a shell holding only that session
        var recentSession = new RecentClosedItem
        {
            Id = Guid.NewGuid(),
            Kind = RecentClosedKind.Session,
            Title = "a",
            Subtitle = "doomed",
            Session = new RecentClosedSession
            {
                WorkspaceId = doomed.Id,
                WorkspaceName = "doomed",
                WorkspaceIndex = 0,
                SessionIndex = 0,
                Snapshot = firstSnapshot,
            },
        };
        Assert.True(store.RestoreRecentClosed(recentSession));

        // reopening the workspace must bring back the session the shell doesn't hold
        var recentWorkspace = new RecentClosedItem
        {
            Id = Guid.NewGuid(),
            Kind = RecentClosedKind.Workspace,
            Title = "doomed",
            Subtitle = "2 sessions",
            Workspace = new RecentClosedWorkspace
            {
                Snapshot = workspaceSnapshot,
                SelectedSessionId = second!.Id,
            },
        };
        Assert.True(store.RestoreRecentClosed(recentWorkspace));

        var restored = store.Workspaces.FirstOrDefault(w => w.Id == doomed.Id);
        Assert.NotNull(restored);
        // count, not just the id set: the shell already holds `first`, and a merge that skips the
        // live-session filter appends a second copy of it under the same id
        Assert.Equal(2, restored!.Sessions.Count);
        Assert.Equal(new HashSet<Guid> { first.Id, second.Id }, restored.Sessions.Select(s => s.Id).ToHashSet());
        Assert.NotNull(store.SessionWithId(second.Id));
        Assert.Equal(second.Id, store.SelectedSessionId);
        // the merge persists immediately, ahead of the debounced selection save
        Assert.Equal(2, persistence.Load().Workspaces.FirstOrDefault(w => w.Id == doomed.Id)?.Sessions.Length);
    }

    [Fact]
    public void ReopeningRecentWorkspaceRestoresSessionsHeldByAPendingSessionClose()
    {
        var store = MakeStore();
        var doomed = store.AddWorkspace("doomed");
        _ = store.AddWorkspace("keep");
        var first = store.AddSession(doomed.Id, "/a");
        Assert.NotNull(first);
        var second = store.AddSession(doomed.Id, "/b");
        Assert.NotNull(second);
        var firstSnapshot = store.SessionSnapshot(first!);
        var doomedWorkspace = store.Workspaces.FirstOrDefault(w => w.Id == doomed.Id);
        Assert.NotNull(doomedWorkspace);
        var workspaceSnapshot = store.WorkspaceSnapshot(doomedWorkspace!);

        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        store.FinalizePendingClose(store.PendingCloseSummary!.Id);

        // reopen `first` alone, rebuilding the workspace as a shell, then soft-close it again so a
        // pending session close holds it while the workspace's recent entry still lists both sessions
        var recentSession = new RecentClosedItem
        {
            Id = Guid.NewGuid(),
            Kind = RecentClosedKind.Session,
            Title = "a",
            Subtitle = "doomed",
            Session = new RecentClosedSession
            {
                WorkspaceId = doomed.Id,
                WorkspaceName = "doomed",
                WorkspaceIndex = 0,
                SessionIndex = 0,
                Snapshot = firstSnapshot,
            },
        };
        Assert.True(store.RestoreRecentClosed(recentSession));
        var rebuiltFirst = store.Workspaces.FirstOrDefault(w => w.Id == doomed.Id)?.Sessions.FirstOrDefault();
        Assert.NotNull(rebuiltFirst);
        Assert.True(store.SoftCloseSession(rebuiltFirst!.Id, 60));

        // the pending session close matches the workspace's recent entry, but undoing it restores only
        // `first`. the workspace restore must still rebuild `second`, which nothing else holds.
        var recentWorkspace = new RecentClosedItem
        {
            Id = Guid.NewGuid(),
            Kind = RecentClosedKind.Workspace,
            Title = "doomed",
            Subtitle = "2 sessions",
            Workspace = new RecentClosedWorkspace
            {
                Snapshot = workspaceSnapshot,
                SelectedSessionId = second!.Id,
            },
        };
        Assert.True(store.RestoreRecentClosed(recentWorkspace));

        var restored = store.Workspaces.FirstOrDefault(w => w.Id == doomed.Id);
        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Sessions.Count);
        Assert.Equal(new HashSet<Guid> { first.Id, second.Id }, restored.Sessions.Select(s => s.Id).ToHashSet());
        Assert.NotNull(store.SessionWithId(second.Id));
    }

    [Fact]
    public void ReopeningRecentWorkspaceDrainsEveryPendingSessionClose()
    {
        var store = MakeStore();
        var doomed = store.AddWorkspace("doomed");
        _ = store.AddWorkspace("keep");
        var first = store.AddSession(doomed.Id, "/a");
        Assert.NotNull(first);
        var second = store.AddSession(doomed.Id, "/b");
        Assert.NotNull(second);
        var firstSurface = new SpySurface();
        first!.Surface = firstSurface;
        var doomedWorkspace = store.Workspaces.FirstOrDefault(w => w.Id == doomed.Id);
        Assert.NotNull(doomedWorkspace);
        var workspaceSnapshot = store.WorkspaceSnapshot(doomedWorkspace!);

        // both sessions are held by their own pending close, so neither is live when the workspace's
        // recent entry is reopened
        Assert.True(store.SoftCloseSession(first.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var firstClose = store.PendingCloseSummary!.Id;
        Assert.True(store.SoftCloseSession(second!.Id, 60));

        var recentWorkspace = new RecentClosedItem
        {
            Id = Guid.NewGuid(),
            Kind = RecentClosedKind.Workspace,
            Title = "doomed",
            Subtitle = "2 sessions",
            Workspace = new RecentClosedWorkspace
            {
                Snapshot = workspaceSnapshot,
                SelectedSessionId = first.Id,
            },
        };
        Assert.True(store.RestoreRecentClosed(recentWorkspace));

        var restored = store.Workspaces.FirstOrDefault(w => w.Id == doomed.Id);
        Assert.NotNull(restored);
        // the live originals come back, not snapshot rebuilds sharing their ids
        Assert.Equal(2, restored!.Sessions.Count);
        Assert.Contains(restored.Sessions, s => ReferenceEquals(s, first));
        Assert.Contains(restored.Sessions, s => ReferenceEquals(s, second));
        // no pending record still holds a session the tree now shows: undoing one would duplicate its id,
        // finalizing one would tear down the surfaces of a session the user can see
        Assert.Empty(store.PendingCloseRecords);
        Assert.False(store.UndoPendingClose(firstClose));
        Assert.Equal(1, store.Workspaces.SelectMany(w => w.Sessions).Count(s => s.Id == first.Id));
        Assert.Equal(0, firstSurface.TeardownCount);
    }

    [Fact]
    public void ClosingARebuiltShellFoldsIntoTheStillPendingWorkspaceClose()
    {
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var doomed = store.AddWorkspace("doomed");
        _ = store.AddWorkspace("keep");
        var first = store.AddSession(doomed.Id, "/a");
        Assert.NotNull(first);
        var second = store.AddSession(doomed.Id, "/b");
        Assert.NotNull(second);

        // close `first`, then the workspace holding `second`, then undo `first` so it lands in a rebuilt
        // shell. closing that shell must not leave a second pending record sharing the workspace id:
        // both would key one Open Recent entry, and the newer snapshot evicts the older one's sessions.
        Assert.True(store.SoftCloseSession(first!.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var sessionClose = store.PendingCloseSummary!.Id;
        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var firstWorkspaceClose = store.PendingCloseSummary!.Id;
        Assert.True(store.UndoPendingClose(sessionClose));
        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var foldedClose = store.PendingCloseSummary!.Id;

        Assert.NotEqual(firstWorkspaceClose, foldedClose);
        Assert.Equal(1, store.PendingCloseRecords.Count);
        // the superseded record is gone with its timer, so finalizing it cannot tear down the folded one
        store.FinalizePendingClose(firstWorkspaceClose);
        Assert.Equal(1, store.PendingCloseRecords.Count);

        Assert.True(store.UndoPendingClose(foldedClose));
        var restored = store.Workspaces.FirstOrDefault(w => w.Id == doomed.Id);
        Assert.NotNull(restored);
        Assert.Equal(new[] { first.Id, second!.Id }, restored!.Sessions.Select(s => s.Id));
        Assert.Empty(recentClosed.Load());
    }

    [Fact]
    public void FinalizedShellSeedsNameAndExpansionFromTheRecentWorkspaceSnapshot()
    {
        var (store, recentClosed, _) = AppStoreFixtures.MakeStoreWithRecentClosed();
        _ = store.AddWorkspace("keep");
        var workspaceId = Guid.NewGuid();
        var sessionSnapshot = new SessionSnapshot { Id = Guid.NewGuid(), CustomName = "a", Cwd = "/a" };
        // nothing pending describes the workspace, so its newest surviving description is the Open Recent
        // snapshot, taken after the rename and collapse. the session entry's `workspaceName` predates both.
        recentClosed.Record(new RecentClosedItem
        {
            Id = Guid.NewGuid(),
            Kind = RecentClosedKind.Workspace,
            Title = "renamed",
            Subtitle = "1 session",
            Workspace = new RecentClosedWorkspace
            {
                Snapshot = new WorkspaceSnapshot
                {
                    Id = workspaceId,
                    Name = "renamed",
                    Sessions = new[] { sessionSnapshot },
                    Collapsed = true,
                },
                SelectedSessionId = null,
            },
        });

        var recentSession = new RecentClosedItem
        {
            Id = Guid.NewGuid(),
            Kind = RecentClosedKind.Session,
            Title = "a",
            Subtitle = "old",
            Session = new RecentClosedSession
            {
                WorkspaceId = workspaceId,
                WorkspaceName = "old",
                WorkspaceIndex = 0,
                SessionIndex = 0,
                Snapshot = sessionSnapshot,
            },
        };
        Assert.True(store.RestoreRecentClosed(recentSession));

        var shell = store.Workspaces.FirstOrDefault(w => w.Id == workspaceId);
        Assert.NotNull(shell);
        Assert.Equal("renamed", shell!.Name);
        Assert.False(shell.IsExpanded);
    }

    [Fact]
    public void ReopeningRecentWorkspaceLeavesAForeignPendingWorkspaceCloseAlone()
    {
        var store = MakeStore();
        var wsW = store.AddWorkspace("W");
        var wsV = store.AddWorkspace("V");
        var moved = store.AddSession(wsW.Id, "/moved");
        Assert.NotNull(moved);
        var wWorkspace = store.Workspaces.FirstOrDefault(w => w.Id == wsW.Id);
        Assert.NotNull(wWorkspace);
        var staleSnapshot = store.WorkspaceSnapshot(wWorkspace!);

        // the session moves to V, then the user closes V deliberately. W's recent entry still lists it.
        store.MoveSession(moved!.Id, wsV.Id);
        Assert.True(store.SoftRemoveWorkspace(wsV.Id, 60));

        var recentW = new RecentClosedItem
        {
            Id = Guid.NewGuid(),
            Kind = RecentClosedKind.Workspace,
            Title = "W",
            Subtitle = "1 session",
            Workspace = new RecentClosedWorkspace { Snapshot = staleSnapshot, SelectedSessionId = moved.Id },
        };
        _ = store.RestoreRecentClosed(recentW);

        // reopening W must not resurrect V, and must not rebuild `moved` beside the original V still holds
        Assert.False(store.Workspaces.Any(w => w.Id == wsV.Id));
        Assert.Equal(1, store.PendingCloseRecords.Count);
        Assert.Equal(0, store.Workspaces.SelectMany(w => w.Sessions).Count(s => s.Id == moved.Id));
    }

    [Fact]
    public void ReopeningRecentWorkspaceNeverRebuildsASessionAForeignPendingCloseHolds()
    {
        var store = MakeStore();
        var wsW = store.AddWorkspace("W");
        var wsV = store.AddWorkspace("V");
        _ = store.AddWorkspace("keep");
        _ = store.AddSession(wsV.Id, "/anchor");
        var moved = store.AddSession(wsW.Id, "/moved");
        Assert.NotNull(moved);
        var wWorkspace = store.Workspaces.FirstOrDefault(w => w.Id == wsW.Id);
        Assert.NotNull(wWorkspace);
        var staleSnapshot = store.WorkspaceSnapshot(wWorkspace!);

        store.MoveSession(moved!.Id, wsV.Id);
        // W is gone for good, so reopening it rebuilds the workspace wholesale
        Assert.True(store.SoftRemoveWorkspace(wsW.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        store.FinalizePendingClose(store.PendingCloseSummary!.Id);
        Assert.True(store.SoftRemoveWorkspace(wsV.Id, 60));
        Assert.NotNull(store.PendingCloseSummary);
        var vClose = store.PendingCloseSummary!.Id;

        var recentW = new RecentClosedItem
        {
            Id = Guid.NewGuid(),
            Kind = RecentClosedKind.Workspace,
            Title = "W",
            Subtitle = "1 session",
            Workspace = new RecentClosedWorkspace { Snapshot = staleSnapshot, SelectedSessionId = moved.Id },
        };
        _ = store.RestoreRecentClosed(recentW);
        Assert.True(store.PendingCloseRecords.ContainsKey(vClose));

        // undoing V returns the original `moved`; the rebuilt W must not have made a second one
        Assert.True(store.UndoPendingClose(vClose));
        Assert.Equal(1, store.Workspaces.SelectMany(w => w.Sessions).Count(s => s.Id == moved.Id));
        Assert.Contains(store.Workspaces.SelectMany(w => w.Sessions), s => ReferenceEquals(s, moved));
    }

    [Fact]
    public void RestoreDropsASessionRepeatedInsideOneWorkspace()
    {
        var store = MakeStore();
        var repeated = Guid.NewGuid();
        store.Restore(new Snapshot
        {
            Workspaces = new[]
            {
                new WorkspaceSnapshot
                {
                    Id = Guid.NewGuid(),
                    Name = "one",
                    Sessions = new[]
                    {
                        new SessionSnapshot { Id = repeated, CustomName = "a", Cwd = "/a" },
                        new SessionSnapshot { Id = repeated, CustomName = "a-again", Cwd = "/b" },
                    },
                },
            },
        });

        Assert.Equal(1, store.Workspaces.SelectMany(w => w.Sessions).Count(s => s.Id == repeated));
        Assert.Equal("a", store.Workspaces[0].Sessions[0].CustomName);
    }

    [Fact]
    public void RestoreDropsASessionRepeatedAcrossWorkspacesWithDifferentIDs()
    {
        var store = MakeStore();
        var repeated = Guid.NewGuid();
        store.Restore(new Snapshot
        {
            Workspaces = new[]
            {
                new WorkspaceSnapshot
                {
                    Id = Guid.NewGuid(), Name = "one",
                    Sessions = new[] { new SessionSnapshot { Id = repeated, CustomName = "a", Cwd = "/a" } },
                },
                new WorkspaceSnapshot
                {
                    Id = Guid.NewGuid(), Name = "two",
                    Sessions = new[] { new SessionSnapshot { Id = repeated, CustomName = "a-again", Cwd = "/b" } },
                },
            },
        });

        Assert.Equal(2, store.Workspaces.Count);
        Assert.Equal(1, store.Workspaces.SelectMany(w => w.Sessions).Count(s => s.Id == repeated));
        Assert.Empty(store.Workspaces[1].Sessions);
    }

    [Fact]
    public void RestoreFoldsWorkspacesSharingAnIDIntoOne()
    {
        var store = MakeStore();
        var shared = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var snapshot = new Snapshot
        {
            Workspaces = new[]
            {
                new WorkspaceSnapshot
                {
                    Id = shared, Name = "one",
                    Sessions = new[] { new SessionSnapshot { Id = firstId, CustomName = "a", Cwd = "/a" } },
                },
                new WorkspaceSnapshot
                {
                    Id = shared, Name = "dupe",
                    Sessions = new[] { new SessionSnapshot { Id = secondId, CustomName = "b", Cwd = "/b" } },
                },
            },
        };

        store.Restore(snapshot);

        Assert.Equal(1, store.Workspaces.Count);
        Assert.Equal("one", store.Workspaces[0].Name);
        Assert.Equal(new[] { firstId, secondId }, store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.NotNull(store.SessionWithId(secondId));
    }

    [Fact]
    public void RestoreDropsASessionRepeatedAcrossWorkspacesSharingAnID()
    {
        var store = MakeStore();
        var shared = Guid.NewGuid();
        var repeated = Guid.NewGuid();
        var snapshot = new Snapshot
        {
            Workspaces = new[]
            {
                new WorkspaceSnapshot
                {
                    Id = shared, Name = "one",
                    Sessions = new[] { new SessionSnapshot { Id = repeated, CustomName = "a", Cwd = "/a" } },
                },
                new WorkspaceSnapshot
                {
                    Id = shared, Name = "dupe",
                    Sessions = new[] { new SessionSnapshot { Id = repeated, CustomName = "a-again", Cwd = "/b" } },
                },
            },
        };

        store.Restore(snapshot);

        Assert.Equal(1, store.Workspaces.Count);
        Assert.Equal(new[] { repeated }, store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.Equal("a", store.Workspaces[0].Sessions[0].CustomName);
    }

    [Fact]
    public void FinalizedSoftRemoveWorkspaceTearsDownSessions()
    {
        var store = MakeStore();
        _ = store.AddWorkspace("keep");
        var doomed = store.AddWorkspace("doomed");
        var session = store.AddSession(doomed.Id, "/a");
        Assert.NotNull(session);
        var surface = new SpySurface();
        session!.Surface = surface;

        Assert.True(store.SoftRemoveWorkspace(doomed.Id, 60));
        var summary = store.PendingCloseSummary;
        Assert.NotNull(summary);
        store.FinalizePendingClose(summary!.Id);

        Assert.Equal(1, surface.TeardownCount);
        Assert.Equal(new[] { "keep" }, store.Workspaces.Select(w => w.Name));
    }

    [Fact]
    public void RemoveWorkspaceTearsDownSessionsAndPrunesRecency()
    {
        var store = MakeStore();
        var keep = store.AddWorkspace("keep");
        var doomed = store.AddWorkspace("doomed");
        var session = store.AddSession(doomed.Id, "/a")!;
        var surface = new SpySurface();
        session.Surface = surface;
        var split = new SpySurface();
        session.SplitSurface = split;
        store.RemoveWorkspace(doomed.Id);
        Assert.Equal(new[] { keep.Id }, store.Workspaces.Select(w => w.Id));
        Assert.Equal(1, surface.TeardownCount);
        Assert.Equal(1, split.TeardownCount);
        Assert.DoesNotContain(session.Id, store.SessionRecency.Items);
    }

    [Fact]
    public void RemoveWorkspaceReselectsWhenActiveInside()
    {
        var store = MakeStore();
        var keep = store.AddWorkspace("keep");
        var kept = store.AddSession(keep.Id, "/k")!;
        var doomed = store.AddWorkspace("doomed");
        var active = store.AddSession(doomed.Id, "/a")!;
        store.SelectSession(active.Id);
        store.RemoveWorkspace(doomed.Id);
        Assert.Equal(kept.Id, store.SelectedSessionId);
    }

    [Fact]
    public void RemoveWorkspaceLeavesSelectionWhenActiveElsewhere()
    {
        var store = MakeStore();
        var keep = store.AddWorkspace("keep");
        var active = store.AddSession(keep.Id, "/k")!;
        var doomed = store.AddWorkspace("doomed");
        _ = store.AddSession(doomed.Id, "/d")!;
        store.SelectSession(active.Id);
        store.RemoveWorkspace(doomed.Id);
        Assert.Equal(active.Id, store.SelectedSessionId);
    }

    [Fact]
    public void RemoveWorkspaceKeepsAtLeastOne()
    {
        var store = MakeStore();
        var only = store.AddWorkspace("only");
        Assert.False(store.CanRemoveWorkspace);
        store.RemoveWorkspace(only.Id);
        Assert.Equal(new[] { only.Id }, store.Workspaces.Select(w => w.Id));
    }

    [Fact]
    public void SelectionUpdatesRecencyMostRecentFirst()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        Assert.Equal(new[] { b.Id, a.Id }, store.SessionRecency.Items); // b selected last
        store.SelectSession(a.Id);
        Assert.Equal(new[] { a.Id, b.Id }, store.SessionRecency.Items); // a now front, b is the previous
        Assert.Equal(b.Id, store.SessionRecency.Items[1]);
    }

    [Fact]
    public void CloseSessionRemovesFromRecency()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        store.CloseSession(b.Id);
        Assert.DoesNotContain(b.Id, store.SessionRecency.Items);
        Assert.Equal(new[] { a.Id }, store.SessionRecency.Items);
    }

    [Fact]
    public void RecentSessionsReturnsMostRecentFirstRespectingLimit()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        var c = store.AddSession(ws.Id, "/c")!;
        // drive the recency order explicitly (selectSession pushes MRU front): most-recent is the last select.
        store.SelectSession(a.Id);
        store.SelectSession(c.Id);
        store.SelectSession(b.Id);
        Assert.Equal(new[] { b.Id, c.Id, a.Id }, store.RecentSessions(9)); // fewer than the limit → all, mru first
        Assert.Equal(new[] { b.Id, c.Id }, store.RecentSessions(2)); // limit clamps the count
    }

    [Fact]
    public void RecentSessionsSpansWorkspacesAndSkipsClosed()
    {
        var store = MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var a = store.AddSession(work.Id, "/a")!;
        var b = store.AddSession(personal.Id, "/b")!;
        store.SelectSession(a.Id);
        store.SelectSession(b.Id);
        Assert.Equal(new[] { b.Id, a.Id }, store.RecentSessions(9)); // recency spans every workspace
        store.CloseSession(b.Id); // closed sessions are pruned from recency, so they drop out
        Assert.Equal(new[] { a.Id }, store.RecentSessions(9));
    }

    [Fact]
    public void SetFontSizeRecordsValue()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        Assert.Null(session.FontSize);
        store.SetFontSize(session.Id, 16);
        Assert.Equal(16d, session.FontSize);
        store.SetFontSize(session.Id, 16); // unchanged: no-op, value unchanged
        Assert.Equal(16d, session.FontSize);
        store.SetFontSize(session.Id, 13);
        Assert.Equal(13d, session.FontSize);
    }

    [Fact]
    public void ResetSessionFontSizesClearsAllOverrides()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!;
        store.SetFontSize(a.Id, 16);
        store.SetFontSize(b.Id, 18);
        store.ResetSessionFontSizes();
        Assert.Null(a.FontSize);
        Assert.Null(b.FontSize);
    }

    [Fact]
    public void SetFontSizeUnknownSessionIsIgnored()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        store.SetFontSize(Guid.NewGuid(), 20);
        Assert.Null(session.FontSize);
    }

    [Fact]
    public void SelectSessionThenSavePersistsSelectionToDisk()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"agterm-tests-{Upper(Guid.NewGuid())}");
        var persistence = new PersistenceStore(dir);
        var store = new AppStore(persistence: persistence);
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        _ = store.AddSession(ws.Id, "/b")!;
        store.SelectSession(a.Id);
        store.Save(); // selection saves are debounced; save() flushes the write
        Assert.Equal(a.Id, persistence.Load().SelectedSessionId); // persisted to disk, not just in-memory
    }

    [Fact]
    public void RapidSelectionAndFontThenSavePersistsLatestSnapshot()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"agterm-tests-{Upper(Guid.NewGuid())}");
        var persistence = new PersistenceStore(dir);
        var store = new AppStore(persistence: persistence);
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        _ = store.AddSession(ws.Id, "/b")!;
        var c = store.AddSession(ws.Id, "/c")!;
        // a burst of debounced selection/font changes; save() writes the latest state once
        store.SelectSession(a.Id);
        store.SelectSession(c.Id);
        store.SetFontSize(c.Id, 18);
        store.Save();
        var loaded = persistence.Load();
        Assert.Equal(c.Id, loaded.SelectedSessionId);               // the latest selection won
        Assert.Equal(18d, loaded.Workspaces[0].Sessions[2].FontSize); // and the latest font change landed
    }

    [Fact]
    public void SelectSessionDefersWriteUntilSaveFlushes()
    {
        // guards the DEBOUNCE itself: selectSession must NOT write synchronously. addSession saves
        // immediately (structural), so disk shows the last-added session selected; a debounced
        // selectSession leaves the disk unchanged until save() flushes. A revert to a synchronous
        // save() in selectSession fails the middle assertion.
        var dir = Path.Combine(Path.GetTempPath(), $"agterm-tests-{Upper(Guid.NewGuid())}");
        var persistence = new PersistenceStore(dir);
        var store = new AppStore(persistence: persistence);
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        var b = store.AddSession(ws.Id, "/b")!; // structural save: disk now selects b
        Assert.Equal(b.Id, persistence.Load().SelectedSessionId);
        store.SelectSession(a.Id);                                // debounced — must not hit disk yet
        Assert.Equal(b.Id, persistence.Load().SelectedSessionId); // still b: the write was deferred
        store.Save();                                             // flush
        Assert.Equal(a.Id, persistence.Load().SelectedSessionId); // now a is persisted
    }

    [Fact]
    public void SetFontSizeDefersWriteUntilSaveFlushes()
    {
        // same guard for setFontSize: the per-keystroke font change is debounced, not synchronous.
        var dir = Path.Combine(Path.GetTempPath(), $"agterm-tests-{Upper(Guid.NewGuid())}");
        var persistence = new PersistenceStore(dir);
        var store = new AppStore(persistence: persistence);
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!; // structural save: fontSize nil on disk
        Assert.Null(persistence.Load().Workspaces[0].Sessions[0].FontSize);
        store.SetFontSize(a.Id, 18);                              // debounced — must not hit disk yet
        Assert.Null(persistence.Load().Workspaces[0].Sessions[0].FontSize); // still nil: deferred
        store.Save();                                             // flush
        Assert.Equal(18d, persistence.Load().Workspaces[0].Sessions[0].FontSize);
    }

    [Fact]
    public void CloseUnknownSessionIsIgnored()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a")!;
        store.SelectSession(a.Id);
        store.CloseSession(Guid.NewGuid());
        Assert.Equal(new[] { a.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
        Assert.Equal(a.Id, store.SelectedSessionId);
    }

    [Fact]
    public void RenameUnknownWorkspaceIsIgnored()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        store.RenameWorkspace(Guid.NewGuid(), "renamed");
        Assert.Equal(new[] { "work" }, store.Workspaces.Select(w => w.Name));
        Assert.Equal(ws.Id, store.Workspaces[0].Id);
    }

    [Fact]
    public void MutationSurvivesSaveFailure()
    {
        // Windows adaptation of the Swift "/dev/null/agterm-cannot-write" unwritable directory: a
        // directory path nested under an existing FILE can never be created, so every save throws
        // IOException and is swallowed.
        var blocker = Path.Combine(Path.GetTempPath(), "agterm-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "");
        var unwritable = Path.Combine(blocker, "agterm-cannot-write");
        var store = new AppStore(persistence: new PersistenceStore(unwritable));
        var ws = store.AddWorkspace("work");
        // save() to an unwritable directory is swallowed; the in-memory mutation stands.
        var session = store.AddSession(ws.Id, "/a");
        Assert.NotNull(session);
        Assert.Equal(1, store.Workspaces[0].Sessions.Count);
    }

    [Fact]
    public void RenameSessionSetsCustomName()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        store.RenameSession(session.Id, "build");
        Assert.Equal("build", session.CustomName);
        Assert.Equal("build", session.DisplayName);
    }

    [Fact]
    public void RenameSessionWithBlankClearsCustomName()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/Users/user/foo")!;
        store.RenameSession(session.Id, "build");
        store.RenameSession(session.Id, "   ");
        Assert.Null(session.CustomName);
        Assert.Equal("foo", session.DisplayName);
    }

    [Fact]
    public void RenameSessionTrimsWhitespace()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        store.RenameSession(session.Id, "  build  ");
        Assert.Equal("build", session.CustomName);
    }

    [Fact]
    public void RenameWorkspaceSetsName()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        store.RenameWorkspace(ws.Id, "personal");
        Assert.Equal("personal", store.Workspaces[0].Name);
    }

    [Fact]
    public void RenameWorkspaceWithBlankIsIgnored()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        store.RenameWorkspace(ws.Id, "   ");
        Assert.Equal("work", store.Workspaces[0].Name);
    }

    [Fact]
    public void SetBackgroundWatermarkReportsWhetherChanged()
    {
        // the change-gate the control server uses to skip a redundant per-surface config apply (which
        // retains an owned config freed only at teardown) on a scripted set-loop.
        var store = MakeStore();
        var ws = store.AddWorkspace("w");
        var session = store.AddSession(ws.Id, "/tmp")!;
        var mark = new BackgroundWatermark { Kind = WatermarkKind.Text, Text = "PROD" };

        Assert.True(store.SetBackgroundWatermark(mark, session.Id));        // first set changes
        Assert.False(store.SetBackgroundWatermark(mark, session.Id));       // identical re-set: no change
        Assert.True(store.SetBackgroundWatermark(null, session.Id));        // clear changes
        Assert.False(store.SetBackgroundWatermark(null, session.Id));       // clear again: no change
        Assert.False(store.SetBackgroundWatermark(mark, Guid.NewGuid()));   // unknown id: no change
    }

    [Fact]
    public void ControlTreeProjectsWorkspaceAndSessionShape()
    {
        var store = MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var a = store.AddSession(work.Id, "/repo/a", name: "alpha");
        Assert.NotNull(a);
        var b = store.AddSession(personal.Id, "/repo/b");
        Assert.NotNull(b);
        b!.CurrentCwd = "/live/b";
        b.OscTitle = "remote:~/b";
        b.IsSplit = true;
        b.HasSplit = true;
        b.SplitSurface = new SpySurface(); // a live split pane, so the `.right` status below stays valid
        b.OverlayActive = true;
        b.ScratchActive = true;
        b.Flagged = true;
        b.BackgroundWatermark = new BackgroundWatermark { Kind = WatermarkKind.Text, Text = "PROD" };
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, StatusPane: StatusPane.Right), b.Id);
        store.SelectSession(b.Id);

        var tree = store.ControlTree();

        Assert.Equal(new[] { Upper(work.Id), Upper(personal.Id) }, tree.Workspaces.Select(w => w.Id));
        Assert.Equal(new[] { "work", "personal" }, tree.Workspaces.Select(w => w.Name));
        Assert.Equal(new[] { false, true }, tree.Workspaces.Select(w => w.Active));
        Assert.Equal(
            CanonicalJson.Of(new[]
            {
                new ControlSessionNode
                {
                    Id = Upper(a!.Id), Name = "alpha", Cwd = "/repo/a",
                    Active = false, Split = false,
                    Surfaces = new[]
                    {
                        new ControlSurfaceNode
                        {
                            Id = new TerminalSurfaceId(a.Id, TerminalZoomSurface.Primary).RawValue,
                            Kind = "left", Active = true, Visible = true,
                        },
                    },
                },
            }),
            CanonicalJson.Of(tree.Workspaces[0].Sessions));
        Assert.Equal(
            CanonicalJson.Of(new[]
            {
                new ControlSessionNode
                {
                    Id = Upper(b.Id), Name = "remote:~/b", Cwd = "/live/b",
                    Title = "remote:~/b", Active = true, Split = true,
                    SplitFocused = false,
                    Overlay = true, Scratch = true, Flagged = true,
                    Status = "blocked", StatusPane = "right",
                    Background = new BackgroundWatermark { Kind = WatermarkKind.Text, Text = "PROD" },
                    Surfaces = new[]
                    {
                        new ControlSurfaceNode
                        {
                            Id = new TerminalSurfaceId(b.Id, TerminalZoomSurface.Primary).RawValue,
                            Kind = "left", Active = false, Visible = false,
                        },
                        new ControlSurfaceNode
                        {
                            Id = new TerminalSurfaceId(b.Id, TerminalZoomSurface.Split).RawValue,
                            Kind = "right", Active = false, Visible = false,
                        },
                        new ControlSurfaceNode
                        {
                            Id = new TerminalSurfaceId(b.Id, TerminalZoomSurface.Scratch).RawValue,
                            Kind = "scratch", Active = false, Visible = false,
                        },
                        new ControlSurfaceNode
                        {
                            Id = new TerminalSurfaceId(b.Id, TerminalZoomSurface.Overlay).RawValue,
                            Kind = "overlay", Active = true, Visible = true,
                        },
                    },
                },
            }),
            CanonicalJson.Of(tree.Workspaces[1].Sessions));
    }

    [Fact]
    public void ControlTreeReportsSidebarVisibility()
    {
        var store = MakeStore();
        Assert.True(store.ControlTree().SidebarVisible); // default: sidebar shown
        store.SetSidebarVisible(false);
        Assert.False(store.ControlTree().SidebarVisible);
        store.SetSidebarVisible(true);
        Assert.True(store.ControlTree().SidebarVisible);
    }

    [Fact]
    public void ControlTreeReportsFocusedWorkspace()
    {
        var store = MakeStore();
        var ws2 = store.AddWorkspace("second");
        // no focus: no workspace node reports focused.
        Assert.All(store.ControlTree().Workspaces, w => Assert.Null(w.Focused));
        // focus the second workspace: ONLY its node reports focused == true (distinct from active).
        store.SetFocusedWorkspace(ws2.Id);
        var nodes = store.ControlTree().Workspaces;
        Assert.True(nodes.First(n => n.Id == Upper(ws2.Id)).Focused);
        Assert.Equal(1, nodes.Count(n => n.Focused == true));
        // clearing focus: no node reports focused again.
        store.SetFocusedWorkspace(null);
        Assert.All(store.ControlTree().Workspaces, w => Assert.Null(w.Focused));
    }

    [Fact]
    public void ControlTreeReportsCollapsedWorkspace()
    {
        var store = MakeStore();
        var ws2 = store.AddWorkspace("second");
        // all workspaces expanded by default: no node reports collapsed.
        Assert.All(store.ControlTree().Workspaces, w => Assert.Null(w.Collapsed));
        // collapse the second workspace: ONLY its node reports collapsed == true.
        store.SetWorkspaceExpanded(ws2.Id, false);
        var nodes = store.ControlTree().Workspaces;
        Assert.True(nodes.First(n => n.Id == Upper(ws2.Id)).Collapsed);
        Assert.Equal(1, nodes.Count(n => n.Collapsed == true));
        // re-expanding it omits the field again.
        store.SetWorkspaceExpanded(ws2.Id, true);
        Assert.All(store.ControlTree().Workspaces, w => Assert.Null(w.Collapsed));
    }

    [Fact]
    public void ControlTreeCollapsedIsIdempotentAndFocusIndependent()
    {
        var store = MakeStore();
        var ws2 = store.AddWorkspace("second");
        // idempotent: collapsing twice leaves exactly one collapsed node reporting true (delta-guarded).
        store.SetWorkspaceExpanded(ws2.Id, false);
        store.SetWorkspaceExpanded(ws2.Id, false);
        var afterTwice = store.ControlTree().Workspaces;
        Assert.Equal(1, afterTwice.Count(n => n.Collapsed == true));
        Assert.True(afterTwice.First(n => n.Id == Upper(ws2.Id)).Collapsed);
        // focus-independent: focusing the collapsed workspace force-reveals it in the sidebar but must NOT
        // flip the persisted model, so the `collapsed` read-back still reports true.
        store.SetFocusedWorkspace(ws2.Id);
        var focused = store.ControlTree().Workspaces.First(n => n.Id == Upper(ws2.Id));
        Assert.True(focused.Collapsed);
        Assert.True(focused.Focused);
    }

    [Fact]
    public void ControlTreeReportsSidebarMode()
    {
        var store = MakeStore();
        Assert.Equal("tree", store.ControlTree().SidebarMode); // default: the workspace tree
        store.SetSidebarMode(SidebarMode.Flagged);
        Assert.Equal("flagged", store.ControlTree().SidebarMode);
        store.SetSidebarMode(SidebarMode.Tree);
        Assert.Equal("tree", store.ControlTree().SidebarMode);
    }

    [Fact]
    public void ControlTreeReportsQuickVisibleFromClosure()
    {
        var store = MakeStore();
        // no closure (host-free / default): omitted (nil).
        Assert.Null(store.ControlTree().QuickVisible);
        // the app supplies the live QuickTerminalController.isVisible via the closure.
        Assert.True(store.ControlTree(quickVisible: () => true).QuickVisible);
        Assert.False(store.ControlTree(quickVisible: () => false).QuickVisible);
    }

    [Fact]
    public void ControlTreeReportsZoomedSurfaceFromClosure()
    {
        var store = MakeStore();
        // no closure (host-free / default) or nothing zoomed: omitted (nil).
        Assert.Null(store.ControlTree().ZoomedSurface);
        Assert.Null(store.ControlTree(zoomedSurface: () => null).ZoomedSurface);
        // the app supplies the live TerminalZoomController.target?.controlID via the closure.
        var id = $"surface:{Upper(Guid.NewGuid())}:left";
        Assert.Equal(id, store.ControlTree(zoomedSurface: () => id).ZoomedSurface);
        Assert.Equal("quick", store.ControlTree(zoomedSurface: () => "quick").ZoomedSurface);
    }

    [Fact]
    public void ControlTreeReportsDashboardFieldsFromClosures()
    {
        var store = MakeStore();
        // no closures (host-free / default) or nothing open: all four omitted (nil).
        var bare = store.ControlTree();
        Assert.Null(bare.DashboardMembers);
        Assert.Null(bare.DashboardHighlighted);
        Assert.Null(bare.DashboardFontSize);
        Assert.Null(bare.DashboardFontMode);
        // the app supplies the live DashboardController state via the closures. Members are pane refs now
        // (`<uuid>:left`/`:right`): a split session shows as both its `:left` and `:right` cells.
        var members = new[] { "9f3c:left", "9f3c:right", "abcd:left" };
        var tree = store.ControlTree(dashboardMembers: () => members, dashboardHighlighted: () => "9f3c:right",
                                     dashboardFontSize: () => 12, dashboardFontMode: () => "auto");
        Assert.Equal(members, tree.DashboardMembers);
        Assert.Equal("9f3c:right", tree.DashboardHighlighted);
        Assert.Equal(12d, tree.DashboardFontSize);
        Assert.Equal("auto", tree.DashboardFontMode);
    }

    [Fact]
    public void ControlTreeDashboardMembersClosurePassesThroughVerbatim()
    {
        // the closure value is threaded verbatim: an EMPTY array is distinct from nil (omitted). The app
        // side never emits [] (its closure returns nil while the dashboard is closed, and a non-empty member
        // set otherwise), so this pins the boundary — [] passes through as [], nil omits.
        var store = MakeStore();
        Assert.Equal(Array.Empty<string>(),
            store.ControlTree(dashboardMembers: () => Array.Empty<string>()).DashboardMembers);
        Assert.Null(store.ControlTree(dashboardMembers: () => null).DashboardMembers);
    }

    [Fact]
    public void SetSidebarVisiblePostsChangeNotificationOnlyOnChange()
    {
        // the app-target ControlServer observes this to refresh window.list's cached sidebarVisible; the
        // post must fire only on an actual change. (macOS posts .agtermSidebarVisibilityChanged through
        // NotificationCenter with a nil queue; the port surfaces the same synchronous inline signal as
        // the SidebarVisibilityChanged event.)
        var store = MakeStore(); // default sidebarVisible == true
        var n = 0;
        store.SidebarVisibilityChanged += () => n++;
        store.SetSidebarVisible(true);   // unchanged from default -> no post
        Assert.Equal(0, n);
        store.SetSidebarVisible(false);  // change -> post
        Assert.Equal(1, n);
        store.SetSidebarVisible(false);  // unchanged -> no post
        Assert.Equal(1, n);
        store.SetSidebarVisible(true);   // change -> post
        Assert.Equal(2, n);
    }

    [Fact]
    public void ControlTreeReportsUnseenCountWhenPositive()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/repo");
        Assert.NotNull(session);
        session!.UnseenCount = 4;

        var node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);

        Assert.Equal(4, node!.Unseen);
    }

    [Fact]
    public void ControlTreeOmitsUnseenCountWhenZero()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/repo");
        Assert.NotNull(session);
        session!.UnseenCount = 0;

        var node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);

        Assert.Null(node!.Unseen); // zero reads as "no badge", omitted from the wire
    }

    [Fact]
    public void ControlTreeReportsStatusPaneForNonIdleSession()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/repo");
        Assert.NotNull(session);
        session!.HasSplit = true;
        session.SplitSurface = new SpySurface(); // a live split, so a `.right` status is valid (not coerced to `.left`)
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, StatusPane: StatusPane.Right), session.Id);

        var node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);

        Assert.Equal("blocked", node!.Status);
        Assert.Equal("right", node.StatusPane);
    }

    [Fact]
    public void ControlTreeNilsStatusPaneWhenIdleEvenWithPane()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/repo");
        Assert.NotNull(session);
        // an idle indicator carrying a pane must project BOTH status and statusPane as nil, never a
        // self-contradictory (status == nil while statusPane == "right") node
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Idle, StatusPane: StatusPane.Right), session!.Id);

        var node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);

        Assert.Null(node!.Status);
        Assert.Null(node.StatusPane);
    }

    [Fact]
    public void ControlTreeOmitsStatusPaneWhenNonIdleButUnspecified()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/repo");
        Assert.NotNull(session);
        // non-idle status with no pane recorded: status present, statusPane stays nil
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Completed), session!.Id);

        var node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);

        Assert.Equal("completed", node!.Status);
        Assert.Null(node.StatusPane);
    }

    [Fact]
    public void ControlTreeReportsStatusShapeOnlyForAPerCallOverride()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var shaped = store.AddSession(ws.Id, "/shaped");
        Assert.NotNull(shaped);
        var plain = store.AddSession(ws.Id, "/plain");
        Assert.NotNull(plain);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, Shape: StatusShape.Triangle), shaped!.Id);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), plain!.Id);

        var sessions = store.ControlTree().Workspaces[0].Sessions;

        Assert.Equal("triangle", sessions[0].StatusShape);
        Assert.Null(sessions[1].StatusShape); // no per-call shape: the Settings shape / default is not reported
    }

    [Fact]
    public void ControlTreeDropsStatusShapeOnTheNextSetWithoutOne()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/repo");
        Assert.NotNull(session);
        // the per-call shape is ephemeral: each session.status builds a whole new indicator, so a following
        // set that names no shape replaces it rather than inheriting it — the discard contract itself
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, Shape: StatusShape.Triangle), session!.Id);
        Assert.Equal("triangle", store.ControlTree().Workspaces[0].Sessions[0].StatusShape);

        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), session.Id);

        Assert.Null(store.ControlTree().Workspaces[0].Sessions[0].StatusShape);
        Assert.Equal("blocked", store.ControlTree().Workspaces[0].Sessions[0].Status); // only the shape reverted
    }

    [Fact]
    public void ControlTreeNilsStatusShapeWhenIdleEvenWithShape()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/repo");
        Assert.NotNull(session);
        // idle renders no glyph, so a retained shape must not project — mirroring statusPane/statusColor
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Idle, Shape: StatusShape.Star), session!.Id);

        var node = store.ControlTree().Workspaces[0].Sessions.FirstOrDefault();
        Assert.NotNull(node);

        Assert.Null(node!.Status);
        Assert.Null(node.StatusShape);
    }

    [Fact]
    public void ControlTreeUsesForegroundLookups()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var active = store.AddSession(ws.Id, "/active");
        Assert.NotNull(active);
        var other = store.AddSession(ws.Id, "/other");
        Assert.NotNull(other);
        store.SelectSession(active!.Id);

        var tree = store.ControlTree(
            foreground: session => session.Id == active.Id ? new[] { "ssh", "host" } : null,
            splitForeground: session => session.Id == other!.Id ? new[] { "tail", "-f", "app.log" } : null);

        Assert.Equal(new[] { "ssh", "host" }, tree.Workspaces[0].Sessions[0].Foreground);
        Assert.Null(tree.Workspaces[0].Sessions[0].SplitForeground);
        Assert.Null(tree.Workspaces[0].Sessions[1].Foreground);
        Assert.Equal(new[] { "tail", "-f", "app.log" }, tree.Workspaces[0].Sessions[1].SplitForeground);
    }

    // MARK: - Sidebar multi-selection (the transient selection model)

    [Fact]
    public void ContextTargetsUseFullSelectionOnlyWhenClickedRowIsSelected()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws.Id, "/b");
        Assert.NotNull(b);
        var c = store.AddSession(ws.Id, "/c");
        Assert.NotNull(c);
        store.SelectSession(a!.Id);
        store.SetSidebarSelection(new[] { b!.Id, a.Id });

        Assert.Equal(new[] { a.Id, b.Id }, store.SidebarSelectionIds);
        Assert.Equal(new[] { a.Id, b.Id }, store.SidebarSelectionTargets(a.Id));
        Assert.Equal(new[] { c!.Id }, store.SidebarSelectionTargets(c.Id));
        Assert.Equal(new[] { a.Id, b.Id }, store.SidebarSelectionTargets(null));
    }

    [Fact]
    public void SelectingSessionResetsTransientSidebarSelection()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws.Id, "/b");
        Assert.NotNull(b);

        store.SetSidebarSelection(new[] { a!.Id, b!.Id });
        Assert.Equal(new[] { a.Id, b.Id }, store.SidebarSelectionIds);

        store.SelectSession(b.Id);
        Assert.Equal(b.Id, store.SelectedSessionId);
        Assert.Equal(new[] { b.Id }, store.SidebarSelectionIds);
    }

    [Fact]
    public void SelectingSessionCanPreserveTransientSidebarSelection()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws.Id, "/b");
        Assert.NotNull(b);
        var c = store.AddSession(ws.Id, "/c");
        Assert.NotNull(c);

        store.SelectSession(c!.Id, new[] { a!.Id, c.Id });

        Assert.Equal(c.Id, store.SelectedSessionId);
        Assert.Equal(new[] { a.Id, c.Id }, store.SidebarSelectionIds);
        Assert.Equal(new[] { a.Id, c.Id }, store.SidebarSelectionTargets(a.Id));
        Assert.Equal(new[] { b!.Id }, store.SidebarSelectionTargets(b.Id));
    }

    [Fact]
    public void SidebarSelectionFallsBackToActiveWhenStoredSelectionIsStale()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws.Id, "/b");
        Assert.NotNull(b);
        store.SetSidebarSelection(new[] { a!.Id, b!.Id });

        var c = store.AddSession(ws.Id, "/c");
        Assert.NotNull(c);

        Assert.Equal(c!.Id, store.SelectedSessionId);
        Assert.Empty(store.SidebarSelectionIds);
        Assert.Equal(new[] { c.Id }, store.SidebarSelectionTargets(null));
        Assert.Equal(new[] { a.Id }, store.SidebarSelectionTargets(a.Id));
    }

    [Fact]
    public void SidebarTargetsDropRowsHiddenByModeOrFocus()
    {
        var store = MakeStore();
        var ws1 = store.AddWorkspace("one");
        var ws2 = store.AddWorkspace("two");
        var a = store.AddSession(ws1.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws1.Id, "/b");
        Assert.NotNull(b);
        var c = store.AddSession(ws2.Id, "/c");
        Assert.NotNull(c);
        store.SetFlag(true, a!.Id);

        store.SelectSession(a.Id);
        store.SetSidebarSelection(new[] { a.Id, b!.Id, c!.Id });
        store.SetSidebarMode(SidebarMode.Flagged);

        Assert.Equal(new[] { a.Id }, store.SidebarSelectionIds);
        Assert.Equal(new[] { a.Id }, store.SidebarSelectionTargets(a.Id));

        store.SetSidebarMode(SidebarMode.Tree);
        store.SelectSession(a.Id);
        store.SetSidebarSelection(new[] { a.Id, c.Id });
        store.SetFocusedWorkspace(ws1.Id);

        Assert.Equal(new[] { a.Id }, store.SidebarSelectionIds);
        Assert.Equal(new[] { a.Id }, store.SidebarSelectionTargets(a.Id));
    }

    // The prune-guard tests below all share one shape: hide selected rows (assert), then RE-SHOW them
    // and assert a second time. `sidebarSelectionIDs` filters on read, so the first assert passes
    // whether or not the raw list was pruned — only the second step catches a missing prune.
    [Fact]
    public void ModeChangePrunesRowsHiddenInFlaggedMode()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws.Id, "/b");
        Assert.NotNull(b);
        store.SetFlag(true, b!.Id);
        store.SetSidebarSelection(new[] { a!.Id, b.Id });

        store.SetSidebarMode(SidebarMode.Flagged);

        Assert.Equal(new[] { b.Id }, store.SidebarSelectionIds);
        store.SetSidebarMode(SidebarMode.Tree);
        Assert.True(store.SidebarSelectionIds.SequenceEqual(new[] { b.Id }),
            "rows hidden by the mode switch must not re-enter the selection when visible again");
    }

    [Fact]
    public void WorkspaceFocusPrunesRowsOutsideFocusedWorkspace()
    {
        var store = MakeStore();
        var ws1 = store.AddWorkspace("one");
        var ws2 = store.AddWorkspace("two");
        var a = store.AddSession(ws1.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws2.Id, "/b");
        Assert.NotNull(b);
        store.SetSidebarSelection(new[] { a!.Id, b!.Id });

        store.SetFocusedWorkspace(ws2.Id);

        Assert.Equal(new[] { b.Id }, store.SidebarSelectionIds);
        store.SetFocusedWorkspace(null);
        Assert.True(store.SidebarSelectionIds.SequenceEqual(new[] { b.Id }),
            "rows hidden by the focus filter must not re-enter the selection when unfocused");
    }

    [Fact]
    public void SingleFlagChangePrunesRowHiddenInFlaggedMode()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws.Id, "/b");
        Assert.NotNull(b);
        store.SetFlag(true, new[] { a!.Id, b!.Id });
        store.SetSidebarMode(SidebarMode.Flagged);
        store.SetSidebarSelection(new[] { a.Id, b.Id });

        store.SetFlag(false, a.Id);

        Assert.Equal(new[] { b.Id }, store.SidebarSelectionIds);
        store.SetFlag(true, a.Id);
        Assert.True(store.SidebarSelectionIds.SequenceEqual(new[] { b.Id }),
            "an unflagged row must not re-enter the selection when re-flagged");
    }

    [Fact]
    public void BatchFlagChangePrunesRowsHiddenInFlaggedMode()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws.Id, "/b");
        Assert.NotNull(b);
        store.SetFlag(true, new[] { a!.Id, b!.Id });
        store.SetSidebarMode(SidebarMode.Flagged);
        store.SetSidebarSelection(new[] { a.Id, b.Id });

        store.SetFlag(false, new[] { a.Id, b.Id });

        Assert.Empty(store.SidebarSelectionIds);
        store.SetFlag(true, new[] { a.Id, b.Id });
        Assert.Empty(store.SidebarSelectionIds);
    }

    [Fact]
    public void ClearFlagsPrunesRowsHiddenInFlaggedMode()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws.Id, "/b");
        Assert.NotNull(b);
        store.SetFlag(true, new[] { a!.Id, b!.Id });
        store.SetSidebarMode(SidebarMode.Flagged);
        store.SetSidebarSelection(new[] { a.Id, b.Id });

        store.ClearFlags();

        Assert.Empty(store.SidebarSelectionIds);
        store.SetFlag(true, new[] { a.Id, b.Id });
        Assert.True(store.SidebarSelectionIds.Count == 0,
            "cleared rows must not re-enter selection when visible again");
    }

    [Fact]
    public void BatchFlagSetsEverySelectedSessionInOneCommand()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws.Id, "/b");
        Assert.NotNull(b);

        store.SetFlag(true, new[] { a!.Id, b!.Id });

        Assert.True(a.Flagged);
        Assert.True(b.Flagged);
    }

    [Fact]
    public void BatchMoveAppendsCrossWorkspaceSessionsAndLeavesTargetSessionsInPlace()
    {
        var store = MakeStore();
        var ws1 = store.AddWorkspace("one");
        var ws2 = store.AddWorkspace("two");
        var a = store.AddSession(ws1.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws1.Id, "/b");
        Assert.NotNull(b);
        var c = store.AddSession(ws2.Id, "/c");
        Assert.NotNull(c);

        var affected = store.MoveSessions(new[] { c!.Id, b!.Id, a!.Id }, ws2.Id);

        Assert.Equal(2, affected);
        Assert.Empty(store.Workspaces[0].Sessions);
        Assert.Equal(new[] { c.Id, a.Id, b.Id }, store.Workspaces[1].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void OneElementBatchMoveWithinWorkspaceMatchesSingularAppend()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws.Id, "/b");
        Assert.NotNull(b);
        var c = store.AddSession(ws.Id, "/c");
        Assert.NotNull(c);

        var affected = store.MoveSessions(new[] { a!.Id }, ws.Id);

        Assert.Equal(1, affected);
        Assert.Equal(new[] { b!.Id, c!.Id, a.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void MultiElementBatchMoveAlreadyInTargetReportsZeroAndKeepsOrder()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws.Id, "/b");
        Assert.NotNull(b);

        var affected = store.MoveSessions(new[] { a!.Id, b!.Id }, ws.Id);

        Assert.Equal(0, affected);
        Assert.Equal(new[] { a.Id, b.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void BatchMoveInsertsCrossWorkspaceSessionsAtDropIndex()
    {
        var store = MakeStore();
        var ws1 = store.AddWorkspace("one");
        var ws2 = store.AddWorkspace("two");
        var a = store.AddSession(ws1.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws1.Id, "/b");
        Assert.NotNull(b);
        var c = store.AddSession(ws2.Id, "/c");
        Assert.NotNull(c);
        var d = store.AddSession(ws2.Id, "/d");
        Assert.NotNull(d);
        var e = store.AddSession(ws2.Id, "/e");
        Assert.NotNull(e);

        store.MoveSessions(new[] { a!.Id, b!.Id }, ws2.Id, 1);

        Assert.Empty(store.Workspaces[0].Sessions);
        Assert.Equal(new[] { c!.Id, a.Id, b.Id, d!.Id, e!.Id }, store.Workspaces[1].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void BatchMoveReordersSameWorkspaceAtDropIndex()
    {
        var store = MakeStore();
        var ws = store.AddWorkspace("work");
        var a = store.AddSession(ws.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws.Id, "/b");
        Assert.NotNull(b);
        var c = store.AddSession(ws.Id, "/c");
        Assert.NotNull(c);
        var d = store.AddSession(ws.Id, "/d");
        Assert.NotNull(d);
        var e = store.AddSession(ws.Id, "/e");
        Assert.NotNull(e);

        store.MoveSessions(new[] { a!.Id, b!.Id }, ws.Id, 2);

        Assert.Equal(new[] { c!.Id, d!.Id, a.Id, b.Id, e!.Id }, store.Workspaces[0].Sessions.Select(s => s.Id));
    }

    [Fact]
    public void BatchMoveMixedSelectionAdjustsTargetInsertionAfterRemoval()
    {
        var store = MakeStore();
        var ws1 = store.AddWorkspace("one");
        var ws2 = store.AddWorkspace("two");
        var a = store.AddSession(ws1.Id, "/a");
        Assert.NotNull(a);
        var b = store.AddSession(ws2.Id, "/b");
        Assert.NotNull(b);
        var c = store.AddSession(ws2.Id, "/c");
        Assert.NotNull(c);
        var d = store.AddSession(ws2.Id, "/d");
        Assert.NotNull(d);

        store.MoveSessions(new[] { a!.Id, b!.Id }, ws2.Id, 1);

        Assert.Empty(store.Workspaces[0].Sessions);
        Assert.Equal(new[] { c!.Id, a.Id, b.Id, d!.Id }, store.Workspaces[1].Sessions.Select(s => s.Id));
    }
}
