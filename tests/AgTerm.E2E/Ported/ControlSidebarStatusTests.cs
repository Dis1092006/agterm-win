using System.Text.Json.Nodes;
using AgTerm.ControlServer;
using AgTerm.Core.Model;
using Xunit;

namespace AgTerm.E2E.Ported;

/// <summary>
/// Port of agtermUITests/ControlSidebarStatusUITests.swift: control-channel e2e for the sidebar
/// visibility/mode/expand-collapse, workspace focus, session flag, agent-status glyph, and
/// notification-badge behaviors, driven against the headless host. Sidebar/status GLYPH assertions
/// (AX "session-row"/"agent-status"/"notify-badge" elements upstream) map to the tree read-back
/// fields the rows render from (sidebarVisible/sidebarMode/flagged/focused/collapsed/status*/
/// unseen); the host is synchronous, so the upstream waitForExistence/poll loops collapse to a
/// single fresh-tree read.
/// </summary>
public class ControlSidebarStatusTests : ControlApiTestCase
{
    // an OSC 9 desktop notification from an UNFOCUSED pane badges its sidebar row, and selecting the
    // session clears it. Fire into the seeded session (realized at launch) after a new session takes
    // focus, so suppression doesn't drop it and no --select (which would re-focus it) is needed.
    [Fact]
    public void UnfocusedNotificationBadgesRowAndClearsOnSelect()
    {
        var tree = SendCommand("""{"cmd":"tree"}""");
        var seeded = tree["result"]!["tree"]!["workspaces"]![0]!["sessions"]![0]!["id"]!
            .GetValue<string>();

        // upstream's seeded session is realized at launch; headless surfaces attach externally.
        var surface = new FakeSurface();
        Host.Store.SessionWithId(Guid.Parse(seeded))!.Surface = surface;

        // a second session takes focus, leaving the seeded one realized but unfocused.
        Assert.True(SendCommand("""{"cmd":"session.new"}""")["ok"]!.GetValue<bool>());
        Assert.Equal(2, AllSessionNodes().Count); // pollSessionCount: synchronous host, one read

        // emit OSC 9 from the unfocused seeded session (printf interprets the octal escapes).
        // Upstream builds this line via typeRequest(text:target:select:) → JSONSerialization.
        var typed = SendCommand(
            $$$"""{"cmd":"session.type","target":"{{{seeded}}}","args":{"text":"printf '\\033]9;agterm test\\007'\n","select":false}}""");
        Assert.True(typed["ok"]!.GetValue<bool>(),
            $"typing into the realized seeded session should succeed: {typed.ToJsonString()}");
        Assert.Contains("printf '\\033]9;agterm test\\007'\n", surface.Injected);

        // PORT: no terminal engine parses OSC 9 headless — stand in for the notification hop the
        // way the app's OSC 9 handler lands on the model: bump the unfocused session's unseen badge.
        Host.Store.SessionWithId(Guid.Parse(seeded))!.UnseenCount += 1;
        // "notify-badge should appear on the unfocused session's row" → the tree's unseen count.
        Assert.Equal(1, UnseenCount(seeded));

        Assert.True(SendCommand($$$"""{"cmd":"session.select","target":"{{{seeded}}}"}""")["ok"]!
            .GetValue<bool>());
        // "selecting the session should clear its badge" → unseen is omitted once cleared.
        Assert.Null(UnseenCount(seeded));
    }

    // the `sidebar` control command shows/hides the custom sidebar (the custom split has no system
    // toggle). hiding removes the session rows from the AX tree; showing restores them. mode is
    // show|hide|toggle on the frontmost window, and an unknown mode is an error.
    [Fact]
    public void SidebarShowHideToggle()
    {
        // "sidebar should start visible" → the session rows' presence maps to tree.sidebarVisible.
        Assert.True(Tree()["tree"]!["sidebarVisible"]!.GetValue<bool>());

        Assert.True(SendCommand("""{"cmd":"sidebar","args":{"mode":"hide"}}""")["ok"]!.GetValue<bool>(),
            "sidebar hide should succeed");
        // "hiding the sidebar should remove the session rows" → sidebarVisible reads false.
        Assert.False(Tree()["tree"]!["sidebarVisible"]!.GetValue<bool>());

        Assert.True(SendCommand("""{"cmd":"sidebar","args":{"mode":"show"}}""")["ok"]!.GetValue<bool>(),
            "sidebar show should succeed");
        // "showing the sidebar should restore the session rows".
        Assert.True(Tree()["tree"]!["sidebarVisible"]!.GetValue<bool>());

        var bad = SendCommand("""{"cmd":"sidebar","args":{"mode":"sideways"}}""");
        Assert.False(bad["ok"]!.GetValue<bool>(), "an invalid sidebar mode should error");
    }

    // session.flag flags a session for the flagged working-set view; sidebar.mode flagged switches the
    // sidebar to the flat list of just the flagged sessions (each labeled "session : workspace"), so an
    // unflagged session's row is absent in flagged mode and returns when switched back to tree.
    [Fact]
    public void SessionFlagAndSidebarModeFlagged()
    {
        // name the seeded session and add a second one, so the two are distinguishable by name.
        var tree = SendCommand("""{"cmd":"tree"}""");
        var seededID = tree["result"]!["tree"]!["workspaces"]![0]!["sessions"]![0]!["id"]!
            .GetValue<string>();

        Assert.True(SendCommand(
            $$$"""{"cmd":"session.rename","target":"{{{seededID}}}","args":{"name":"flagme"}}""")["ok"]!
            .GetValue<bool>(), "renaming the seeded session should succeed");
        var created = SendCommand("""{"cmd":"session.new"}""");
        var newID = created["result"]!["id"]!.GetValue<string>();
        Assert.True(SendCommand(
            $$$"""{"cmd":"session.rename","target":"{{{newID}}}","args":{"name":"keepme"}}""")["ok"]!
            .GetValue<bool>(), "renaming the new session should succeed");

        // both rows present in tree mode.
        Assert.Equal(2, AllSessionNodes().Count);

        // flag the seeded session.
        var flag = SendCommand(
            $$$"""{"cmd":"session.flag","target":"{{{seededID}}}","args":{"mode":"on"}}""");
        Assert.True(flag["ok"]!.GetValue<bool>(), $"session.flag on should succeed: {flag.ToJsonString()}");

        // switch to the flat flagged view: exactly one row, the flagged "flagme"; the unflagged
        // "keepme" row is absent. The tree read-back itself is unfiltered, so the flagged-mode row
        // set maps to sidebarMode == "flagged" + the sessions' flagged flags.
        var mode = SendCommand("""{"cmd":"sidebar.mode","args":{"mode":"flagged"}}""");
        Assert.True(mode["ok"]!.GetValue<bool>(), $"sidebar.mode flagged should succeed: {mode.ToJsonString()}");
        Assert.Equal("flagged", Tree()["tree"]!["sidebarMode"]!.GetValue<string>());
        Assert.Equal(1, FlaggedSessionCount()); // flagged mode shows only the one flagged row
        Assert.True(SessionFlagged("flagme"), "the flagged session's row should be present");
        Assert.False(SessionFlagged("keepme"), "the unflagged session's row should be absent");

        // toggling back to the tree restores both rows.
        Assert.True(SendCommand("""{"cmd":"sidebar.mode","args":{"mode":"toggle"}}""")["ok"]!
            .GetValue<bool>(), "sidebar.mode toggle should succeed");
        Assert.Equal("tree", Tree()["tree"]!["sidebarMode"]!.GetValue<string>());
        Assert.Equal(2, AllSessionNodes().Count); // toggling back to tree restores the full tree

        // session.flag clear unflags everything: flag BOTH, view the flat list (two rows), then
        // clear → the flagged view empties (zero rows).
        Assert.True(SendCommand(
            $$$"""{"cmd":"session.flag","target":"{{{newID}}}","args":{"mode":"on"}}""")["ok"]!
            .GetValue<bool>(), "flagging the second session should succeed");
        Assert.True(SendCommand("""{"cmd":"sidebar.mode","args":{"mode":"flagged"}}""")["ok"]!
            .GetValue<bool>(), "switching to flagged should succeed");
        Assert.Equal(2, FlaggedSessionCount()); // both flagged rows present
        var cleared = SendCommand("""{"cmd":"session.flag","args":{"mode":"clear"}}""");
        Assert.True(cleared["ok"]!.GetValue<bool>(),
            $"session.flag clear should succeed: {cleared.ToJsonString()}");
        Assert.Equal(0, FlaggedSessionCount()); // clearing all flags empties the flagged view
        // back to the tree so the invalid-mode check below runs against the full tree.
        Assert.True(SendCommand("""{"cmd":"sidebar.mode","args":{"mode":"tree"}}""")["ok"]!
            .GetValue<bool>(), "switching back to tree should succeed");
        Assert.Equal(2, AllSessionNodes().Count); // the sessions themselves are not closed by clear

        // an invalid mode errors rather than silently no-opping.
        var bad = SendCommand("""{"cmd":"sidebar.mode","args":{"mode":"bogus"}}""");
        Assert.False(bad["ok"]!.GetValue<bool>(), $"an invalid sidebar mode should error: {bad.ToJsonString()}");
        Assert.Contains("invalid sidebar mode", bad["error"]!.GetValue<string>());
    }

    // workspace.focus collapses the sidebar tree to a single workspace's subtree — the other workspaces'
    // session rows leave the AX tree; unfocusing restores them. Orthogonal to the flagged view.
    [Fact]
    public void WorkspaceFocusHidesOtherWorkspaces()
    {
        // capture the seeded workspace + session, name the session so it's findable by value.
        var tree = SendCommand("""{"cmd":"tree"}""");
        var ws = tree["result"]!["tree"]!["workspaces"]![0]!;
        var firstWsID = ws["id"]!.GetValue<string>();
        var seededID = ws["sessions"]![0]!["id"]!.GetValue<string>();
        Assert.True(SendCommand(
            $$$"""{"cmd":"session.rename","target":"{{{seededID}}}","args":{"name":"stay"}}""")["ok"]!
            .GetValue<bool>(), "renaming the seeded session should succeed");

        // add a second workspace with its own session.
        var newWs = SendCommand("""{"cmd":"workspace.new","args":{"name":"second"}}""");
        var secondWsID = newWs["result"]!["id"]!.GetValue<string>();
        var created = SendCommand(
            $$$"""{"cmd":"session.new","args":{"workspace":"{{{secondWsID}}}"}}""");
        var newSessID = created["result"]!["id"]!.GetValue<string>();
        Assert.True(SendCommand(
            $$$"""{"cmd":"session.rename","target":"{{{newSessID}}}","args":{"name":"hidden"}}""")["ok"]!
            .GetValue<bool>(), "renaming the new session should succeed");

        // both rows present in the unfocused tree.
        Assert.Equal(2, AllSessionNodes().Count);

        // select the first workspace's session (so the active session is inside the workspace we
        // focus), then focus the FIRST workspace: the second workspace's rows leave the sidebar —
        // the row filter derives from the tree's workspace `focused` read-back.
        Assert.True(SendCommand($$$"""{"cmd":"session.select","target":"{{{seededID}}}"}""")["ok"]!
            .GetValue<bool>(), "selecting the seeded session should succeed");
        var focus = SendCommand(
            $$$"""{"cmd":"workspace.focus","target":"{{{firstWsID}}}","args":{"mode":"on"}}""");
        Assert.True(focus["ok"]!.GetValue<bool>(), $"workspace.focus on should succeed: {focus.ToJsonString()}");
        Assert.True(WorkspaceFocused(firstWsID), "the focused workspace's session should remain");
        Assert.False(WorkspaceFocused(secondWsID), "the other workspace's session should be hidden");

        // workspace.focus off on a NON-focused workspace is a no-op — it unfocuses only the currently
        // focused one, so the focus on the first workspace must survive (the other's rows stay hidden).
        Assert.True(SendCommand(
            $$$"""{"cmd":"workspace.focus","target":"{{{secondWsID}}}","args":{"mode":"off"}}""")["ok"]!
            .GetValue<bool>(), "workspace.focus off on a non-focused workspace should succeed (no-op)");
        Assert.True(WorkspaceFocused(firstWsID), "the focus on the first workspace should be unchanged");
        Assert.False(WorkspaceFocused(secondWsID), "the other workspace's session should still be hidden");

        // unfocus restores the full tree (no workspace reports focused).
        Assert.True(SendCommand(
            $$$"""{"cmd":"workspace.focus","target":"{{{firstWsID}}}","args":{"mode":"off"}}""")["ok"]!
            .GetValue<bool>(), "workspace.focus off should succeed");
        Assert.False(WorkspaceFocused(firstWsID), "unfocusing should restore the full tree");
        Assert.False(WorkspaceFocused(secondWsID));

        // an invalid mode errors rather than silently no-opping.
        var bad = SendCommand(
            $$$"""{"cmd":"workspace.focus","target":"{{{firstWsID}}}","args":{"mode":"bogus"}}""");
        Assert.False(bad["ok"]!.GetValue<bool>(), $"an invalid focus mode should error: {bad.ToJsonString()}");
        Assert.Contains("invalid focus mode", bad["error"]!.GetValue<string>());
    }

    // --no-select --create-workspace preserves the workspace-focus filter. A plain --create-workspace
    // clears focusedWorkspaceID (addWorkspace's auto-reveal); --no-select threads clearFocus:false so a
    // background create leaves the focused workspace (and the current session selection) untouched.
    [Fact]
    public void SessionNewNoSelectCreateWorkspacePreservesFocus()
    {
        var tree = SendCommand("""{"cmd":"tree"}""");
        var firstWsID = tree["result"]!["tree"]!["workspaces"]![0]!["id"]!.GetValue<string>();

        // focus the seeded workspace.
        Assert.True(SendCommand(
            $$$"""{"cmd":"workspace.focus","target":"{{{firstWsID}}}","args":{"mode":"on"}}""")["ok"]!
            .GetValue<bool>(), "workspace.focus on should succeed");
        Assert.True(WorkspaceFocused(firstWsID), "the seeded workspace should be focused");

        // background-create a session in a brand-new workspace: the focus filter must survive.
        var created = SendCommand(
            """{"cmd":"session.new","args":{"workspaceName":"bg","createWorkspace":true,"noSelect":true}}""");
        Assert.True(created["ok"]!.GetValue<bool>(),
            $"background create-workspace should succeed: {created.ToJsonString()}");
        Assert.True(WorkspaceFocused(firstWsID),
            "--no-select --create-workspace must preserve the focused workspace (not clear it via auto-reveal)");
    }

    // sidebar.collapse collapses every workspace except the current one — the others' session rows
    // leave the AX tree while the active workspace's stay; sidebar.expand re-expands every workspace and
    // restores them.
    [Fact]
    public void SidebarExpandCollapse()
    {
        // capture the seeded workspace + session, name the session so it's findable by value.
        var tree = SendCommand("""{"cmd":"tree"}""");
        var ws = tree["result"]!["tree"]!["workspaces"]![0]!;
        var firstWsID = ws["id"]!.GetValue<string>();
        var seededID = ws["sessions"]![0]!["id"]!.GetValue<string>();
        Assert.True(SendCommand(
            $$$"""{"cmd":"session.rename","target":"{{{seededID}}}","args":{"name":"stay"}}""")["ok"]!
            .GetValue<bool>(), "renaming the seeded session should succeed");

        // add a second workspace with its own session in a different workspace.
        var newWs = SendCommand("""{"cmd":"workspace.new","args":{"name":"second"}}""");
        var secondWsID = newWs["result"]!["id"]!.GetValue<string>();
        var created = SendCommand(
            $$$"""{"cmd":"session.new","args":{"workspace":"{{{secondWsID}}}"}}""");
        var newSessID = created["result"]!["id"]!.GetValue<string>();
        Assert.True(SendCommand(
            $$$"""{"cmd":"session.rename","target":"{{{newSessID}}}","args":{"name":"hidden"}}""")["ok"]!
            .GetValue<bool>(), "renaming the new session should succeed");

        // both rows present with both workspaces expanded (collapsed omitted on both).
        Assert.Null(WorkspaceNode(firstWsID)["collapsed"]);
        Assert.Null(WorkspaceNode(secondWsID)["collapsed"]);

        // select the seeded session so the ACTIVE workspace is the first one, then collapse: the second
        // workspace folds away (its "hidden" row leaves the sidebar) while the active workspace stays open.
        Assert.True(SendCommand($$$"""{"cmd":"session.select","target":"{{{seededID}}}"}""")["ok"]!
            .GetValue<bool>(), "selecting the seeded session should succeed");
        var collapse = SendCommand("""{"cmd":"sidebar.collapse"}""");
        Assert.True(collapse["ok"]!.GetValue<bool>(),
            $"sidebar.collapse should succeed: {collapse.ToJsonString()}");
        Assert.Null(WorkspaceNode(firstWsID)["collapsed"]); // the active workspace's session remains
        Assert.Equal(true, WorkspaceNode(secondWsID)["collapsed"]?.GetValue<bool>()); // "hidden" folds away

        // expand re-opens every workspace and restores both rows.
        var expand = SendCommand("""{"cmd":"sidebar.expand"}""");
        Assert.True(expand["ok"]!.GetValue<bool>(), $"sidebar.expand should succeed: {expand.ToJsonString()}");
        Assert.Null(WorkspaceNode(firstWsID)["collapsed"]);
        Assert.Null(WorkspaceNode(secondWsID)["collapsed"]); // the collapsed workspace's session returns
    }

    // upstream 0106275a: sidebar.collapse keeps the current workspace, and a workspace created in the
    // foreground is the current one until the selection changes.
    [Fact]
    public void SidebarCollapseKeepsAForegroundCreatedWorkspace()
    {
        var tree = SendCommand("""{"cmd":"tree"}""");
        var firstWsID = tree["result"]!["tree"]!["workspaces"]![0]!["id"]!.GetValue<string>();
        var fresh = SendCommand("""{"cmd":"workspace.new","args":{"name":"fresh"}}""");
        var freshWsID = fresh["result"]!["id"]!.GetValue<string>();

        var collapse = SendCommand("""{"cmd":"sidebar.collapse"}""");

        Assert.Equal("""{"ok":true}""", collapse.ToJsonString());
        Assert.Equal(true, WorkspaceNode(firstWsID)["collapsed"]?.GetValue<bool>());
        Assert.Null(WorkspaceNode(freshWsID)["collapsed"]);
    }

    // session.status sets a session's agent indicator: a valid state returns ok + the resolved id, an
    // unknown state returns the literal `invalid status` error, and an unknown target is not-found.
    [Fact]
    public void SessionStatusSetsIndicator()
    {
        var tree = SendCommand("""{"cmd":"tree"}""");
        var seeded = tree["result"]!["tree"]!["workspaces"]![0]!["sessions"]![0]!["id"]!
            .GetValue<string>();

        // a valid state with a blink flag succeeds and echoes the resolved id.
        var ok = SendCommand(
            $$$"""{"cmd":"session.status","target":"{{{seeded}}}","args":{"status":"active","blink":true}}""");
        Assert.True(ok["ok"]!.GetValue<bool>(), $"session.status active should succeed: {ok.ToJsonString()}");
        Assert.Equal(seeded.ToLowerInvariant(), ok["result"]!["id"]!.GetValue<string>().ToLowerInvariant());

        // an unknown state returns the literal guard string the arm emits.
        var bad = SendCommand(
            $$$"""{"cmd":"session.status","target":"{{{seeded}}}","args":{"status":"bogus"}}""");
        Assert.False(bad["ok"]!.GetValue<bool>(), $"an unknown status should fail: {bad.ToJsonString()}");
        Assert.Equal("invalid status", bad["error"]!.GetValue<string>());

        // an unknown target is the structured not-found error (mirrors testUnknownTargetErrors).
        var unknown = SendCommand(
            """{"cmd":"session.status","target":"deadbeef","args":{"status":"active"}}""");
        Assert.False(unknown["ok"]!.GetValue<bool>(), $"an unknown target should fail: {unknown.ToJsonString()}");
        Assert.StartsWith("no such session", unknown["error"]!.GetValue<string>());
    }

    [Fact]
    public void SessionStatusSoundValidatesName()
    {
        var seeded = ActiveSessionId();

        // reads the seeded session's current status from a fresh tree (null when idle).
        string? CurrentStatus() => SessionNode(seeded)["status"]?.GetValue<string>();

        // the default-beep keyword resolves and the command succeeds.
        var ok = SendCommand(
            $$$"""{"cmd":"session.status","target":"{{{seeded}}}","args":{"status":"active","sound":"default"}}""");
        Assert.True(ok["ok"]!.GetValue<bool>(),
            $"session.status --sound default should succeed: {ok.ToJsonString()}");

        // establish a known baseline, then set a DIFFERENT status with an unknown sound.
        Assert.True(SendCommand(
            $$$"""{"cmd":"session.status","target":"{{{seeded}}}","args":{"status":"completed"}}""")["ok"]!
            .GetValue<bool>());
        Assert.Equal("completed", CurrentStatus());

        var unknownSound = SendCommand(
            $$$"""{"cmd":"session.status","target":"{{{seeded}}}","args":{"status":"active","sound":"NoSuchSoundXYZ"}}""");
        Assert.False(unknownSound["ok"]!.GetValue<bool>());
        Assert.StartsWith("unknown sound: NoSuchSoundXYZ (use 'default', 'beep', or one of: ",
            unknownSound["error"]!.GetValue<string>());
        Assert.Equal("completed", CurrentStatus());
    }

    // session.status --color sets a per-call glyph tint. Upstream cannot observe the tint in the AX
    // tree; headless CAN — the tree read-back carries statusColor — so this asserts it directly: a
    // valid #rrggbb applies the status, and a malformed color is rejected before the mutation and
    // leaves the status unchanged.
    [Fact]
    public void SessionStatusColorValidatesHex()
    {
        var seeded = ActiveSessionId();

        string? CurrentStatus() => SessionNode(seeded)["status"]?.GetValue<string>();

        // a valid #rrggbb applies the status and shows the glyph (status + statusColor read back).
        var ok = SendCommand(
            $$$"""{"cmd":"session.status","target":"{{{seeded}}}","args":{"status":"blocked","color":"#ff0000"}}""");
        Assert.True(ok["ok"]!.GetValue<bool>(),
            $"session.status --color #ff0000 should succeed: {ok.ToJsonString()}");
        Assert.Equal("blocked", CurrentStatus());
        Assert.Equal("#ff0000", SessionNode(seeded)["statusColor"]?.GetValue<string>());

        // a malformed color is rejected before the mutation.
        var bad = SendCommand(
            $$$"""{"cmd":"session.status","target":"{{{seeded}}}","args":{"status":"active","color":"nope"}}""");
        Assert.False(bad["ok"]!.GetValue<bool>(), $"a malformed color should fail: {bad.ToJsonString()}");
        Assert.Equal("invalid color (expected #rrggbb)", bad["error"]!.GetValue<string>());

        // the rejected call must NOT have changed the status — validation happens before the mutation.
        Assert.Equal("blocked", CurrentStatus());
    }

    // session.status --shape picks a per-call glyph silhouette. The drawn shape isn't observable
    // upstream (only the state name is); headless drives the same command path: a valid shape applies
    // the status and reads back on the tree, an unknown shape is rejected before the mutation, and the
    // next status without --shape discards the override.
    [Fact]
    public void SessionStatusShapeValidatesAndReadsBack()
    {
        var seeded = ActiveSessionId();

        // a valid shape applies the status and shows the glyph.
        var ok = SendCommand(
            $$$"""{"cmd":"session.status","target":"{{{seeded}}}","args":{"status":"blocked","shape":"triangle"}}""");
        Assert.True(ok["ok"]!.GetValue<bool>(),
            $"session.status --shape triangle should succeed: {ok.ToJsonString()}");
        // "the glyph keeps reporting the state name" — the AX value maps to the status read-back.
        Assert.Equal("blocked", SessionNode(seeded)["status"]?.GetValue<string>());

        // the tree read-back carries the per-call shape alongside the status.
        var node = SessionNode(seeded);
        Assert.Equal("blocked", node["status"]?.GetValue<string>());
        Assert.Equal("triangle", node["statusShape"]?.GetValue<string>());

        // an unknown shape is rejected with the allCases-derived message, before any mutation.
        var bad = SendCommand(
            $$$"""{"cmd":"session.status","target":"{{{seeded}}}","args":{"status":"active","shape":"hexagon"}}""");
        Assert.False(bad["ok"]!.GetValue<bool>(), $"an unknown shape should fail: {bad.ToJsonString()}");
        Assert.Equal("invalid shape: hexagon (circle|square|triangle|diamond|capsule|star)",
            bad["error"]!.GetValue<string>());
        node = SessionNode(seeded);
        Assert.Equal("blocked", node["status"]?.GetValue<string>());
        Assert.Equal("triangle", node["statusShape"]?.GetValue<string>());

        // the next status without --shape discards the override — the glyph falls back to the
        // Settings shape (the built-in circle) and the read-back drops the field.
        var cleared = SendCommand(
            $$$"""{"cmd":"session.status","target":"{{{seeded}}}","args":{"status":"active"}}""");
        Assert.True(cleared["ok"]!.GetValue<bool>(),
            $"session.status without --shape should succeed: {cleared.ToJsonString()}");
        node = SessionNode(seeded);
        Assert.Equal("active", node["status"]?.GetValue<string>());
        Assert.Null(node["statusShape"]);
    }

    // the agent-status icon shows on every non-idle session, the selected one INCLUDED — there is no
    // visibility gate. Set active on a non-selected session → the icon appears; select that session → it
    // STAYS (active is keep-state); set completed --auto-reset on a non-selected session → it shows, then
    // VISITING (selecting) it clears the auto-reset flash. The icon's existence maps to the session's
    // `status` read-back being present.
    [Fact]
    public void AgentStatusIconShowsRegardlessOfSelectionAndAutoResetClears()
    {
        var tree = SendCommand("""{"cmd":"tree"}""");
        var seeded = tree["result"]!["tree"]!["workspaces"]![0]!["sessions"]![0]!["id"]!
            .GetValue<string>();

        // a second session takes focus, leaving the seeded one realized but non-selected.
        var created = SendCommand("""{"cmd":"session.new"}""");
        var secondID = created["result"]!["id"]!.GetValue<string>();
        Assert.Equal(2, AllSessionNodes().Count); // the new session should land

        // negative baseline: no status set yet, so no agent-status icon exists on any row.
        Assert.All(AllSessionNodes(), s => Assert.Null(s["status"]));

        // set active on the non-selected seeded session: the icon appears.
        var status = SendCommand(
            $$$"""{"cmd":"session.status","target":"{{{seeded}}}","args":{"status":"active"}}""");
        Assert.True(status["ok"]!.GetValue<bool>(),
            $"session.status active should succeed: {status.ToJsonString()}");
        Assert.Equal("active", SessionNode(seeded)["status"]?.GetValue<string>());

        // selecting that session KEEPS the icon — active is keep-state and there is no visibility gate.
        Assert.True(SendCommand($$$"""{"cmd":"session.select","target":"{{{seeded}}}"}""")["ok"]!
            .GetValue<bool>());
        Assert.Equal("active", SessionNode(seeded)["status"]?.GetValue<string>());

        // completed --auto-reset on the now non-selected session shows, then VISITING it clears it.
        Assert.True(SendCommand($$$"""{"cmd":"session.select","target":"{{{secondID}}}"}""")["ok"]!
            .GetValue<bool>());
        Assert.True(SendCommand(
            $$$"""{"cmd":"session.status","target":"{{{seeded}}}","args":{"status":"completed","autoReset":true}}""")["ok"]!
            .GetValue<bool>());
        Assert.Equal("completed", SessionNode(seeded)["status"]?.GetValue<string>());
        Assert.True(SendCommand($$$"""{"cmd":"session.select","target":"{{{seeded}}}"}""")["ok"]!
            .GetValue<bool>());
        Assert.Null(SessionNode(seeded)["status"]); // visiting a completed --auto-reset session clears it
    }

    // typing into a session flagged for your attention (`blocked` or `completed`) clears the glyph — the
    // input-driven clear. Upstream MUST drive the real keyboard (wired off GhosttySurfaceView.keyDown);
    // headless drives the same store seam that keyDown handler calls: AgentIndicator.ClearedBy decides,
    // SetAgentIndicator(idle) clears.
    [Fact]
    public void TypingClearsBlockedOrCompletedStatus()
    {
        var tree = SendCommand("""{"cmd":"tree"}""");
        var seeded = tree["result"]!["tree"]!["workspaces"]![0]!["sessions"]![0]!["id"]!
            .GetValue<string>();

        // both attention states clear on a keystroke; active would NOT (agent still working), idle
        // has no glyph.
        foreach (var state in new[] { "blocked", "completed" })
        {
            var set = SendCommand(
                $$$"""{"cmd":"session.status","target":"{{{seeded}}}","args":{"status":"{{{state}}}"}}""");
            Assert.True(set["ok"]!.GetValue<bool>(),
                $"session.status {state} should succeed: {set.ToJsonString()}");
            Assert.Equal(state, SessionNode(seeded)["status"]?.GetValue<string>()); // glyph shows

            // an ordinary (non-interrupt) keystroke from the owning pane clears an attention state —
            // the store-seam equivalent of app.typeKey(.escape) landing in the focused terminal.
            var session = Host.Store.SessionWithId(Guid.Parse(seeded))!;
            Assert.True(session.AgentIndicator.ClearedBy(StatusPane.Left, isInterrupt: false),
                $"typing into a {state} session should clear its glyph");
            Host.Store.SetAgentIndicator(new AgentIndicator(), session.Id);
            Assert.Null(SessionNode(seeded)["status"]); // the glyph is gone
        }
    }

    // an `active` glyph clears ONLY on an interrupt keystroke. Ctrl-C is an interrupt just like Esc —
    // Claude Code and most TUIs treat it as Esc for dismissing a pending prompt — so it must drop the
    // stale glyph where ordinary typing does not. Headless drives the same seam the app-side
    // `isInterruptKeystroke` wiring feeds: ClearedBy(pane, isInterrupt).
    [Fact]
    public void CtrlCClearsActiveStatus()
    {
        var tree = SendCommand("""{"cmd":"tree"}""");
        var seeded = tree["result"]!["tree"]!["workspaces"]![0]!["sessions"]![0]!["id"]!
            .GetValue<string>();

        var set = SendCommand(
            $$$"""{"cmd":"session.status","target":"{{{seeded}}}","args":{"status":"active"}}""");
        Assert.True(set["ok"]!.GetValue<bool>(), $"session.status active should succeed: {set.ToJsonString()}");
        Assert.Equal("active", SessionNode(seeded)["status"]?.GetValue<string>()); // glyph shows

        var session = Host.Store.SessionWithId(Guid.Parse(seeded))!;
        // ordinary typing must NOT clear active (the agent is still working)…
        Assert.False(session.AgentIndicator.ClearedBy(StatusPane.Left, isInterrupt: false));
        // …but Ctrl-C (an interrupt) must.
        Assert.True(session.AgentIndicator.ClearedBy(StatusPane.Left, isInterrupt: true),
            "Ctrl-C into an active session should clear its glyph");
        Host.Store.SetAgentIndicator(new AgentIndicator(), session.Id);
        Assert.Null(SessionNode(seeded)["status"]);
    }

    // session.seen clears a session's unseen badge WITHOUT changing the selection/focus — the focus-free
    // counterpart to notify. Fire notify on a non-selected session so the badge + tree `unseen` read-back
    // show, then session.seen clears both while the selection stays on the other session.
    [Fact]
    public void SessionSeenClearsBadgeWithoutFocus()
    {
        var tree = SendCommand("""{"cmd":"tree"}""");
        var seeded = tree["result"]!["tree"]!["workspaces"]![0]!["sessions"]![0]!["id"]!
            .GetValue<string>();

        // a second session takes focus, leaving the seeded one non-selected so its badge persists.
        var created = SendCommand("""{"cmd":"session.new"}""");
        Assert.True(created["ok"]!.GetValue<bool>());
        var newSession = created["result"]!["id"]!.GetValue<string>();
        Assert.Equal(2, AllSessionNodes().Count); // the new session should land

        // notify (no focus-suppression) bumps the non-selected session's unseen count → the badge shows.
        var notified = SendCommand(
            $$$"""{"cmd":"notify","target":"{{{seeded}}}","args":{"body":"hi"}}""");
        Assert.True(notified["ok"]!.GetValue<bool>(), $"notify should succeed: {notified.ToJsonString()}");

        // the tree read-back surfaces the unseen count and the new session is the active one.
        Assert.Equal(1, UnseenCount(seeded));
        Assert.Equal(newSession, ActiveNodeId());

        // session.seen clears the badge; it targets the NON-selected session and returns its id.
        var seen = SendCommand($$$"""{"cmd":"session.seen","target":"{{{seeded}}}"}""");
        Assert.True(seen["ok"]!.GetValue<bool>(), $"session.seen should succeed: {seen.ToJsonString()}");
        Assert.Equal(seeded, seen["result"]!["id"]!.GetValue<string>());
        Assert.Null(UnseenCount(seeded)); // session.seen clears the count pill

        // the clear is focus-free: the selection is unchanged and the tree no longer reports unseen.
        Assert.Equal(newSession, ActiveNodeId());

        // idempotent: a second seen on an already-clear session is ok AND a genuine no-op.
        var again = SendCommand($$$"""{"cmd":"session.seen","target":"{{{seeded}}}"}""");
        Assert.True(again["ok"]!.GetValue<bool>(), "session.seen is idempotent when the badge is already zero");
        Assert.Null(UnseenCount(seeded));
        Assert.Equal(newSession, ActiveNodeId());

        // an unknown target errors through the shared resolver, like the other session.* commands.
        var bad = SendCommand("""{"cmd":"session.seen","target":"deadbeef"}""");
        Assert.False(bad["ok"]!.GetValue<bool>(), "session.seen with an unknown target should fail");
        Assert.StartsWith("no such session", bad["error"]!.GetValue<string>());
    }

    // notify posts a banner for the active session; a missing body errors.
    [Fact]
    public void NotifySend()
    {
        var ok = SendCommand("""{"cmd":"notify","target":"active","args":{"body":"hello","title":"Test"}}""");
        Assert.True(ok["ok"]!.GetValue<bool>(), $"notify with a body should succeed: {ok.ToJsonString()}");

        var noBody = SendCommand("""{"cmd":"notify","target":"active"}""");
        Assert.False(noBody["ok"]!.GetValue<bool>(),
            $"notify without a body should fail: {noBody.ToJsonString()}");
        Assert.Contains("requires a body", noBody["error"]!.GetValue<string>());
    }

    [Fact]
    public void WorkspaceCollapseAndExpand()
    {
        // name the seeded session, then add a second workspace with its own named session.
        var seededID = ActiveSessionId();
        Assert.True(SendCommand(
            $$$"""{"cmd":"session.rename","target":"{{{seededID}}}","args":{"name":"stay"}}""")["ok"]!
            .GetValue<bool>(), "renaming the seeded session should succeed");
        var newWs = SendCommand("""{"cmd":"workspace.new","args":{"name":"second"}}""");
        var secondWsID = newWs["result"]!["id"]!.GetValue<string>();
        var created = SendCommand(
            $$$"""{"cmd":"session.new","args":{"workspace":"{{{secondWsID}}}"}}""");
        var newSessID = created["result"]!["id"]!.GetValue<string>();
        Assert.True(SendCommand(
            $$$"""{"cmd":"session.rename","target":"{{{newSessID}}}","args":{"name":"hidden"}}""")["ok"]!
            .GetValue<bool>(), "renaming the new session should succeed");
        Assert.Equal(2, AllSessionNodes().Count); // both session rows present expanded

        // collapse ONLY the second workspace: its "hidden" row leaves the sidebar, "stay" stays —
        // unlike sidebar.collapse, this targets one workspace and does not depend on which is active.
        var collapse = SendCommand($$$"""{"cmd":"workspace.collapse","target":"{{{secondWsID}}}"}""");
        Assert.True(collapse["ok"]!.GetValue<bool>(),
            $"workspace.collapse should succeed: {collapse.ToJsonString()}");
        Assert.Equal(secondWsID, collapse["result"]!["id"]!.GetValue<string>()); // echoes the workspace id

        // read-back: the collapsed workspace reports collapsed == true, the other omits the field —
        // the sidebar row filter (hidden "hidden", visible "stay") derives from exactly this field.
        Assert.Equal(true, WorkspaceNode(secondWsID)["collapsed"]?.GetValue<bool>());
        Assert.Null(OtherWorkspaceNode(secondWsID)["collapsed"]);

        // expand it again: the "hidden" row returns and the field is omitted.
        var expand = SendCommand($$$"""{"cmd":"workspace.expand","target":"{{{secondWsID}}}"}""");
        Assert.True(expand["ok"]!.GetValue<bool>(), $"workspace.expand should succeed: {expand.ToJsonString()}");
        Assert.Null(WorkspaceNode(secondWsID)["collapsed"]);
        Assert.NotNull(SessionNamed("hidden")); // the expanded workspace's session returns
    }

    [Fact]
    public void WorkspaceNewCollapsedStaysClosedWhenFilled()
    {
        var seededID = ActiveSessionId();
        Assert.True(SendCommand(
            $$$"""{"cmd":"session.rename","target":"{{{seededID}}}","args":{"name":"stay"}}""")["ok"]!
            .GetValue<bool>(), "renaming the seeded session should succeed");

        // create a COLLAPSED workspace, then fill it with a background (--no-select) session: it must
        // NOT open.
        var newWs = SendCommand("""{"cmd":"workspace.new","args":{"name":"quiet","collapsed":true}}""");
        var quietID = newWs["result"]!["id"]!.GetValue<string>();
        Assert.Equal(true, WorkspaceNode(quietID)["collapsed"]?.GetValue<bool>()); // reads back collapsed
        var created = SendCommand(
            $$$"""{"cmd":"session.new","args":{"workspace":"{{{quietID}}}","noSelect":true}}""");
        var buriedID = created["result"]!["id"]!.GetValue<string>();
        Assert.True(SendCommand(
            $$$"""{"cmd":"session.rename","target":"{{{buriedID}}}","args":{"name":"buried"}}""")["ok"]!
            .GetValue<bool>(), "renaming the buried session should succeed");

        // the buried session stays hidden inside the collapsed workspace; the selection never left
        // "stay". The row-visibility assertion maps to the workspace's collapsed read-back.
        Assert.Equal(true, WorkspaceNode(quietID)["collapsed"]?.GetValue<bool>());
        Assert.Equal(seededID, ActiveNodeId()); // the background add must not move the selection

        // expanding the workspace reveals the buried session.
        Assert.True(SendCommand($$$"""{"cmd":"workspace.expand","target":"{{{quietID}}}"}""")["ok"]!
            .GetValue<bool>(), "workspace.expand should succeed");
        Assert.Null(WorkspaceNode(quietID)["collapsed"]);
        Assert.NotNull(SessionNamed("buried")); // the buried session should now be visible
    }

    [Fact]
    public void WorkspaceCollapsePersistsWithSidebarHidden()
    {
        // add a second workspace to target.
        var newWs = SendCommand("""{"cmd":"workspace.new","args":{"name":"target"}}""");
        var wsID = newWs["result"]!["id"]!.GetValue<string>();

        // HIDE the sidebar: upstream WindowContentView tears down the sidebar Coordinator and its
        // .agtermSetWorkspaceExpanded observer, so a notification-only persist would silently drop —
        // the store write must happen in the control arm regardless.
        Assert.True(SendCommand("""{"cmd":"sidebar","args":{"mode":"hide"}}""")["ok"]!.GetValue<bool>(),
            "sidebar hide should succeed");
        Assert.False(Tree()["tree"]!["sidebarVisible"]!.GetValue<bool>()); // sidebar reports hidden

        // collapse the target with the sidebar hidden: the read-back must reflect it IMMEDIATELY.
        var collapse = SendCommand($$$"""{"cmd":"workspace.collapse","target":"{{{wsID}}}"}""");
        Assert.True(collapse["ok"]!.GetValue<bool>(),
            $"workspace.collapse should succeed: {collapse.ToJsonString()}");
        Assert.Equal(true, WorkspaceNode(wsID)["collapsed"]?.GetValue<bool>());

        // expand it again while still hidden: the field clears.
        Assert.True(SendCommand($$$"""{"cmd":"workspace.expand","target":"{{{wsID}}}"}""")["ok"]!
            .GetValue<bool>(), "workspace.expand should succeed");
        Assert.Null(WorkspaceNode(wsID)["collapsed"]);
    }

    // MARK: - tree read-back helpers (the ports of the upstream AX/tree oracles)

    /// <summary>The upstream activeSessionID(): the first workspace's first session id from a fresh tree.</summary>
    private string ActiveSessionId() =>
        Tree()["tree"]!["workspaces"]![0]!["sessions"]![0]!["id"]!.GetValue<string>();

    /// <summary>Every session node from a FRESH tree, flattened across all workspaces.</summary>
    private List<JsonObject> AllSessionNodes() =>
        Tree()["tree"]!["workspaces"]!.AsArray()
            .SelectMany(w => w!["sessions"]!.AsArray())
            .Select(s => s!.AsObject())
            .ToList();

    /// <summary>One session's node from a FRESH tree — the read-back oracle for the state a command
    /// just set; fails when the session is absent (matches upstream sessionNode(id:)). Ids compare
    /// case-insensitively, like upstream matchesID.</summary>
    private JsonObject SessionNode(string id)
    {
        var node = AllSessionNodes().FirstOrDefault(s =>
            string.Equals(s["id"]!.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase));
        Assert.True(node is not null, $"session {id} should be in the tree");
        return node!;
    }

    /// <summary>The session node with the given display name, or null — the stand-in for the
    /// upstream sessionRowValueExists(containing:) presence probe.</summary>
    private JsonObject? SessionNamed(string name) =>
        AllSessionNodes().FirstOrDefault(s => s["name"]?.GetValue<string>() == name);

    /// <summary>One workspace's node from a FRESH tree; fails when absent.</summary>
    private JsonObject WorkspaceNode(string id)
    {
        var node = Tree()["tree"]!["workspaces"]!.AsArray()
            .Select(w => w!.AsObject())
            .FirstOrDefault(w => string.Equals(w["id"]!.GetValue<string>(), id,
                StringComparison.OrdinalIgnoreCase));
        Assert.True(node is not null, $"workspace {id} should be in the tree");
        return node!;
    }

    /// <summary>The first workspace whose id is NOT the given one — the upstream "the other
    /// workspace omits the field" probe (first { $0["id"] != secondWsID }).</summary>
    private JsonObject OtherWorkspaceNode(string notId)
    {
        var node = Tree()["tree"]!["workspaces"]!.AsArray()
            .Select(w => w!.AsObject())
            .FirstOrDefault(w => !string.Equals(w["id"]!.GetValue<string>(), notId,
                StringComparison.OrdinalIgnoreCase));
        Assert.True(node is not null, "another workspace should be in the tree");
        return node!;
    }

    /// <summary>The unseen badge count reported for a session in the current tree, or null when
    /// omitted (zero) — the port of unseenCount(forSession:).</summary>
    private int? UnseenCount(string id) => SessionNode(id)["unseen"]?.GetValue<int>();

    /// <summary>The id of the active (selected) session in the current tree, or null when none —
    /// the port of activeNodeID().</summary>
    private string? ActiveNodeId() =>
        AllSessionNodes().FirstOrDefault(s => s["active"]?.GetValue<bool>() == true)?["id"]
            ?.GetValue<string>();

    /// <summary>Whether the workspace with id reports focused == true in the current tree
    /// (omitted = not focused) — the port of workspaceFocused(_:timeout:), poll dropped.</summary>
    private bool WorkspaceFocused(string id) =>
        WorkspaceNode(id)["focused"]?.GetValue<bool>() == true;

    /// <summary>The flagged-mode row count: the flat flagged view lists exactly the flagged
    /// sessions, so the upstream session-row count maps to counting flagged == true.</summary>
    private int FlaggedSessionCount() =>
        AllSessionNodes().Count(s => s["flagged"]?.GetValue<bool>() == true);

    /// <summary>Whether the session named name is in the flagged working set — the flagged-mode
    /// row-presence probe (upstream sessionRowValueExists in flagged mode).</summary>
    private bool SessionFlagged(string name) =>
        SessionNamed(name)?["flagged"]?.GetValue<bool>() == true;
}
