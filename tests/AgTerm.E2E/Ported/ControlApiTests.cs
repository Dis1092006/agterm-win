using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using AgTerm.ControlServer;
using Xunit;

namespace AgTerm.E2E.Ported;

/// <summary>
/// 1:1 port of agtermUITests/ControlAPIUITests.swift onto the headless host. Request literals are
/// byte-identical to the upstream Swift. UI-side interactions take their headless equivalents:
/// terminal typing/reading goes through an attached FakeSurface (Injected / ScreenText), sidebar
/// assertions read the tree back, "relaunch" scenarios boot a SECOND HeadlessHost over the SAME
/// state dir, and the workspaces.json file-polling oracle becomes a direct file read (the host is
/// synchronous, so every upstream poll collapses to a single assertion).
/// </summary>
public class ControlApiTests : ControlApiTestCase, IDisposable
{
    private readonly List<HeadlessHost> _relaunched = new();

    public new void Dispose()
    {
        foreach (var host in _relaunched) host.Stop();
        base.Dispose();
    }

    // a `tree` request returns the seeded workspace and session with non-empty ids.
    [Fact]
    public void TreeReturnsSeededWorkspaceAndSession()
    {
        var response = SendCommand("""{"cmd":"tree"}""");
        Assert.True(response["ok"]!.GetValue<bool>(), $"tree should succeed: {response.ToJsonString()}");

        var result = Assert.IsType<JsonObject>(response["result"]);
        var tree = Assert.IsType<JsonObject>(result["tree"]);
        var workspaces = Assert.IsType<JsonArray>(tree["workspaces"]);
        Assert.Single(workspaces);

        var workspace = (JsonObject)workspaces[0]!;
        Assert.False(string.IsNullOrEmpty(workspace["id"]?.GetValue<string>()), "workspace should have an id");
        var sessions = Assert.IsType<JsonArray>(workspace["sessions"]);
        Assert.Single(sessions);
        Assert.False(string.IsNullOrEmpty(sessions[0]!["id"]?.GetValue<string>()), "session should have an id");
        Assert.True(sessions[0]!["active"]!.GetValue<bool>(), "the seeded session should be active");
    }

    // the tree exposes a session's raw OSC title. Upstream types a printf that emits OSC 2 and the
    // pty callback delivers the title; headless the FakeSurface records the injection and the OSC 2
    // report is applied to the model directly (the step a real terminal engine would perform).
    [Fact]
    public void TreeExposesOscTitle()
    {
        var surface = Attach(Host, ActiveSessionId());
        var text = "printf '\\033]2;CTL-OSC-TITLE\\007'; cat\n";
        var line = "{\"cmd\":\"session.type\",\"args\":{\"text\":" + JsonString(text) + "}}";
        var typed = SendCommand(line);
        Assert.True(typed["ok"]!.GetValue<bool>(), $"session.type should succeed: {typed.ToJsonString()}");
        Assert.Contains(text, surface.Injected);

        // the shell "ran" the printf: the OSC 2 title report lands on the session model.
        Host.Store.SessionWithId(Guid.Parse(ActiveSessionId()))!.OscTitle = "CTL-OSC-TITLE";

        var resp = SendCommand("""{"cmd":"tree"}""");
        Assert.Equal("CTL-OSC-TITLE", FirstSessionTitle(resp));
    }

    /// <summary>The first session's `title` from a `tree` response, or null if absent.</summary>
    private static string? FirstSessionTitle(JsonObject response)
    {
        var sessions = (JsonArray?)((JsonArray?)response["result"]?["tree"]?["workspaces"])?[0]?["sessions"];
        return sessions?[0]?["title"]?.GetValue<string>();
    }

    // tree exposes each session's LIVE foreground command.
    [Fact(Skip = "PORT-GAP: Windows ConPTY has no foreground-pid inspection; tree omits the " +
                 "foreground argv (docs/PLATFORM-NOTES.md).")]
    public void TreeExposesForegroundProcess()
    {
        // Swift body:
        // let marker = markerDir.appendingPathComponent("fg-\(UUID().uuidString)").path
        // let payload: [String: Any] = ["cmd": "session.type", "args": ["text": "tee \(marker)\n"]]
        // let line = String(data: try JSONSerialization.data(withJSONObject: payload), encoding: .utf8)!
        // XCTAssertEqual(try sendCommand(line)["ok"] as? Bool, true, "session.type should succeed")
        // var fg: [String]?
        // for _ in 0..<40 {
        //     let resp = try sendCommand(#"{"cmd":"tree"}"#)
        //     if let f = firstSessionForeground(resp), f.first == "tee" { fg = f; break }
        //     RunLoop.current.run(until: Date().addingTimeInterval(0.5))
        // }
        // XCTAssertEqual(fg, ["tee", marker], "tree should expose the session's live foreground command")
    }

    // tree exposes each session's agent status: setting `blocked` surfaces `status: "blocked"` on that
    // session's node, while a session left idle omits the key entirely (the read side of session.status).
    [Fact]
    public void TreeExposesAgentStatus()
    {
        var tree = SendCommand("""{"cmd":"tree"}""");
        var sessions = (JsonArray)tree["result"]!["tree"]!["workspaces"]![0]!["sessions"]!;
        var seeded = sessions[0]!["id"]!.GetValue<string>();

        // a second session stays idle so its node can prove the status key is omitted when idle.
        var created = SendCommand("""{"cmd":"session.new"}""");
        var idleId = created["result"]!["id"]!.GetValue<string>();
        Assert.Equal(2, SessionCounts(SendCommand("""{"cmd":"tree"}"""))[0]);

        // baseline: every fresh session is idle, so the seeded node omits the status key.
        var before = SendCommand("""{"cmd":"tree"}""");
        Assert.Null(SessionNode(before, seeded)!["status"]);

        // set blocked on the seeded session; its node now reports status "blocked".
        var set = SendCommand($$$"""{"cmd":"session.status","target":"{{{seeded}}}","args":{"status":"blocked"}}""");
        Assert.True(set["ok"]!.GetValue<bool>(), $"session.status blocked should succeed: {set.ToJsonString()}");

        var after = SendCommand("""{"cmd":"tree"}""");
        Assert.Equal("blocked", SessionNode(after, seeded)!["status"]!.GetValue<string>());

        // the untouched second session is still idle, so it omits the status key.
        Assert.Null(SessionNode(after, idleId)!["status"]);
    }

    // restore.clear succeeds and the server keeps serving; it is CAPTURE-scoped, so it must leave a
    // per-session `session.restore` override pinned.
    [Fact]
    public void RestoreClearSucceeds()
    {
        var resp = SendCommand("""{"cmd":"restore.clear"}""");
        Assert.True(resp["ok"]!.GetValue<bool>(), $"restore.clear should succeed: {resp.ToJsonString()}");

        var sessionId = ActiveSessionId();
        Assert.True(SendRestore(sessionId, "set", command: "echo kept")["ok"]!.GetValue<bool>(),
            "pinning an override should succeed");
        Assert.True(SendCommand("""{"cmd":"restore.clear"}""")["ok"]!.GetValue<bool>(),
            "restore.clear should succeed");
        Assert.Equal("echo kept", RestoreNode(sessionId)["restoreCommand"]!.GetValue<string>());
    }

    // the two `--pane-id` FALLBACK paths: an EMPTY token counts as ABSENT, and an unresolvable token
    // supplied WITH an explicit `--pane` falls back to that pane instead of erroring.
    [Fact]
    public void SessionRestorePaneIDFallsBackWhenEmptyOrUnresolvable()
    {
        var sessionId = ActiveSessionId();
        var empty = SendRestore(sessionId, "set", command: "echo empty-token", paneId: "");
        Assert.True(empty["ok"]!.GetValue<bool>(), $"an empty pane id must be treated as absent: {empty.ToJsonString()}");
        Assert.Equal("echo empty-token", RestoreNode(sessionId)["restoreCommand"]!.GetValue<string>());

        var withPane = SendRestore(sessionId, "set", command: "echo fallback",
            pane: "left", paneId: "not-a-real-token");
        Assert.True(withPane["ok"]!.GetValue<bool>(),
            $"an unresolvable token with an explicit --pane must fall back, not error: {withPane.ToJsonString()}");
        Assert.Equal("echo fallback", RestoreNode(sessionId)["restoreCommand"]!.GetValue<string>());
    }

    // the read side of session.restore: a pinned command reads back verbatim, `--none` reads back as
    // an EMPTY string (key present), and `--clear` omits the key.
    [Fact]
    public void SessionRestoreReadsBackOnTree()
    {
        var sessionId = ActiveSessionId();
        Assert.Null(RestoreNode(sessionId)["restoreCommand"]);

        Assert.True(SendRestore(sessionId, "set", command: "echo pinned")["ok"]!.GetValue<bool>());
        Assert.Equal("echo pinned", RestoreNode(sessionId)["restoreCommand"]!.GetValue<string>());

        Assert.True(SendRestore(sessionId, "none")["ok"]!.GetValue<bool>());
        var node = RestoreNode(sessionId);
        Assert.NotNull(node["restoreCommand"]);
        Assert.Equal("", node["restoreCommand"]!.GetValue<string>());

        Assert.True(SendRestore(sessionId, "clear")["ok"]!.GetValue<bool>());
        Assert.Null(RestoreNode(sessionId)["restoreCommand"]);
    }

    // `--pane-id` addresses the pane's LIVE slot: after the MAIN pane exits and the split survivor
    // is PROMOTED into the main slot, its own token must pin restoreCommand, not splitRestoreCommand.
    // Headless the surfaces attach externally and "the main pane's shell exits" is the store's
    // ClosePrimaryPane path (what the primary surface's exit callback drives).
    [Fact]
    public void SessionRestorePaneIDFollowsPromotedPane()
    {
        var sessionId = ActiveSessionId();
        var session = Host.Store.SessionWithId(Guid.Parse(sessionId))!;
        session.Surface = new FakeSurface { PaneToken = "main-" + Guid.NewGuid().ToString("N") };

        Assert.True(SendCommand($$$"""{"cmd":"session.split","target":"{{{sessionId}}}","args":{"mode":"on"}}""")
            ["ok"]!.GetValue<bool>(), "split on should succeed");
        Assert.True(SessionNode(SendCommand("""{"cmd":"tree"}"""), sessionId)!["split"]!.GetValue<bool>(),
            "the session should report split:true");
        // the split pane's shell, baked with its own AGTERM_PANE_ID (the upstream readPaneToken read).
        var rightToken = "right-" + Guid.NewGuid().ToString("N");
        session.SplitSurface = new FakeSurface { PaneToken = rightToken };

        // exiting the main pane promotes the survivor into the main slot (session stays, split:false).
        Host.Store.ClosePrimaryPane(session.Id);
        Assert.False(SessionNode(SendCommand("""{"cmd":"tree"}"""), sessionId)!["split"]!.GetValue<bool>(),
            "the session should collapse to a single pane");

        Assert.True(SendRestore(sessionId, "set", command: "echo promoted", paneId: rightToken)
            ["ok"]!.GetValue<bool>(), "pinning via the survivor's own pane id should succeed");
        var node = RestoreNode(sessionId);
        Assert.Equal("echo promoted", node["restoreCommand"]!.GetValue<string>());
        Assert.Null(node["splitRestoreCommand"]);
    }

    // the pane-resolution rejections: a right pane that does not exist, a token that resolves to the
    // scratch, and an unresolvable token with no `--pane` fallback.
    [Fact]
    public void SessionRestoreRejectsUnrestorablePanes()
    {
        var sessionId = ActiveSessionId();
        var noSplit = SendRestore(sessionId, "set", command: "echo x", pane: "right");
        Assert.False(noSplit["ok"]!.GetValue<bool>(), "the right pane of a split-less session should be rejected");
        Assert.Equal("session has no split", noSplit["error"]!.GetValue<string>());

        Assert.True(SendCommand($$$"""{"cmd":"session.scratch","target":"{{{sessionId}}}","args":{"mode":"on"}}""")
            ["ok"]!.GetValue<bool>(), "scratch on should succeed");
        // the scratch shell, baked with its own AGTERM_PANE_ID (surfaces attach externally headless).
        var scratchToken = "scratch-" + Guid.NewGuid().ToString("N");
        Host.Store.SessionWithId(Guid.Parse(sessionId))!.ScratchSurface =
            new FakeSurface { PaneToken = scratchToken };
        var scratch = SendRestore(sessionId, "set", command: "echo x", paneId: scratchToken);
        Assert.False(scratch["ok"]!.GetValue<bool>(), "a token resolving to the scratch should be rejected");
        Assert.Equal("the scratch terminal is never restored", scratch["error"]!.GetValue<string>());

        var unknown = SendRestore(sessionId, "set", command: "echo x", paneId: "not-a-real-token");
        Assert.False(unknown["ok"]!.GetValue<bool>(), "an unresolvable token with no --pane fallback should be rejected");
        Assert.Equal("unknown pane id: not-a-real-token", unknown["error"]!.GetValue<string>());

        Assert.Null(RestoreNode(sessionId)["restoreCommand"]);
    }

    // session.reveal resolves the active session and drives the platform action (headless: selects
    // the session, the documented adaptation); a missing target proves normal resolver errors remain.
    [Fact]
    public void SessionRevealSucceedsAndResolvesTargets()
    {
        var activeId = ActiveSessionId();
        var revealed = SendCommand("""{"cmd":"session.reveal","target":"active"}""");
        Assert.True(revealed["ok"]!.GetValue<bool>(), $"session.reveal should succeed: {revealed.ToJsonString()}");
        Assert.Equal(activeId.ToLowerInvariant(),
            revealed["result"]!["id"]!.GetValue<string>().ToLowerInvariant());

        var missing = SendCommand("""{"cmd":"session.reveal","target":"ffffffff"}""");
        Assert.False(missing["ok"]!.GetValue<bool>(), "an unknown target should fail");
        Assert.Contains("no such session", missing["error"]!.GetValue<string>());
    }

    // session.background sets a text watermark and clears it; bad input is rejected and the server
    // stays alive. Covers the control round-trip + validation (no pixels headless, same as upstream).
    [Fact]
    public void SessionBackgroundSetClearAndValidation()
    {
        var sid = ActiveSessionId();

        var text = SendCommand($$$"""{"cmd":"session.background","target":"{{{sid}}}","args":{"mode":"text","text":"STAGING","opacity":0.2}}""");
        Assert.True(text["ok"]!.GetValue<bool>(), $"session.background text should succeed: {text.ToJsonString()}");

        // read-back: the watermark spec now rides the session's tree node (set/clear/query symmetry).
        var afterSet = SendCommand("""{"cmd":"tree"}""");
        var setNode = SessionNode(afterSet, sid)!;
        var bg = Assert.IsType<JsonObject>(setNode["background"]);
        Assert.Equal("text", bg["kind"]!.GetValue<string>());
        Assert.Equal("STAGING", bg["text"]!.GetValue<string>());

        // a solid background color reads back with kind "color" and the hex.
        var colorSet = SendCommand($$$"""{"cmd":"session.background","target":"{{{sid}}}","args":{"mode":"color","color":"#ff0000"}}""");
        Assert.True(colorSet["ok"]!.GetValue<bool>(), $"session.background color should succeed: {colorSet.ToJsonString()}");
        var afterColor = SendCommand("""{"cmd":"tree"}""");
        var colorBg = Assert.IsType<JsonObject>(SessionNode(afterColor, sid)!["background"]);
        Assert.Equal("color", colorBg["kind"]!.GetValue<string>());
        Assert.Equal("#ff0000", colorBg["colorHex"]!.GetValue<string>());

        var badColor = SendCommand($$$"""{"cmd":"session.background","target":"{{{sid}}}","args":{"mode":"color","color":"red"}}""");
        Assert.False(badColor["ok"]!.GetValue<bool>(), "a malformed color should be rejected");

        // color mode with no color hits the "requires a color" guard.
        var emptyColor = SendCommand($$$"""{"cmd":"session.background","target":"{{{sid}}}","args":{"mode":"color"}}""");
        Assert.False(emptyColor["ok"]!.GetValue<bool>(), "color mode with no color should be rejected");
        Assert.Equal("session.background color requires a color", emptyColor["error"]!.GetValue<string>());

        var missing = SendCommand($$$"""{"cmd":"session.background","target":"{{{sid}}}","args":{"mode":"image","path":"/no/such.png"}}""");
        Assert.False(missing["ok"]!.GetValue<bool>(), "a missing image file should be rejected");

        var badFit = SendCommand($$$"""{"cmd":"session.background","target":"{{{sid}}}","args":{"mode":"image","path":"/no/such.png","fit":"fill"}}""");
        Assert.False(badFit["ok"]!.GetValue<bool>(), "an invalid fit should be rejected");

        // config-injection vector: the control-char guard runs BEFORE the format/existence checks.
        var injection = SendCommand($$$"""{"cmd":"session.background","target":"{{{sid}}}","args":{"mode":"image","path":"x.png\nclipboard-read = allow\ny.png"}}""");
        Assert.False(injection["ok"]!.GetValue<bool>(), "an image path with a control char must be rejected");
        Assert.Equal("image path must not contain control characters", injection["error"]!.GetValue<string>());

        var badOpacity = SendCommand($$$"""{"cmd":"session.background","target":"{{{sid}}}","args":{"mode":"text","text":"X","opacity":5}}""");
        Assert.False(badOpacity["ok"]!.GetValue<bool>(), "an out-of-range opacity should be rejected");

        // an over-long text must be rejected at the boundary.
        var longText = new string('A', 5000);
        var tooLong = SendCommand($$$"""{"cmd":"session.background","target":"{{{sid}}}","args":{"mode":"text","text":"{{{longText}}}"}}""");
        Assert.False(tooLong["ok"]!.GetValue<bool>(), "an over-long watermark text should be rejected");

        var cleared = SendCommand($$$"""{"cmd":"session.background","target":"{{{sid}}}","args":{"mode":"clear"}}""");
        Assert.True(cleared["ok"]!.GetValue<bool>(), $"session.background clear should succeed: {cleared.ToJsonString()}");

        var tree = SendCommand("""{"cmd":"tree"}""");
        Assert.True(tree["ok"]!.GetValue<bool>(), "the server should stay alive after background commands");
        // read-back after clear: the background key is omitted from the node.
        Assert.Null(SessionNode(tree, sid)!["background"]);
    }

    // a malformed JSON line returns ok:false with an error, and the server stays alive.
    [Fact]
    public void MalformedRequestErrorsAndServerStaysAlive()
    {
        var bad = SendCommand("not json at all");
        Assert.False(bad["ok"]!.GetValue<bool>(), "malformed request should fail");
        Assert.False(string.IsNullOrEmpty(bad["error"]?.GetValue<string>()),
            "a failed request should carry an error string");

        var good = SendCommand("""{"cmd":"tree"}""");
        Assert.True(good["ok"]!.GetValue<bool>(), "the server should still answer after a bad request");
    }

    // session.new returns an id and the session appears in the tree; session.close removes it.
    [Fact]
    public void SessionNewAndClose()
    {
        Assert.Equal(new[] { 1 }, SessionCounts(SendCommand("""{"cmd":"tree"}""")));

        var created = SendCommand("""{"cmd":"session.new"}""");
        Assert.True(created["ok"]!.GetValue<bool>(), $"session.new should succeed: {created.ToJsonString()}");
        var newId = created["result"]!["id"]!.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(newId), "the new session id should not be empty");
        Assert.Equal(new[] { 2 }, SessionCounts(SendCommand("""{"cmd":"tree"}""")));

        var closed = SendCommand($$$"""{"cmd":"session.close","target":"{{{newId}}}"}""");
        Assert.True(closed["ok"]!.GetValue<bool>(), $"session.close should succeed: {closed.ToJsonString()}");
        Assert.Equal(new[] { 1 }, SessionCounts(SendCommand("""{"cmd":"tree"}""")));
    }

    // session.duplicate opens a fresh session rooted at the source's cwd, inserted DIRECTLY AFTER
    // the source, carrying the source's cwd, and it becomes the active session.
    [Fact]
    public void SessionDuplicate()
    {
        const string sourceId = "DD100000-0000-0000-0000-000000000001";
        // a real dir the source's cwd points at (headless keeps the seeded string verbatim — no
        // OSC 7 canonicalization to dodge, but a real dir keeps the scenario genuine).
        var cwd = (StateDir + "\\agterm-dup-" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
        Directory.CreateDirectory(cwd);
        var snapshot = $$$"""{"version":1,"selectedSessionID":"{{{sourceId}}}","workspaces":[{"id":"{{{NewId()}}}","name":"workspace 1","sessions":[{"id":"{{{sourceId}}}","customName":null,"cwd":"{{{cwd}}}"}]}]}""";
        var host = Relaunch(snapshot);
        Assert.Equal(new[] { 1 }, SessionCounts(SendTo(host, """{"cmd":"tree"}""")));

        var dup = SendTo(host, $$$"""{"cmd":"session.duplicate","target":"{{{sourceId}}}"}""");
        Assert.True(dup["ok"]!.GetValue<bool>(), $"session.duplicate should succeed: {dup.ToJsonString()}");
        var newId = dup["result"]!["id"]!.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(newId), "the new session id should not be empty");
        Assert.NotEqual(sourceId.ToLowerInvariant(), newId.ToLowerInvariant());

        var tree = SendTo(host, """{"cmd":"tree"}""");
        var sessions = (JsonArray)tree["result"]!["tree"]!["workspaces"]![0]!["sessions"]!;
        Assert.Equal(2, sessions.Count);

        // inserted DIRECTLY AFTER its source, not appended elsewhere.
        Assert.Equal(sourceId.ToLowerInvariant(), sessions[0]!["id"]!.GetValue<string>().ToLowerInvariant());
        Assert.Equal(newId.ToLowerInvariant(), sessions[1]!["id"]!.GetValue<string>().ToLowerInvariant());
        // carries the source's cwd (the read-back) and becomes the active session.
        Assert.Equal(sessions[0]!["cwd"]!.GetValue<string>(), sessions[1]!["cwd"]!.GetValue<string>());
        Assert.Equal(cwd, sessions[1]!["cwd"]!.GetValue<string>());
        Assert.True(sessions[1]!["active"]!.GetValue<bool>(), "the duplicate becomes the active session");
    }

    // session.close with multiple targets hides all targeted sessions together (the save is deferred
    // by the grace window, so the persisted file still holds the pre-close snapshot; assert via tree).
    [Fact]
    public void SessionCloseMultipleTargets()
    {
        const string firstId = "AA100000-0000-0000-0000-000000000001";
        const string secondId = "AA200000-0000-0000-0000-000000000002";
        const string thirdId = "AA300000-0000-0000-0000-000000000003";
        // grace-undo is opt-in headless (the default is OFF, see PLATFORM-NOTES); the upstream
        // default-ON setting maps to CloseGraceUndoEnabled = true here.
        var host = Relaunch(ThreeSessionSnapshot(firstId, secondId, thirdId),
            new HeadlessHostOptions { CloseGraceUndoEnabled = true });

        var closed = SendTo(host, $$$"""{"cmd":"session.close","args":{"targets":["{{{secondId}}}","{{{thirdId}}}"]}}""");
        Assert.True(closed["ok"]!.GetValue<bool>(), $"batch session.close should succeed: {closed.ToJsonString()}");
        Assert.Equal(2, closed["result"]!["affected"]!.GetValue<int>());
        Assert.Equal(3, PersistedSessionCounts().Sum());

        var tree = SendTo(host, """{"cmd":"tree"}""");
        var node = SessionNode(tree, firstId);
        Assert.NotNull(node);
        Assert.Equal(firstId, node!["id"]!.GetValue<string>());
        Assert.Null(SessionNode(tree, secondId));
        Assert.Null(SessionNode(tree, thirdId));
    }

    // With grace disabled, batch close tears down and persists immediately instead of leaving the
    // pre-close snapshot on disk for the undo window.
    [Fact]
    public void SessionCloseMultipleTargetsHonorsDisabledGraceUndo()
    {
        const string firstId = "AB100000-0000-0000-0000-000000000001";
        const string secondId = "AB200000-0000-0000-0000-000000000002";
        const string thirdId = "AB300000-0000-0000-0000-000000000003";
        // upstream relaunches with {"closeGraceUndoEnabled":false}; headless the setting is a host
        // option and OFF is already the default.
        var host = Relaunch(ThreeSessionSnapshot(firstId, secondId, thirdId));

        var closed = SendTo(host, $$$"""{"cmd":"session.close","args":{"targets":["{{{secondId}}}","{{{thirdId}}}"]}}""");

        Assert.True(closed["ok"]!.GetValue<bool>(), $"immediate batch session.close should succeed: {closed.ToJsonString()}");
        Assert.Equal(2, closed["result"]!["affected"]!.GetValue<int>());
        Assert.Equal(1, PersistedSessionCounts().Sum());
    }

    // Batch target resolution is all-or-nothing: an unknown or ambiguous target fails the WHOLE
    // request without closing the valid member.
    [Fact]
    public void SessionCloseBatchIsAllOrNothing()
    {
        const string firstId = "ABCD1111-0000-0000-0000-000000000001";
        const string secondId = "ABCD2222-0000-0000-0000-000000000002";
        const string thirdId = "AC330000-0000-0000-0000-000000000003";
        var host = Relaunch(ThreeSessionSnapshot(firstId, secondId, thirdId));

        var unknown = SendTo(host, $$$"""{"cmd":"session.close","args":{"targets":["{{{thirdId}}}","eeee"]}}""");
        Assert.False(unknown["ok"]!.GetValue<bool>(), "an unknown target must fail the whole batch");
        Assert.Equal("no such session: eeee", unknown["error"]!.GetValue<string>());

        var ambiguous = SendTo(host, $$$"""{"cmd":"session.close","args":{"targets":["{{{thirdId}}}","abcd"]}}""");
        Assert.False(ambiguous["ok"]!.GetValue<bool>(), "an ambiguous prefix must fail the whole batch");
        Assert.Equal("ambiguous session prefix 'abcd' → ABCD1111, ABCD2222", ambiguous["error"]!.GetValue<string>());

        var tree = SendTo(host, """{"cmd":"tree"}""");
        Assert.NotNull(SessionNode(tree, firstId));
        Assert.NotNull(SessionNode(tree, secondId));
        Assert.NotNull(SessionNode(tree, thirdId));
    }

    // session.new --command runs the command AS the session's process. Headless spawns no process,
    // so the session's InitialCommand seam (what the surface factory would exec) is the read-back
    // instead of the upstream marker file.
    [Fact]
    public void SessionNewWithCommandRunsAsProcess()
    {
        var marker = (StateDir + "/agterm-cmd-" + Guid.NewGuid().ToString("N") + ".txt").Replace('\\', '/');
        var cmd = $"printf RANCMD > {marker}";
        var created = SendCommand("{\"cmd\":\"session.new\",\"args\":{\"command\":" + JsonString(cmd) + "}}");
        Assert.True(created["ok"]!.GetValue<bool>(), $"session.new --command should succeed: {created.ToJsonString()}");

        var newId = created["result"]!["id"]!.GetValue<string>();
        Assert.Equal(cmd, Host.Store.SessionWithId(Guid.Parse(newId))!.InitialCommand);
    }

    // session.new --command --wait HOLDS the session open after the command exits (issue #254):
    // the tree reports commandWait=true (the read-back).
    [Fact]
    public void SessionNewCommandWaitHoldsSessionAfterExit()
    {
        var created = SendCommand("""{"cmd":"session.new","args":{"command":"true","wait":true}}""");
        Assert.True(created["ok"]!.GetValue<bool>(), $"session.new --command --wait should succeed: {created.ToJsonString()}");
        var newId = created["result"]!["id"]!.GetValue<string>();

        var tree = SendCommand("""{"cmd":"tree"}""");
        var node = SessionNode(tree, newId);
        Assert.NotNull(node);
        Assert.True(node!["commandWait"]!.GetValue<bool>(),
            "the tree should report commandWait=true for a held session");
    }

    // --wait is meaningless without a command; the dispatcher rejects it SERVER-SIDE.
    [Fact]
    public void SessionNewWaitWithoutCommandRejectedServerSide()
    {
        var resp = SendCommand("""{"cmd":"session.new","args":{"wait":true}}""");
        Assert.False(resp["ok"]!.GetValue<bool>(), $"--wait without --command must be rejected: {resp.ToJsonString()}");
        Assert.Equal("--wait requires --command", resp["error"]!.GetValue<string>());
    }

    // a control session.new (frontmost window) FOCUSES the new session, so real keystrokes reach it.
    [Fact(Skip = "PORT-GAP: a live WebView2 test must type through keyboard focus into the new " +
                 "--command session; LivePortedE2ETests tests untargeted control-channel typing only.")]
    public void SessionNewWithCommandFocusesTheNewSession()
    {
        // Swift body:
        // let marker = NSTemporaryDirectory() + "agterm-focus-\(UUID().uuidString).txt"
        // let cmd = "head -n1 > \(marker)"
        // let created = try sendCommand(#"{"cmd":"session.new","args":{"command":"\#(cmd)"}}"#)
        // XCTAssertEqual(created["ok"] as? Bool, true, "session.new --command should succeed: \(created)")
        // RunLoop.current.run(until: Date().addingTimeInterval(2))
        // app.typeText("FOCUSED")
        // app.typeKey(.return, modifierFlags: [])
        // var got = false
        // for _ in 0..<40 {
        //     if let s = (try? String(contentsOfFile: marker, encoding: .utf8))?
        //         .trimmingCharacters(in: .whitespacesAndNewlines), s == "FOCUSED" { got = true; break }
        //     RunLoop.current.run(until: Date().addingTimeInterval(0.5))
        // }
        // XCTAssertTrue(got, "the new command session should be focused so typed text reaches its process")
        // try? FileManager.default.removeItem(atPath: marker)
    }

    // session.new --name seeds the new session's custom name at creation.
    [Fact]
    public void SessionNewWithName()
    {
        var created = SendCommand("""{"cmd":"session.new","args":{"name":"myhost"}}""");
        Assert.True(created["ok"]!.GetValue<bool>(), $"session.new --name should succeed: {created.ToJsonString()}");
        var newId = created["result"]!["id"]!.GetValue<string>();
        Assert.Equal("myhost", SessionNode(SendCommand("""{"cmd":"tree"}"""), newId)!["name"]!.GetValue<string>());
    }

    // session.new --workspace-name addresses a workspace by its sidebar label; --create-workspace
    // creates once and REUSES on the next call (idempotent).
    [Fact]
    public void SessionNewWorkspaceNameCreatesThenReuses()
    {
        // no match + no create -> error, and nothing is created.
        var missing = SendCommand("""{"cmd":"session.new","args":{"workspaceName":"servers"}}""");
        Assert.False(missing["ok"]!.GetValue<bool>(), $"name target with no match and no create should fail: {missing.ToJsonString()}");
        Assert.Contains("no workspace named", missing["error"]!.GetValue<string>());
        Assert.Equal(new[] { "workspace 1" }, WorkspaceNames(SendCommand("""{"cmd":"tree"}""")));

        // create: the "servers" workspace is added and the session lands in it.
        var created = SendCommand("""{"cmd":"session.new","args":{"workspaceName":"servers","createWorkspace":true}}""");
        Assert.True(created["ok"]!.GetValue<bool>(), $"create-workspace should succeed: {created.ToJsonString()}");
        Assert.Equal(new[] { "workspace 1", "servers" }, WorkspaceNames(SendCommand("""{"cmd":"tree"}""")));

        // reuse: a second call with the same name does NOT create a duplicate.
        var reused = SendCommand("""{"cmd":"session.new","args":{"workspaceName":"servers","createWorkspace":true}}""");
        Assert.True(reused["ok"]!.GetValue<bool>(), $"the second create should reuse the workspace: {reused.ToJsonString()}");
        Assert.Equal(new[] { "workspace 1", "servers" }, WorkspaceNames(SendCommand("""{"cmd":"tree"}""")));
        Assert.Equal(new[] { 1, 2 }, SessionCounts(SendCommand("""{"cmd":"tree"}""")));

        // no-create name target of an EXISTING workspace succeeds and lands there (a third session).
        var existing = SendCommand("""{"cmd":"session.new","args":{"workspaceName":"servers"}}""");
        Assert.True(existing["ok"]!.GetValue<bool>(), $"no-create name target of an existing workspace should succeed: {existing.ToJsonString()}");
        Assert.Equal(new[] { 1, 3 }, SessionCounts(SendCommand("""{"cmd":"tree"}""")));
    }

    // the server arm enforces the two addressing rules independently of the CLI validate().
    [Fact]
    public void SessionNewWorkspaceNameValidationErrors()
    {
        var both = SendCommand("""{"cmd":"session.new","args":{"workspace":"active","workspaceName":"servers"}}""");
        Assert.False(both["ok"]!.GetValue<bool>(), $"both --workspace and --workspace-name should fail: {both.ToJsonString()}");
        Assert.Contains("not both", both["error"]!.GetValue<string>());

        var createNoName = SendCommand("""{"cmd":"session.new","args":{"createWorkspace":true}}""");
        Assert.False(createNoName["ok"]!.GetValue<bool>(), $"create-workspace with no name should fail: {createNoName.ToJsonString()}");
        Assert.Contains("requires --workspace-name", createNoName["error"]!.GetValue<string>());

        var blank = SendCommand("""{"cmd":"session.new","args":{"workspaceName":"   "}}""");
        Assert.False(blank["ok"]!.GetValue<bool>(), $"a blank workspace name should fail: {blank.ToJsonString()}");
        Assert.Contains("must not be blank", blank["error"]!.GetValue<string>());
    }

    // session.new --no-select creates the session in the background: the ORIGINAL session stays
    // active, so the read-back `active` flag confirms the selection was not pulled to the new one.
    [Fact]
    public void SessionNewNoSelectKeepsActiveSelection()
    {
        var original = ActiveSessionId();

        var created = SendCommand("""{"cmd":"session.new","args":{"noSelect":true}}""");
        Assert.True(created["ok"]!.GetValue<bool>(), $"session.new --no-select should succeed: {created.ToJsonString()}");
        var newId = created["result"]!["id"]!.GetValue<string>();
        Assert.NotEqual(original.ToLowerInvariant(), newId.ToLowerInvariant());

        var tree = SendCommand("""{"cmd":"tree"}""");
        Assert.True(SessionNode(tree, original)!["active"]!.GetValue<bool>(),
            "the original session stays active");
        Assert.False(SessionNode(tree, newId)!["active"]!.GetValue<bool>(),
            "the background session must not steal the selection");
    }

    // workspace.new returns an id and the workspace appears; workspace.rename is reflected.
    [Fact]
    public void WorkspaceNewAndRename()
    {
        var created = SendCommand("""{"cmd":"workspace.new","args":{"name":"control ws"}}""");
        Assert.True(created["ok"]!.GetValue<bool>(), $"workspace.new should succeed: {created.ToJsonString()}");
        var newId = created["result"]!["id"]!.GetValue<string>();
        Assert.Equal(new[] { "workspace 1", "control ws" }, WorkspaceNames(SendCommand("""{"cmd":"tree"}""")));

        var renamed = SendCommand($$$"""{"cmd":"workspace.rename","target":"{{{newId}}}","args":{"name":"renamed ws"}}""");
        Assert.True(renamed["ok"]!.GetValue<bool>(), $"workspace.rename should succeed: {renamed.ToJsonString()}");
        Assert.Equal(new[] { "workspace 1", "renamed ws" }, WorkspaceNames(SendCommand("""{"cmd":"tree"}""")));
    }

    // workspace.delete of the last workspace returns the keep-one error and leaves it present.
    [Fact]
    public void WorkspaceDeleteLastErrors()
    {
        Assert.Equal(new[] { "workspace 1" }, WorkspaceNames(SendCommand("""{"cmd":"tree"}""")));

        var response = SendCommand("""{"cmd":"workspace.delete","target":"active"}""");
        Assert.False(response["ok"]!.GetValue<bool>(), "deleting the last workspace should fail");
        Assert.Equal("cannot delete last workspace", response["error"]!.GetValue<string>());
        Assert.Equal(new[] { "workspace 1" }, WorkspaceNames(SendCommand("""{"cmd":"tree"}""")));
    }

    // a command with an unknown target returns a structured "no such …" error.
    [Fact]
    public void UnknownTargetErrors()
    {
        var response = SendCommand("""{"cmd":"session.close","target":"deadbeef"}""");
        Assert.False(response["ok"]!.GetValue<bool>(), "an unknown target should fail");
        var error = response["error"]!.GetValue<string>();
        Assert.StartsWith("no such session", error);
    }

    // session.type without select into a realized session lands in its shell. Headless the attached
    // FakeSurface IS the realized surface and its Injected list is the oracle (the upstream tty>file
    // marker needs a live shell); the type-and-retry readiness dance collapses to one call.
    [Fact]
    public void SessionTypeIntoActiveSession()
    {
        var created = SendCommand("""{"cmd":"session.new"}""");
        var newId = created["result"]!["id"]!.GetValue<string>();
        var surface = Attach(Host, newId);

        var command = $"tty > '{StateDir.Replace('\\', '/')}/active'\n";
        var typed = SendCommand(TypeRequest(command, newId, select: false));
        Assert.True(typed["ok"]!.GetValue<bool>(), $"session.type should succeed: {typed.ToJsonString()}");
        Assert.Contains(command, surface.Injected);
    }

    // session.type --select realizes a never-shown session upstream; headless surfaces attach
    // externally (nothing realizes lazily), so the surface is attached first and the --select arm
    // still routes the text into it.
    [Fact]
    public void SessionTypeSelectRealizesNeverShownSession()
    {
        var created = SendCommand("""{"cmd":"session.new"}""");
        var newId = created["result"]!["id"]!.GetValue<string>();

        var command = $"tty > '{StateDir.Replace('\\', '/')}/realized'\n";
        var surface = Attach(Host, newId);
        var typed = SendCommand(TypeRequest(command, newId, select: true));
        Assert.True(typed["ok"]!.GetValue<bool>(), $"session.type --select should succeed: {typed.ToJsonString()}");
        Assert.Contains(command, surface.Injected);
    }

    // eager session realization means a restored-but-not-selected session is already live, so
    // session.type without --select reaches it.
    [Fact]
    public void SessionTypeReachesEagerlyRealizedSession()
    {
        var selectedId = NewId();
        var otherId = NewId();
        var snapshot = $$$"""{"version":1,"selectedSessionID":"{{{selectedId}}}","workspaces":[{"id":"{{{NewId()}}}","name":"workspace 1","sessions":[{{{SessionJson(selectedId)}}},{{{SessionJson(otherId)}}}]}]}""";
        var host = Relaunch(snapshot);
        // the deck realizes every session at startup upstream; headless that is the attached surface.
        var surface = Attach(host, otherId);

        var command = $"tty > '{StateDir.Replace('\\', '/')}/eager'\n";
        var typed = SendTo(host, TypeRequest(command, otherId, select: false));
        Assert.True(typed["ok"]!.GetValue<bool>(),
            $"session.type without select reaches the eagerly-realized, non-selected session: {typed.ToJsonString()}");
        Assert.Contains(command, surface.Injected);
    }

    // session.copy on a session with no selection returns the "no selection" error.
    [Fact]
    public void SessionCopyWithoutSelectionErrors()
    {
        var created = SendCommand("""{"cmd":"session.new"}""");
        var newId = created["result"]!["id"]!.GetValue<string>();
        Attach(Host, newId); // realized surface, nothing selected

        var response = SendCommand($$$"""{"cmd":"session.copy","target":"{{{newId}}}"}""");
        Assert.False(response["ok"]!.GetValue<bool>(), $"copy with no selection should fail: {response.ToJsonString()}");
        Assert.Equal("no selection", response["error"]!.GetValue<string>());
    }

    // session.selectall selects the WHOLE buffer; session.copy is its read-back. The FakeSurface's
    // ScreenText stands in for the shell-rendered echoes (two markers separated by blank lines).
    [Fact]
    public void SessionSelectAllThenCopyReturnsBuffer()
    {
        var id = ActiveSessionId();
        const string first = "SELECTALLMARKERONE";
        const string second = "SELECTALLMARKERTWO";
        var surface = Attach(Host, id);
        surface.ScreenText = $"$ echo {first}\n{first}\n\n\n$ echo {second}\n{second}\n";

        var selected = SendCommand($$$"""{"cmd":"session.selectall","target":"{{{id}}}"}""");
        Assert.True(selected["ok"]!.GetValue<bool>(), $"session.selectall should succeed: {selected.ToJsonString()}");

        var copied = SendCommand($$$"""{"cmd":"session.copy","target":"{{{id}}}"}""");
        Assert.True(copied["ok"]!.GetValue<bool>(), $"session.copy after select-all should succeed: {copied.ToJsonString()}");
        var text = copied["result"]!["text"]!.GetValue<string>();
        Assert.Contains(first, text);
        Assert.Contains(second, text);
    }

    // session.paste pastes the SYSTEM clipboard into the session (the socket analogue of ⌘V).
    // Headless there is no clipboard: the FakeSurface records the paste action (PasteCount), which
    // is the round-trip the upstream buffer-marker poll proved.
    [Fact]
    public void SessionPasteInsertsClipboardText()
    {
        var id = ActiveSessionId();
        var surface = Attach(Host, id);

        var pasted = SendCommand($$$"""{"cmd":"session.paste","target":"{{{id}}}"}""");
        Assert.True(pasted["ok"]!.GetValue<bool>(), $"session.paste should succeed: {pasted.ToJsonString()}");
        Assert.Equal(1, surface.PasteCount);
    }

    // session.paste is UNGATED: an empty clipboard is `ok` with no buffer change, matching every
    // other binding-action arm. Pinned so the GUI-gated / socket-ungated asymmetry can't drift.
    [Fact]
    public void SessionPasteWithEmptyClipboardSucceedsWithoutChangingBuffer()
    {
        var id = ActiveSessionId();
        var surface = Attach(Host, id);
        surface.ScreenText = "$ "; // an empty clipboard paste inserts nothing

        var before = SendCommand($$$"""{"cmd":"session.text","target":"{{{id}}}"}""");
        var beforeText = before["result"]?["text"]?.GetValue<string>();

        var pasted = SendCommand($$$"""{"cmd":"session.paste","target":"{{{id}}}"}""");
        Assert.True(pasted["ok"]!.GetValue<bool>(), $"session.paste on an empty clipboard should still be ok: {pasted.ToJsonString()}");

        var after = SendCommand($$$"""{"cmd":"session.text","target":"{{{id}}}"}""");
        var afterText = after["result"]?["text"]?.GetValue<string>();
        Assert.Equal(beforeText, afterText);
    }

    // both commands surface the resolver's error for an unknown target rather than silently no-opping.
    [Fact]
    public void SessionPasteAndSelectAllRejectUnknownTarget()
    {
        foreach (var cmd in new[] { "session.paste", "session.selectall" })
        {
            var response = SendCommand($$$"""{"cmd":"{{{cmd}}}","target":"deadbeef"}""");
            Assert.False(response["ok"]!.GetValue<bool>(), $"{cmd} with an unknown target should fail: {response.ToJsonString()}");
            Assert.NotNull(response["error"]?.GetValue<string>());
        }
    }

    // session.search over the active session's buffer: seed the needle via the FakeSurface's screen
    // (the shell render), open with the needle (count + "N of M"/"M matches" display), --next/--prev
    // step the selection, --close exits and a re-open still finds the needle. The headless search is
    // synchronous, so the upstream settle-polls collapse to single calls.
    [Fact]
    public void SessionSearch()
    {
        const string needle = "agtermFINDME";
        var surface = Attach(Host, ActiveSessionId());
        var typed = SendCommand(TypeRequest($"echo {needle} {needle} {needle}\n", target: null, select: false));
        Assert.True(typed["ok"]!.GetValue<bool>(), $"typing the needle into the active session should succeed: {typed.ToJsonString()}");
        // the shell renders the typed command line plus its echoed output.
        surface.ScreenText = $"$ echo {needle} {needle} {needle}\n{needle} {needle} {needle}\n";

        var search = SendCommand($$$"""{"cmd":"session.search","target":"active","args":{"text":"{{{needle}}}"}}""");
        Assert.True(search["ok"]!.GetValue<bool>(), $"session.search should succeed: {search.ToJsonString()}");
        var count = search["result"]!["count"]!.GetValue<int>();
        Assert.True(count >= 1, "the seeded needle should match at least once");
        var display = search["result"]!["text"]!.GetValue<string>();
        Assert.True(display.Contains("of") || display.Contains("match"),
            $"the display string should report 'N of M' or 'M matches', got: {display}");

        // step the selection forward: the "N of M" selected index must ADVANCE.
        var openIndex = SelectedIndex(display);
        var next = SendCommand("""{"cmd":"session.search","target":"active","args":{"to":"next"}}""");
        Assert.True(next["ok"]!.GetValue<bool>(), $"session.search --next should succeed: {next.ToJsonString()}");
        var advancedDisplay = next["result"]!["text"]!.GetValue<string>();
        Assert.NotEqual(openIndex, SelectedIndex(advancedDisplay));

        // step back: the index must move back off the --next position.
        var prev = SendCommand("""{"cmd":"session.search","target":"active","args":{"to":"prev"}}""");
        Assert.True(prev["ok"]!.GetValue<bool>(), $"session.search --prev should succeed: {prev.ToJsonString()}");
        if (prev["result"]?["text"]?.GetValue<string>() is { } prevText && SelectedIndex(prevText) is { } prevIdx)
        {
            Assert.NotEqual(SelectedIndex(advancedDisplay), prevIdx);
        }

        // close, then confirm search actually exited: a re-open settles a fresh count again.
        var close = SendCommand("""{"cmd":"session.search","target":"active","args":{"to":"close"}}""");
        Assert.True(close["ok"]!.GetValue<bool>(), $"session.search --close should succeed: {close.ToJsonString()}");
        var reopen = SendCommand($$$"""{"cmd":"session.search","target":"active","args":{"text":"{{{needle}}}"}}""");
        Assert.True(reopen["ok"]!.GetValue<bool>(), $"re-opening search after close should succeed: {reopen.ToJsonString()}");
        Assert.True(reopen["result"]!["count"]!.GetValue<int>() >= 1,
            "search should still find the needle after a --close (close left a clean state)");
    }

    // a SECOND search with a DIFFERENT needle must report the NEW needle's count, not the previous
    // query's stale count.
    [Fact]
    public void SessionSearchSecondNeedleReportsFreshCount()
    {
        const string rare = "agtermRARE";     // appears few times
        const string common = "agtermCOMMON"; // appears many more times
        var surface = Attach(Host, ActiveSessionId());
        var line = $"echo {rare} {common} {common} {common} {common} {common}\n";
        var typed = SendCommand(TypeRequest(line, target: null, select: false));
        Assert.True(typed["ok"]!.GetValue<bool>(), $"typing the two needles should succeed: {typed.ToJsonString()}");
        // command line + echoed output: rare twice, common ten times.
        surface.ScreenText = $"$ {line}{rare} {common} {common} {common} {common} {common}\n";

        var rareCount = SettledSearchCount(rare);
        var commonCount = SettledSearchCount(common);
        Assert.True(commonCount > rareCount,
            "the second search must report the common needle's (larger) count, not the rare needle's stale count");
        SendCloseSearch();
    }

    /// <summary>Opens search for `needle` and returns the count (the headless search is synchronous,
    /// so the upstream settle-poll is a single call).</summary>
    private int SettledSearchCount(string needle)
    {
        var search = SendCommand($$$"""{"cmd":"session.search","target":"active","args":{"text":"{{{needle}}}"}}""");
        Assert.True(search["ok"]!.GetValue<bool>(), $"session.search for {needle} should succeed: {search.ToJsonString()}");
        var count = search["result"]!["count"]!.GetValue<int>();
        Assert.True(count >= 1, $"session.search for {needle} never settled a non-zero count");
        return count;
    }

    private void SendCloseSearch() =>
        SendCommand("""{"cmd":"session.search","target":"active","args":{"to":"close"}}""");

    // invalid `to` (not next|prev|close) errors before touching the surface.
    [Fact]
    public void SessionSearchRejectsInvalidDirection()
    {
        var response = SendCommand("""{"cmd":"session.search","target":"active","args":{"to":"sideways"}}""");
        Assert.False(response["ok"]!.GetValue<bool>(), $"an invalid --to should fail: {response.ToJsonString()}");
        Assert.Equal("session.search --to must be next|prev|close", response["error"]!.GetValue<string>());
    }

    // an EMPTY needle clears the query: ok with NO count, and a subsequent non-empty query must
    // still find matches (the empty needle left a clean searchable state).
    [Fact]
    public void SessionSearchEmptyNeedleClearsThenRecovers()
    {
        const string needle = "agtermCLEARME";
        var surface = Attach(Host, ActiveSessionId());
        var typed = SendCommand(TypeRequest($"echo {needle} {needle}\n", target: null, select: false));
        Assert.True(typed["ok"]!.GetValue<bool>(), $"typing the needle should succeed: {typed.ToJsonString()}");
        surface.ScreenText = $"$ echo {needle} {needle}\n{needle} {needle}\n";

        _ = SettledSearchCount(needle); // open + settle a real count first

        // clear the query with an empty needle: ok, and no count in the result (counter blanks).
        var cleared = SendCommand("""{"cmd":"session.search","target":"active","args":{"text":""}}""");
        Assert.True(cleared["ok"]!.GetValue<bool>(), $"an empty needle should succeed (clears the query): {cleared.ToJsonString()}");
        var clearedResult = Assert.IsType<JsonObject>(cleared["result"]);
        Assert.Null(clearedResult["count"]);

        // re-query the same needle: it must find matches again (the clear didn't break search).
        var recovered = SettledSearchCount(needle);
        Assert.True(recovered >= 1, "search must still find the needle after an empty-needle clear");
        SendCloseSearch();
    }

    /// <summary>The 1-based selected index from a "S of N" display string (null for "M matches" /
    /// "no matches" / other shapes).</summary>
    private static int? SelectedIndex(string? display)
    {
        if (display is null) return null;
        var ofIndex = display.IndexOf(" of ", StringComparison.Ordinal);
        if (ofIndex < 0) return null;
        return int.TryParse(display[..ofIndex].Trim(), out var index) ? index : null;
    }

    // session.go navigates the selection in the sidebar's flattened order and returns the
    // newly-selected id.
    [Fact]
    public void SessionGoNavigatesSelection()
    {
        const string firstId = "EEEE0000-0000-0000-0000-000000000001";
        const string secondId = "FFFF0000-0000-0000-0000-000000000002";
        var snapshot = $$$"""{"version":1,"selectedSessionID":"{{{firstId}}}","workspaces":[{"id":"{{{NewId()}}}","name":"workspace 1","sessions":[{{{SessionJson(firstId)}}},{{{SessionJson(secondId)}}}]}]}""";
        var host = Relaunch(snapshot);

        // next: first -> second; the response carries the second's id and it becomes active.
        var next = SendTo(host, """{"cmd":"session.go","args":{"to":"next"}}""");
        Assert.True(next["ok"]!.GetValue<bool>(), $"session.go next should succeed: {next.ToJsonString()}");
        Assert.Equal(secondId.ToLowerInvariant(), next["result"]!["id"]!.GetValue<string>().ToLowerInvariant());
        Assert.Equal(secondId.ToLowerInvariant(), ActiveSessionIdIn(SendTo(host, """{"cmd":"tree"}""")).ToLowerInvariant());

        // first: jumps to the first session.
        var first = SendTo(host, """{"cmd":"session.go","args":{"to":"first"}}""");
        Assert.True(first["ok"]!.GetValue<bool>(), $"session.go first should succeed: {first.ToJsonString()}");
        Assert.Equal(firstId.ToLowerInvariant(), first["result"]!["id"]!.GetValue<string>().ToLowerInvariant());
        Assert.Equal(firstId.ToLowerInvariant(), ActiveSessionIdIn(SendTo(host, """{"cmd":"tree"}""")).ToLowerInvariant());

        // last: jumps to the last (second) session.
        var last = SendTo(host, """{"cmd":"session.go","args":{"to":"last"}}""");
        Assert.True(last["ok"]!.GetValue<bool>(), $"session.go last should succeed: {last.ToJsonString()}");
        Assert.Equal(secondId.ToLowerInvariant(), last["result"]!["id"]!.GetValue<string>().ToLowerInvariant());
        Assert.Equal(secondId.ToLowerInvariant(), ActiveSessionIdIn(SendTo(host, """{"cmd":"tree"}""")).ToLowerInvariant());
    }

    // session.go with an unknown direction returns the structured guard.
    [Fact]
    public void SessionGoInvalidDirectionErrors()
    {
        var response = SendCommand("""{"cmd":"session.go","args":{"to":"sideways"}}""");
        Assert.False(response["ok"]!.GetValue<bool>(), $"an invalid direction should fail: {response.ToJsonString()}");
        Assert.Equal("session.go requires --to next|prev|first|last|next-attention|prev-attention",
            response["error"]!.GetValue<string>());
    }

    // session.go next-attention steps only through sessions needing attention (blocked or
    // completed), wrapping.
    [Fact]
    public void SessionGoNavigatesAttentionSessions()
    {
        const string firstId = "AAAA0000-0000-0000-0000-000000000001";
        const string secondId = "BBBB0000-0000-0000-0000-000000000002";
        const string thirdId = "CCCC0000-0000-0000-0000-000000000003";
        var host = Relaunch(ThreeSessionSnapshot(firstId, secondId, thirdId));

        // mark the 2nd blocked and the 3rd completed; the selected 1st stays idle.
        var s2 = SendTo(host, $$$"""{"cmd":"session.status","target":"{{{secondId}}}","args":{"status":"blocked"}}""");
        Assert.True(s2["ok"]!.GetValue<bool>(), $"set blocked status: {s2.ToJsonString()}");
        var s3 = SendTo(host, $$$"""{"cmd":"session.status","target":"{{{thirdId}}}","args":{"status":"completed"}}""");
        Assert.True(s3["ok"]!.GetValue<bool>(), $"set completed status: {s3.ToJsonString()}");

        // next-attention from the idle first session skips to the blocked second.
        var n1 = SendTo(host, """{"cmd":"session.go","args":{"to":"next-attention"}}""");
        Assert.True(n1["ok"]!.GetValue<bool>(), $"next-attention should succeed: {n1.ToJsonString()}");
        Assert.Equal(secondId.ToLowerInvariant(), n1["result"]!["id"]!.GetValue<string>().ToLowerInvariant());
        Assert.Equal(secondId.ToLowerInvariant(), ActiveSessionIdIn(SendTo(host, """{"cmd":"tree"}""")).ToLowerInvariant());

        // again -> the completed third.
        var n2 = SendTo(host, """{"cmd":"session.go","args":{"to":"next-attention"}}""");
        Assert.Equal(thirdId.ToLowerInvariant(), n2["result"]!["id"]!.GetValue<string>().ToLowerInvariant());
        Assert.Equal(thirdId.ToLowerInvariant(), ActiveSessionIdIn(SendTo(host, """{"cmd":"tree"}""")).ToLowerInvariant());

        // wraps forward back to the blocked second.
        var n3 = SendTo(host, """{"cmd":"session.go","args":{"to":"next-attention"}}""");
        Assert.Equal(secondId.ToLowerInvariant(), n3["result"]!["id"]!.GetValue<string>().ToLowerInvariant());
        Assert.Equal(secondId.ToLowerInvariant(), ActiveSessionIdIn(SendTo(host, """{"cmd":"tree"}""")).ToLowerInvariant());
    }

    // quick toggle shows the quick terminal, and toggling again hides it. The AX element is UI-only;
    // the tree's quickVisible flag is the headless read-back.
    [Fact]
    public void QuickTerminalToggle()
    {
        Assert.False(QuickVisible(), "quick terminal should start hidden");

        var shown = SendCommand("""{"cmd":"quick","args":{"mode":"toggle"}}""");
        Assert.True(shown["ok"]!.GetValue<bool>(), $"quick toggle should succeed: {shown.ToJsonString()}");
        Assert.True(QuickVisible(), "quick terminal should appear");

        var hidden = SendCommand("""{"cmd":"quick","args":{"mode":"hide"}}""");
        Assert.True(hidden["ok"]!.GetValue<bool>(), $"quick hide should succeed: {hidden.ToJsonString()}");
        Assert.False(QuickVisible(), "quick terminal should hide");
    }

    // font.inc on the realized active session returns ok.
    [Fact]
    public void FontIncreaseSucceeds()
    {
        Attach(Host, ActiveSessionId()); // the realized surface
        var response = SendCommand("""{"cmd":"font.inc","target":"active"}""");
        Assert.True(response["ok"]!.GetValue<bool>(), $"font.inc on the active session should succeed: {response.ToJsonString()}");
    }

    // an invalid mode returns an error and does NOT flip state.
    [Fact]
    public void InvalidQuickModeErrors()
    {
        Assert.False(QuickVisible(), "quick terminal should start hidden");

        var response = SendCommand("""{"cmd":"quick","args":{"mode":"bogus"}}""");
        Assert.False(response["ok"]!.GetValue<bool>(), "an invalid quick mode should fail");
        var error = response["error"]!.GetValue<string>();
        Assert.Contains("invalid quick mode", error);
        // state must not have flipped.
        Assert.False(QuickVisible(), "an invalid mode must leave the quick terminal hidden");
    }

    // quick.type before the overlay has ever been shown errors (not a silent drop); once shown, a
    // typed marker reads back off the quick terminal's own buffer via quick.text.
    [Fact]
    public void QuickTypeAndReadText()
    {
        Assert.False(QuickVisible(), "quick terminal should start hidden");

        var closed = SendCommand("""{"cmd":"quick.type","args":{"text":"x"}}""");
        Assert.False(closed["ok"]!.GetValue<bool>(), $"quick.type before show should fail: {closed.ToJsonString()}");
        Assert.Equal("quick terminal not open", closed["error"]!.GetValue<string>());

        var shown = SendCommand("""{"cmd":"quick","args":{"mode":"show"}}""");
        Assert.True(shown["ok"]!.GetValue<bool>(), $"quick show should succeed: {shown.ToJsonString()}");
        // the show mounts + realizes the quick surface (attached externally headless — upstream's
        // server-side realize poll has no async mount to ride out here).
        var surface = new FakeSurface();
        Host.Actions.Quick.Surface = surface;
        var typed = SendCommand("""{"cmd":"quick.type","args":{"text":"QUICKPROBE"}}""");
        Assert.True(typed["ok"]!.GetValue<bool>(), $"quick.type right after quick show must land: {typed.ToJsonString()}");
        Assert.Contains("QUICKPROBE", surface.Injected);

        // read the typed marker back off the quick surface (the shell echo on the prompt line).
        surface.ScreenText = "$ QUICKPROBE";
        var response = SendCommand("""{"cmd":"quick.text","args":{"all":true}}""");
        Assert.True(response["ok"]!.GetValue<bool>(), $"quick.text should succeed: {response.ToJsonString()}");
        Assert.Contains("QUICKPROBE", response["result"]!["text"]!.GetValue<string>());
    }

    // session.select by a UNIQUE prefix of a session id resolves to that session.
    [Fact]
    public void SessionSelectByUniquePrefix()
    {
        const string firstId = "AAAA0000-0000-0000-0000-000000000001";
        const string secondId = "BBBB0000-0000-0000-0000-000000000002";
        var snapshot = $$$"""{"version":1,"selectedSessionID":"{{{firstId}}}","workspaces":[{"id":"{{{NewId()}}}","name":"workspace 1","sessions":[{{{SessionJson(firstId)}}},{{{SessionJson(secondId)}}}]}]}""";
        var host = Relaunch(snapshot);

        // "bbbb" is unique to the second session.
        var response = SendTo(host, """{"cmd":"session.select","target":"bbbb"}""");
        Assert.True(response["ok"]!.GetValue<bool>(), $"select by unique prefix should succeed: {response.ToJsonString()}");
        Assert.Equal(secondId.ToLowerInvariant(),
            response["result"]!["id"]!.GetValue<string>().ToLowerInvariant());
        Assert.Equal(secondId.ToLowerInvariant(), ActiveSessionIdIn(SendTo(host, """{"cmd":"tree"}""")).ToLowerInvariant());
    }

    // an ambiguous-prefix request returns the `ambiguous` error listing the candidate ids and
    // changes nothing.
    [Fact]
    public void SessionSelectAmbiguousPrefixErrors()
    {
        const string firstId = "ABCD1111-0000-0000-0000-000000000001";
        const string secondId = "ABCD2222-0000-0000-0000-000000000002";
        var snapshot = $$$"""{"version":1,"selectedSessionID":"{{{firstId}}}","workspaces":[{"id":"{{{NewId()}}}","name":"workspace 1","sessions":[{{{SessionJson(firstId)}}},{{{SessionJson(secondId)}}}]}]}""";
        var host = Relaunch(snapshot);

        // "abcd" matches both sessions.
        var response = SendTo(host, """{"cmd":"session.select","target":"abcd"}""");
        Assert.False(response["ok"]!.GetValue<bool>(), "an ambiguous prefix should fail");
        Assert.Equal("ambiguous session prefix 'abcd' → ABCD1111, ABCD2222",
            response["error"]!.GetValue<string>());
        // selection must be unchanged (the originally-selected first session stays active).
        Assert.Equal(firstId.ToLowerInvariant(), ActiveSessionIdIn(SendTo(host, """{"cmd":"tree"}""")).ToLowerInvariant());
    }

    // `active` targeting with no explicit id works end-to-end: session.rename renames the currently
    // selected session, verified via the tree read-back.
    [Fact]
    public void ActiveTargetingWithNoExplicitID()
    {
        var response = SendCommand("""{"cmd":"session.rename","args":{"name":"active-renamed"}}""");
        Assert.True(response["ok"]!.GetValue<bool>(), $"rename of the active session should succeed: {response.ToJsonString()}");
        var tree = SendCommand("""{"cmd":"tree"}""");
        var sessions = (JsonArray)tree["result"]!["tree"]!["workspaces"]![0]!["sessions"]!;
        Assert.Equal("active-renamed", sessions[0]!["name"]!.GetValue<string>());
    }

    // session.move relocates a session to another workspace.
    [Fact]
    public void SessionMoveToAnotherWorkspace()
    {
        var created = SendCommand("""{"cmd":"workspace.new","args":{"name":"dest ws"}}""");
        var destId = created["result"]!["id"]!.GetValue<string>();
        Assert.Equal(new[] { "workspace 1", "dest ws" }, WorkspaceNames(SendCommand("""{"cmd":"tree"}""")));

        // move the active (seeded) session into the new workspace.
        var moved = SendCommand($$$"""{"cmd":"session.move","target":"active","args":{"workspace":"{{{destId}}}"}}""");
        Assert.True(moved["ok"]!.GetValue<bool>(), $"session.move should succeed: {moved.ToJsonString()}");
        Assert.Equal(new[] { 0, 1 }, SessionCounts(SendCommand("""{"cmd":"tree"}""")));
    }

    // session.move with neither --to nor a workspace returns the structured missing-arg guard.
    [Fact]
    public void SessionMoveRequiresWorkspace()
    {
        var response = SendCommand("""{"cmd":"session.move","target":"active"}""");
        Assert.False(response["ok"]!.GetValue<bool>(), "session.move without --to or a workspace should fail");
        Assert.Equal("session.move requires --to or a workspace", response["error"]!.GetValue<string>());
    }

    // session.move with BOTH --to and a workspace is ambiguous and returns the either/or guard.
    [Fact]
    public void SessionMoveBothToAndWorkspaceErrors()
    {
        var response = SendCommand("""{"cmd":"session.move","target":"active","args":{"to":"up","workspace":"active"}}""");
        Assert.False(response["ok"]!.GetValue<bool>(), "session.move with both --to and a workspace should fail");
        Assert.Equal("session.move takes either --to or a workspace, not both", response["error"]!.GetValue<string>());
    }

    // session.move with an invalid --to direction returns the direction guard.
    [Fact]
    public void SessionMoveInvalidDirectionErrors()
    {
        var response = SendCommand("""{"cmd":"session.move","target":"active","args":{"to":"sideways"}}""");
        Assert.False(response["ok"]!.GetValue<bool>(), $"an invalid direction should fail: {response.ToJsonString()}");
        Assert.Equal("session.move --to must be up|down|top|bottom", response["error"]!.GetValue<string>());
    }

    // workspace.move without --to returns the structured missing-arg guard.
    [Fact]
    public void WorkspaceMoveRequiresTo()
    {
        var response = SendCommand("""{"cmd":"workspace.move","target":"active"}""");
        Assert.False(response["ok"]!.GetValue<bool>(), "workspace.move without --to should fail");
        Assert.Equal("workspace.move requires --to", response["error"]!.GetValue<string>());
    }

    // workspace.move with an invalid --to direction returns the direction guard.
    [Fact]
    public void WorkspaceMoveInvalidDirectionErrors()
    {
        var response = SendCommand("""{"cmd":"workspace.move","target":"active","args":{"to":"sideways"}}""");
        Assert.False(response["ok"]!.GetValue<bool>(), $"an invalid direction should fail: {response.ToJsonString()}");
        Assert.Equal("workspace.move --to must be up|down|top|bottom", response["error"]!.GetValue<string>());
    }

    // session.move --to reorders a session within its own workspace.
    [Fact]
    public void SessionMoveReorderWithinWorkspace()
    {
        const string firstId = "A1110000-0000-0000-0000-000000000001";
        const string secondId = "A2220000-0000-0000-0000-000000000002";
        const string thirdId = "A3330000-0000-0000-0000-000000000003";
        var host = Relaunch(ThreeSessionSnapshot(firstId, secondId, thirdId));
        Assert.Equal(new[] { firstId, secondId, thirdId }, SessionOrder(SendTo(host, """{"cmd":"tree"}""")));

        // move the third session up one step: [first, second, third] -> [first, third, second].
        var up = SendTo(host, $$$"""{"cmd":"session.move","target":"{{{thirdId}}}","args":{"to":"up"}}""");
        Assert.True(up["ok"]!.GetValue<bool>(), $"session.move --to up should succeed: {up.ToJsonString()}");
        Assert.Equal(new[] { firstId, thirdId, secondId }, SessionOrder(SendTo(host, """{"cmd":"tree"}""")));

        // bottom then top to prove a non-trivial reorder ending back at index 0.
        var bottom = SendTo(host, $$$"""{"cmd":"session.move","target":"{{{firstId}}}","args":{"to":"bottom"}}""");
        Assert.True(bottom["ok"]!.GetValue<bool>(), $"session.move --to bottom should succeed: {bottom.ToJsonString()}");
        Assert.Equal(new[] { thirdId, secondId, firstId }, SessionOrder(SendTo(host, """{"cmd":"tree"}""")));

        var top = SendTo(host, $$$"""{"cmd":"session.move","target":"{{{firstId}}}","args":{"to":"top"}}""");
        Assert.True(top["ok"]!.GetValue<bool>(), $"session.move --to top should succeed: {top.ToJsonString()}");
        Assert.Equal(new[] { firstId, thirdId, secondId }, SessionOrder(SendTo(host, """{"cmd":"tree"}""")));
    }

    // session.move --after/--before repositions a session within its own workspace in one round-trip.
    [Fact]
    public void SessionMovePlaceWithinWorkspace()
    {
        const string firstId = "B1110000-0000-0000-0000-000000000001";
        const string secondId = "B2220000-0000-0000-0000-000000000002";
        const string thirdId = "B3330000-0000-0000-0000-000000000003";
        var host = Relaunch(ThreeSessionSnapshot(firstId, secondId, thirdId));
        Assert.Equal(new[] { firstId, secondId, thirdId }, SessionOrder(SendTo(host, """{"cmd":"tree"}""")));

        // place the third session right after the first: [first, second, third] -> [first, third, second].
        var after = SendTo(host, $$$"""{"cmd":"session.move","target":"{{{thirdId}}}","args":{"after":"{{{firstId}}}"}}""");
        Assert.True(after["ok"]!.GetValue<bool>(), $"session.move --after should succeed: {after.ToJsonString()}");
        Assert.Equal(new[] { firstId, thirdId, secondId }, SessionOrder(SendTo(host, """{"cmd":"tree"}""")));

        // place the first session right before the second: [first, third, second] -> [third, first, second].
        var before = SendTo(host, $$$"""{"cmd":"session.move","target":"{{{firstId}}}","args":{"before":"{{{secondId}}}"}}""");
        Assert.True(before["ok"]!.GetValue<bool>(), $"session.move --before should succeed: {before.ToJsonString()}");
        Assert.Equal(new[] { thirdId, firstId, secondId }, SessionOrder(SendTo(host, """{"cmd":"tree"}""")));
    }

    // session.move with multiple targets removes the whole block first, then inserts it at the
    // post-removal slot.
    [Fact]
    public void SessionMoveMultipleTargetsWithinWorkspaceBeforeAnchor()
    {
        const string firstId = "BB100000-0000-0000-0000-000000000001";
        const string secondId = "BB200000-0000-0000-0000-000000000002";
        const string thirdId = "BB300000-0000-0000-0000-000000000003";
        const string fourthId = "BB400000-0000-0000-0000-000000000004";
        const string fifthId = "BB500000-0000-0000-0000-000000000005";
        var snapshot = $$$"""{"version":1,"selectedSessionID":"{{{firstId}}}","workspaces":[{"id":"{{{NewId()}}}","name":"workspace 1","sessions":[{{{SessionJson(firstId)}}},{{{SessionJson(secondId)}}},{{{SessionJson(thirdId)}}},{{{SessionJson(fourthId)}}},{{{SessionJson(fifthId)}}}]}]}""";
        var host = Relaunch(snapshot);

        var moved = SendTo(host, $$$"""{"cmd":"session.move","args":{"targets":["{{{firstId}}}","{{{secondId}}}"],"before":"{{{fifthId}}}"}}""");
        Assert.True(moved["ok"]!.GetValue<bool>(), $"batch session.move --before should succeed: {moved.ToJsonString()}");
        Assert.Equal(2, moved["result"]!["affected"]!.GetValue<int>());
        Assert.Equal(new[] { thirdId, fourthId, firstId, secondId, fifthId },
            SessionOrder(SendTo(host, """{"cmd":"tree"}""")));
    }

    // A destination member in a multi-target request stays in place and is not counted. A
    // one-element args.targets request remains wire-equivalent to singular move.
    [Fact]
    public void SessionMoveBatchReportsActualAffectedAndNormalizesOneTarget()
    {
        const string wsOneId = "BC100000-0000-0000-0000-000000000001";
        const string wsTwoId = "BC200000-0000-0000-0000-000000000002";
        const string aId = "BC300000-0000-0000-0000-000000000003";
        const string bId = "BC400000-0000-0000-0000-000000000004";
        const string cId = "BC500000-0000-0000-0000-000000000005";
        var snapshot = $$$"""{"version":1,"selectedSessionID":"{{{aId}}}","workspaces":[{"id":"{{{wsOneId}}}","name":"one","sessions":[{{{SessionJson(aId)}}}]},{"id":"{{{wsTwoId}}}","name":"two","sessions":[{{{SessionJson(bId)}}},{{{SessionJson(cId)}}}]}]}""";
        var host = Relaunch(snapshot);

        var batch = SendTo(host, $$$"""{"cmd":"session.move","args":{"targets":["{{{aId}}}","{{{bId}}}"],"workspace":"{{{wsTwoId}}}"}}""");
        Assert.True(batch["ok"]!.GetValue<bool>(), $"mixed batch move should succeed: {batch.ToJsonString()}");
        Assert.Equal(1, batch["result"]!["affected"]!.GetValue<int>());
        Assert.Equal(new[] { bId, cId, aId }, SessionOrder(SendTo(host, """{"cmd":"tree"}"""), 1));

        var oneTarget = SendTo(host, $$$"""{"cmd":"session.move","args":{"targets":["{{{bId}}}"],"workspace":"{{{wsTwoId}}}"}}""");
        Assert.True(oneTarget["ok"]!.GetValue<bool>(), $"one-target array move should succeed: {oneTarget.ToJsonString()}");
        Assert.Equal(bId, oneTarget["result"]!["id"]!.GetValue<string>());
        Assert.Null(oneTarget["result"]!["affected"]);
        Assert.Equal(new[] { cId, aId, bId }, SessionOrder(SendTo(host, """{"cmd":"tree"}"""), 1));
    }

    // session.move --after with an anchor in ANOTHER workspace relocates the session to the anchor's
    // workspace AND positions it at the right slot in one round-trip.
    [Fact]
    public void SessionMovePlaceCrossWorkspace()
    {
        const string aId = "C1110000-0000-0000-0000-000000000001";
        const string bId = "C2220000-0000-0000-0000-000000000002";
        const string xId = "C3330000-0000-0000-0000-000000000003";
        const string yId = "C4440000-0000-0000-0000-000000000004";
        var snapshot = $$$"""{"version":1,"selectedSessionID":"{{{aId}}}","workspaces":[{"id":"{{{NewId()}}}","name":"ws one","sessions":[{{{SessionJson(aId)}}},{{{SessionJson(bId)}}}]},{"id":"{{{NewId()}}}","name":"ws two","sessions":[{{{SessionJson(xId)}}},{{{SessionJson(yId)}}}]}]}""";
        var host = Relaunch(snapshot);
        Assert.Equal(new[] { 2, 2 }, SessionCounts(SendTo(host, """{"cmd":"tree"}""")));

        // move B (in ws one) right after X (in ws two): ws one -> [A], ws two -> [X, B, Y].
        var moved = SendTo(host, $$$"""{"cmd":"session.move","target":"{{{bId}}}","args":{"after":"{{{xId}}}"}}""");
        Assert.True(moved["ok"]!.GetValue<bool>(), $"cross-workspace session.move --after should succeed: {moved.ToJsonString()}");
        Assert.Equal(new[] { 1, 3 }, SessionCounts(SendTo(host, """{"cmd":"tree"}""")));
        Assert.Equal(new[] { xId, bId, yId }, SessionOrder(SendTo(host, """{"cmd":"tree"}"""), 1));
    }

    // session.new --after/--before creates a session at the chosen slot in one round-trip.
    [Fact]
    public void SessionNewPlaceRelativeToAnchor()
    {
        const string firstId = "D1110000-0000-0000-0000-000000000001";
        const string secondId = "D2220000-0000-0000-0000-000000000002";
        const string thirdId = "D3330000-0000-0000-0000-000000000003";
        var host = Relaunch(ThreeSessionSnapshot(firstId, secondId, thirdId));
        Assert.Equal(new[] { firstId, secondId, thirdId }, SessionOrder(SendTo(host, """{"cmd":"tree"}""")));

        // create right after the first session: [first, NEW, second, third].
        var afterResp = SendTo(host, $$$"""{"cmd":"session.new","args":{"after":"{{{firstId}}}"}}""");
        Assert.True(afterResp["ok"]!.GetValue<bool>(), $"session.new --after should succeed: {afterResp.ToJsonString()}");
        var afterId = afterResp["result"]!["id"]!.GetValue<string>().ToUpperInvariant();
        Assert.Equal(new[] { firstId, afterId, secondId, thirdId },
            SessionOrder(SendTo(host, """{"cmd":"tree"}""")));

        // create right before the third session: [first, NEW1, second, NEW2, third].
        var beforeResp = SendTo(host, $$$"""{"cmd":"session.new","args":{"before":"{{{thirdId}}}"}}""");
        Assert.True(beforeResp["ok"]!.GetValue<bool>(), $"session.new --before should succeed: {beforeResp.ToJsonString()}");
        var beforeId = beforeResp["result"]!["id"]!.GetValue<string>().ToUpperInvariant();
        Assert.Equal(new[] { firstId, afterId, secondId, beforeId, thirdId },
            SessionOrder(SendTo(host, """{"cmd":"tree"}""")));
    }

    // session.new --after with an anchor in ANOTHER workspace creates the new session in the
    // anchor's workspace (not the active one) at the right slot.
    [Fact]
    public void SessionNewPlaceCrossWorkspace()
    {
        const string aId = "E1110000-0000-0000-0000-000000000001";
        const string bId = "E2220000-0000-0000-0000-000000000002";
        const string xId = "E3330000-0000-0000-0000-000000000003";
        const string yId = "E4440000-0000-0000-0000-000000000004";
        var snapshot = $$$"""{"version":1,"selectedSessionID":"{{{aId}}}","workspaces":[{"id":"{{{NewId()}}}","name":"ws one","sessions":[{{{SessionJson(aId)}}},{{{SessionJson(bId)}}}]},{"id":"{{{NewId()}}}","name":"ws two","sessions":[{{{SessionJson(xId)}}},{{{SessionJson(yId)}}}]}]}""";
        var host = Relaunch(snapshot);
        Assert.Equal(new[] { 2, 2 }, SessionCounts(SendTo(host, """{"cmd":"tree"}""")));

        var resp = SendTo(host, $$$"""{"cmd":"session.new","args":{"after":"{{{xId}}}"}}""");
        Assert.True(resp["ok"]!.GetValue<bool>(), $"cross-workspace session.new --after should succeed: {resp.ToJsonString()}");
        var newId = resp["result"]!["id"]!.GetValue<string>().ToUpperInvariant();
        Assert.Equal(new[] { 2, 3 }, SessionCounts(SendTo(host, """{"cmd":"tree"}""")));
        Assert.Equal(new[] { xId, newId, yId }, SessionOrder(SendTo(host, """{"cmd":"tree"}"""), 1));
    }

    // session.move rejects --after + --before together with the either/or guard.
    [Fact]
    public void SessionMovePlaceRejectsAfterAndBefore()
    {
        var response = SendCommand("""{"cmd":"session.move","target":"active","args":{"after":"active","before":"active"}}""");
        Assert.False(response["ok"]!.GetValue<bool>(), "--after + --before should fail");
        Assert.Equal("use either --after or --before, not both", response["error"]!.GetValue<string>());
    }

    // session.move rejects a placement anchor combined with --to.
    [Fact]
    public void SessionMovePlaceRejectsAfterAndTo()
    {
        var response = SendCommand("""{"cmd":"session.move","target":"active","args":{"after":"active","to":"up"}}""");
        Assert.False(response["ok"]!.GetValue<bool>(), "--after + --to should fail");
        Assert.Equal("session.move takes --after/--before or --to, not both", response["error"]!.GetValue<string>());
    }

    // session.move rejects a placement anchor combined with a workspace.
    [Fact]
    public void SessionMovePlaceRejectsAfterAndWorkspace()
    {
        var response = SendCommand("""{"cmd":"session.move","target":"active","args":{"after":"active","workspace":"active"}}""");
        Assert.False(response["ok"]!.GetValue<bool>(), "--after + a workspace should fail");
        Assert.Equal("session.move takes --after/--before or a workspace, not both", response["error"]!.GetValue<string>());
    }

    // session.new rejects --after + --before together, and a placement anchor with a workspace.
    [Fact]
    public void SessionNewPlaceRejectsConflicts()
    {
        var both = SendCommand("""{"cmd":"session.new","args":{"after":"active","before":"active"}}""");
        Assert.False(both["ok"]!.GetValue<bool>(), "--after + --before should fail");
        Assert.Equal("use either --after or --before, not both", both["error"]!.GetValue<string>());

        var withWorkspace = SendCommand("""{"cmd":"session.new","args":{"after":"active","workspace":"active"}}""");
        Assert.False(withWorkspace["ok"]!.GetValue<bool>(), "--after + a workspace should fail");
        Assert.Equal("session.new takes --after/--before or a workspace, not both",
            withWorkspace["error"]!.GetValue<string>());
    }

    // workspace.move --to reorders a workspace among its siblings.
    [Fact]
    public void WorkspaceMoveReorder()
    {
        var snapshot = $$$"""{"version":1,"selectedSessionID":null,"workspaces":[{"id":"{{{NewId()}}}","name":"alpha","sessions":[{{{SessionJson(NewId())}}}]},{"id":"{{{NewId()}}}","name":"beta","sessions":[{{{SessionJson(NewId())}}}]},{"id":"{{{NewId()}}}","name":"gamma","sessions":[{{{SessionJson(NewId())}}}]}]}""";
        var host = Relaunch(snapshot);
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, WorkspaceNames(SendTo(host, """{"cmd":"tree"}""")));

        // capture gamma's id (the last workspace) from the tree, then move it to the top.
        var tree = SendTo(host, """{"cmd":"tree"}""");
        var workspaces = (JsonArray)tree["result"]!["tree"]!["workspaces"]!;
        var gammaId = workspaces.First(w => w!["name"]!.GetValue<string>() == "gamma")!["id"]!.GetValue<string>();

        var top = SendTo(host, $$$"""{"cmd":"workspace.move","target":"{{{gammaId}}}","args":{"to":"top"}}""");
        Assert.True(top["ok"]!.GetValue<bool>(), $"workspace.move --to top should succeed: {top.ToJsonString()}");
        Assert.Equal(new[] { "gamma", "alpha", "beta" }, WorkspaceNames(SendTo(host, """{"cmd":"tree"}""")));

        // move gamma down one step: [gamma, alpha, beta] -> [alpha, gamma, beta].
        var down = SendTo(host, $$$"""{"cmd":"workspace.move","target":"{{{gammaId}}}","args":{"to":"down"}}""");
        Assert.True(down["ok"]!.GetValue<bool>(), $"workspace.move --to down should succeed: {down.ToJsonString()}");
        Assert.Equal(new[] { "alpha", "gamma", "beta" }, WorkspaceNames(SendTo(host, """{"cmd":"tree"}""")));
    }

    // session.rename with no name arg returns the structured missing-arg guard.
    [Fact]
    public void SessionRenameRequiresName()
    {
        var response = SendCommand("""{"cmd":"session.rename","target":"active"}""");
        Assert.False(response["ok"]!.GetValue<bool>(), "session.rename without a name should fail");
        Assert.Equal("session.rename requires a name", response["error"]!.GetValue<string>());
    }

    // workspace.select selects a workspace's first session.
    [Fact]
    public void WorkspaceSelect()
    {
        const string firstId = "CCCC0000-0000-0000-0000-000000000001";
        const string secondId = "DDDD0000-0000-0000-0000-000000000002";
        var secondWorkspaceId = NewId();
        var snapshot = $$$"""{"version":1,"selectedSessionID":"{{{firstId}}}","workspaces":[{"id":"{{{NewId()}}}","name":"workspace 1","sessions":[{{{SessionJson(firstId)}}}]},{"id":"{{{secondWorkspaceId}}}","name":"workspace 2","sessions":[{{{SessionJson(secondId)}}}]}]}""";
        var host = Relaunch(snapshot);

        var response = SendTo(host, $$$"""{"cmd":"workspace.select","target":"{{{secondWorkspaceId}}}"}""");
        Assert.True(response["ok"]!.GetValue<bool>(), $"workspace.select should succeed: {response.ToJsonString()}");
        Assert.Equal(secondId.ToLowerInvariant(), ActiveSessionIdIn(SendTo(host, """{"cmd":"tree"}""")).ToLowerInvariant());
    }

    // font.dec and font.reset on the realized active session return ok.
    [Fact]
    public void FontDecreaseAndResetSucceed()
    {
        Attach(Host, ActiveSessionId()); // the realized surface

        var dec = SendCommand("""{"cmd":"font.dec","target":"active"}""");
        Assert.True(dec["ok"]!.GetValue<bool>(), $"font.dec on the active session should succeed: {dec.ToJsonString()}");

        var reset = SendCommand("""{"cmd":"font.reset","target":"active"}""");
        Assert.True(reset["ok"]!.GetValue<bool>(), $"font.reset on the active session should succeed: {reset.ToJsonString()}");
    }

    // MARK: - Keymap

    // keymap.reload re-reads keymap.conf and returns the parse-diagnostic count. With no keymap file
    // seeded, a reload reports zero diagnostics.
    [Fact]
    public void KeymapReloadReportsZeroDiagnostics()
    {
        var response = SendCommand("""{"cmd":"keymap.reload"}""");
        Assert.True(response["ok"]!.GetValue<bool>(), $"keymap.reload should succeed: {response.ToJsonString()}");
        Assert.Equal(0, response["result"]!["count"]!.GetValue<int>());
    }

    // a keymap.conf with a broken line surfaces in the diagnostic count keymap.reload returns.
    // Upstream relaunches so the auto-created starter isn't written over the broken file; the
    // headless host creates no starter and re-reads the file on every reload, so seeding suffices.
    [Fact]
    public void KeymapReloadReportsDiagnosticsForBrokenFile()
    {
        File.WriteAllText(Path.Combine(StateDir, "keymap.conf"), "bogus verb here\n");
        var response = SendCommand("""{"cmd":"keymap.reload"}""");
        Assert.True(response["ok"]!.GetValue<bool>(), $"keymap.reload should succeed even with a broken file: {response.ToJsonString()}");
        var count = response["result"]!["count"]!.GetValue<int>();
        Assert.True(count >= 1, $"a broken keymap line should yield at least one diagnostic: {response.ToJsonString()}");
    }

    // MARK: - Config

    // config.reload re-reads the config and returns the config-diagnostic count. Assert ok + a
    // count present rather than count==0 (upstream's count merges the host machine's own config).
    [Fact]
    public void ConfigReloadSucceeds()
    {
        var response = SendCommand("""{"cmd":"config.reload"}""");
        Assert.True(response["ok"]!.GetValue<bool>(), $"config.reload should succeed: {response.ToJsonString()}");
        Assert.NotNull(response["result"]!["count"]);
        _ = response["result"]!["count"]!.GetValue<int>();
    }

    // a malformed config line surfaces in the diagnostic count config.reload returns. Engine-less
    // adaptation (PLATFORM-NOTES): the headless host has no ghostty engine to feed a malformed
    // ghostty.conf into — its diagnostic count is host-injectable, so the malformed file's one
    // deterministic diagnostic is modeled by ConfigReload = () => 1 and the test pins the wire
    // plumbing (the count rides result.count).
    [Fact]
    public void ConfigReloadReportsDiagnosticsForMalformedFile()
    {
        var host = Relaunch(options: new HeadlessHostOptions { ConfigReload = () => 1 });
        var response = SendTo(host, """{"cmd":"config.reload"}""");
        Assert.True(response["ok"]!.GetValue<bool>(), $"config.reload should succeed even with a malformed file: {response.ToJsonString()}");
        var count = response["result"]!["count"]!.GetValue<int>();
        Assert.True(count >= 1, $"a malformed config line should yield at least one diagnostic: {response.ToJsonString()}");
    }

    [Fact]
    public void ThemeListAndSet()
    {
        // the bundled theme set is host-supplied headless (no app bundle to enumerate); a fresh
        // install seeds the agterm theme as the default/current, modeled on the theme slots.
        var host = Relaunch(options: new HeadlessHostOptions
        {
            AvailableThemes = new[] { "agterm", "Dracula" },
        });
        host.Actions.Theme.Light = "agterm";

        // list: a non-empty set of bundled themes, including the repo's own "agterm" theme.
        var listed = SendTo(host, """{"cmd":"theme.list"}""");
        Assert.True(listed["ok"]!.GetValue<bool>(), $"theme.list should succeed: {listed.ToJsonString()}");
        var themes = ((JsonArray)listed["result"]!["themes"]!).Select(t => t!.GetValue<string>()).ToArray();
        Assert.Contains("agterm", themes);
        Assert.Equal("agterm", listed["result"]!["theme"]!.GetValue<string>());

        // set a different known theme and get it echoed back.
        var set = SendTo(host, """{"cmd":"theme.set","args":{"name":"Dracula"}}""");
        Assert.True(set["ok"]!.GetValue<bool>(), $"theme.set should succeed: {set.ToJsonString()}");
        Assert.Equal("Dracula", set["result"]!["theme"]!.GetValue<string>());

        // list again: the just-set theme is now current.
        var after = SendTo(host, """{"cmd":"theme.list"}""");
        Assert.Equal("Dracula", after["result"]!["theme"]!.GetValue<string>());

        // an unknown theme name is rejected, not silently ignored.
        var bad = SendTo(host, """{"cmd":"theme.set","args":{"name":"NotARealTheme"}}""");
        Assert.False(bad["ok"]!.GetValue<bool>(), $"an unknown theme should fail: {bad.ToJsonString()}");
        Assert.Contains("unknown theme", bad["error"]!.GetValue<string>());

        // no name selects the built-in default ("default ghostty" = nil current).
        var cleared = SendTo(host, """{"cmd":"theme.set"}""");
        Assert.True(cleared["ok"]!.GetValue<bool>(), $"theme.set with no name should select the default: {cleared.ToJsonString()}");
        var afterClear = SendTo(host, """{"cmd":"theme.list"}""");
        Assert.Null(afterClear["result"]!["theme"]);
    }

    // MARK: - Port scaffolding (the upstream ControlAPITestCase helpers, headless)

    /// <summary>The current user's home with forward slashes, JSON-embeddable verbatim — the
    /// NSHomeDirectory() every upstream snapshot seeds cwd with.</summary>
    private static string Home =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/');

    private static string NewId() => Guid.NewGuid().ToString("D").ToUpperInvariant();

    /// <summary>One snapshot session entry at the home dir, the upstream seed shape.</summary>
    private static string SessionJson(string id) =>
        $$$"""{"id":"{{{id}}}","customName":null,"cwd":"{{{Home}}}"}""";

    /// <summary>The upstream three-session single-workspace snapshot, first selected.</summary>
    private static string ThreeSessionSnapshot(string firstId, string secondId, string thirdId) =>
        $$$"""{"version":1,"selectedSessionID":"{{{firstId}}}","workspaces":[{"id":"{{{NewId()}}}","name":"workspace 1","sessions":[{{{SessionJson(firstId)}}},{{{SessionJson(secondId)}}},{{{SessionJson(thirdId)}}}]}]}""";

    /// <summary>The upstream relaunch(withSnapshot:): write the snapshot (when given) over the
    /// shared state dir and boot a SECOND HeadlessHost on a fresh pipe — that IS the relaunch.
    /// The host is stopped at test disposal.</summary>
    private HeadlessHost Relaunch(string? snapshot = null, HeadlessHostOptions? options = null)
    {
        if (snapshot is not null)
        {
            File.WriteAllText(Path.Combine(StateDir, "workspaces.json"), snapshot);
        }
        var host = new HeadlessHost((options ?? new HeadlessHostOptions()) with
        {
            StateDir = StateDir,
            ConfigDir = StateDir,
            PipeName = "agterm-e2e-" + Guid.NewGuid().ToString("N"),
        });
        host.Start();
        _relaunched.Add(host);
        return host;
    }

    /// <summary>SendCommand against an arbitrary (relaunched) host's pipe — one verbatim
    /// newline-JSON request over a fresh connection, like the fixture's SendCommand.</summary>
    private static JsonObject SendTo(HeadlessHost host, string line)
    {
        using var pipe = new NamedPipeClientStream(".", host.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);
        pipe.Connect(10_000);
        pipe.Write(Encoding.UTF8.GetBytes(line + "\n"));
        pipe.Flush();
        var response = ReadLine(pipe);
        var node = JsonNode.Parse(response);
        Assert.True(node is JsonObject, $"response should be a JSON object, got: {response}");
        return (JsonObject)node!;
    }

    private static string ReadLine(PipeStream pipe)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var n = pipe.Read(chunk);
            if (n == 0) break;
            var newline = Array.IndexOf(chunk, (byte)'\n', 0, n);
            if (newline >= 0)
            {
                buffer.Write(chunk, 0, newline);
                break;
            }
            buffer.Write(chunk, 0, n);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Attach a fresh FakeSurface as the session's main-pane surface — the headless
    /// equivalent of the surface being realized on screen.</summary>
    private static FakeSurface Attach(HeadlessHost host, string sessionId)
    {
        var surface = new FakeSurface();
        host.Store.SessionWithId(Guid.Parse(sessionId))!.Surface = surface;
        return surface;
    }

    /// <summary>A JSON string literal (quoted + escaped) for splicing dynamic text into a request
    /// line, standing in for the upstream JSONSerialization-built payloads.</summary>
    private static string JsonString(string value) => JsonValue.Create(value)!.ToJsonString();

    /// <summary>The upstream typeRequest helper: a session.type request line.</summary>
    private static string TypeRequest(string text, string? target, bool select)
    {
        var payload = "{\"cmd\":\"session.type\"";
        if (target is not null) payload += ",\"target\":" + JsonString(target);
        payload += ",\"args\":{\"text\":" + JsonString(text) + ",\"select\":" + (select ? "true" : "false") + "}}";
        return payload;
    }

    /// <summary>Send a session.restore request, returning the raw response. mode is set|none|clear.</summary>
    private JsonObject SendRestore(string target, string mode, string? command = null,
                                   string? pane = null, string? paneId = null)
    {
        var args = "\"mode\":" + JsonString(mode);
        if (command is not null) args += ",\"command\":" + JsonString(command);
        if (pane is not null) args += ",\"pane\":" + JsonString(pane);
        if (paneId is not null) args += ",\"paneID\":" + JsonString(paneId);
        return SendCommand("{\"cmd\":\"session.restore\",\"target\":" + JsonString(target)
            + ",\"args\":{" + args + "}}");
    }

    /// <summary>The tree node of the session with id, for reading restore-override fields back.</summary>
    private JsonObject RestoreNode(string id)
    {
        var response = SendCommand("""{"cmd":"tree"}""");
        var node = SessionNode(response, id);
        Assert.True(node is not null, $"the tree should carry the session's node: {response.ToJsonString()}");
        return node!;
    }

    [Fact]
    public void SessionNewAfterWorkspaceNewTargetsTheNewWorkspace()
    {
        var fresh = SendOk("""{"cmd":"workspace.new","args":{"name":"fresh"}}""")["id"]!.GetValue<string>();
        var created = SendOk("""{"cmd":"session.new","args":{"workspace":"active"}}""")["id"]!.GetValue<string>();

        var owner = WorkspacesIn(SendCommand("""{"cmd":"tree"}"""))
            .Single(w => ((JsonArray)w!["sessions"]!).Any(s => string.Equals(s!["id"]!.GetValue<string>(), created, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(fresh, owner!["id"]!.GetValue<string>(), ignoreCase: true);
    }

    [Fact]
    public void WorkspaceSelectOfTheSelectionOwnerTakesTheTargetBackFromAFreshWorkspace()
    {
        var ownerId = WorkspacesIn(SendCommand("""{"cmd":"tree"}"""))
            .Single(w => ((JsonArray)w!["sessions"]!).Count > 0)!["id"]!.GetValue<string>();
        SendOk("""{"cmd":"workspace.new","args":{"name":"fresh"}}""");
        SendOk($$"""{"cmd":"workspace.select","target":"{{ownerId}}"}""");
        var created = SendOk("""{"cmd":"session.new","args":{"workspace":"active"}}""")["id"]!.GetValue<string>();

        var owner = WorkspacesIn(SendCommand("""{"cmd":"tree"}"""))
            .Single(w => ((JsonArray)w!["sessions"]!).Any(s => string.Equals(s!["id"]!.GetValue<string>(), created, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(ownerId, owner!["id"]!.GetValue<string>(), ignoreCase: true);
    }

    private static JsonArray WorkspacesIn(JsonObject response) =>
        (JsonArray)response["result"]!["tree"]!["workspaces"]!;

    /// <summary>The session node matching id (case-insensitive) anywhere in a tree response, or null.</summary>
    private static JsonObject? SessionNode(JsonObject response, string id)
    {
        foreach (var workspace in WorkspacesIn(response))
        {
            foreach (var session in (JsonArray)workspace!["sessions"]!)
            {
                if (string.Equals(session!["id"]!.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase))
                {
                    return (JsonObject)session;
                }
            }
        }
        return null;
    }

    /// <summary>The active session's id from the fixture host — the upstream activeSessionID().</summary>
    private string ActiveSessionId() => ActiveSessionIdIn(SendCommand("""{"cmd":"tree"}"""));

    private static string ActiveSessionIdIn(JsonObject response)
    {
        foreach (var workspace in WorkspacesIn(response))
        {
            foreach (var session in (JsonArray)workspace!["sessions"]!)
            {
                if (session!["active"]?.GetValue<bool>() == true)
                {
                    return session["id"]!.GetValue<string>();
                }
            }
        }
        Assert.Fail($"no active session in tree: {response.ToJsonString()}");
        return null!;
    }

    /// <summary>Per-workspace sidebar names in order — the pollWorkspaceNames oracle, off the tree.</summary>
    private static string[] WorkspaceNames(JsonObject response) =>
        WorkspacesIn(response).Select(w => w!["name"]!.GetValue<string>()).ToArray();

    /// <summary>Per-workspace session counts in order — the pollSessionCount(s) oracle, off the tree.</summary>
    private static int[] SessionCounts(JsonObject response) =>
        WorkspacesIn(response).Select(w => ((JsonArray)w!["sessions"]!).Count).ToArray();

    /// <summary>Session ids (uppercase) in sidebar order for one workspace — the pollSessionOrder oracle.</summary>
    private static string[] SessionOrder(JsonObject response, int workspaceIndex = 0) =>
        ((JsonArray)WorkspacesIn(response)[workspaceIndex]!["sessions"]!)
        .Select(s => s!["id"]!.GetValue<string>().ToUpperInvariant()).ToArray();

    /// <summary>Per-workspace session counts from workspaces.json on disk — the upstream
    /// file-polling oracle, for the grace-window scenarios where tree and disk deliberately differ.</summary>
    private int[] PersistedSessionCounts()
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(StateDir, "workspaces.json")))!;
        return ((JsonArray)json["workspaces"]!)
            .Select(w => ((JsonArray)w!["sessions"]!).Count).ToArray();
    }

    private bool QuickVisible() =>
        SendCommand("""{"cmd":"tree"}""")["result"]!["tree"]!["quickVisible"]?.GetValue<bool>() ?? false;
}
