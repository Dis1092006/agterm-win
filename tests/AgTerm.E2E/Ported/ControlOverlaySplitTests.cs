using System.Text.Json.Nodes;
using AgTerm.ControlServer;
using Xunit;

namespace AgTerm.E2E.Ported;

/// <summary>
/// Port of agtermUITests/ControlOverlaySplitUITests.swift: control-channel e2e for the overlay
/// lifecycle, the scratch terminal, and the split-pane commands (session.split/scratch/focus/
/// resize) plus the Cmd-W cover-peel precedence. Headless adaptations:
///  - the store tracks the overlay/scratch/split state, but the overlay/scratch PROGRAM never runs
///    (no pty). Where the Swift asserts the program's output or exit status, the wrapper's exit
///    capture is simulated with Host.Store.RecordOverlayExit and the child-exited auto-close with
///    Host.Store.CloseOverlay, then the session.overlay.result wire shapes ("overlay still
///    running" / "no overlay result" / result.exitCode) are asserted verbatim.
///  - synchronous host: every upstream poll (pollSessionOverlay / pollActiveSessionScratch / ...)
///    collapses to a single tree read.
///  - keyboard-first-responder scenarios (focus handoff, sidebar clicks, Cmd-W peeling) need a live
///    key-event route that the headless host does not model: PORT-GAP skips.
/// </summary>
public class ControlOverlaySplitTests : ControlApiTestCase
{
    // session.overlay.open requires a command.
    [Fact]
    public void OverlayOpenRequiresCommand()
    {
        var response = SendCommand("""{"cmd":"session.overlay.open","target":"active"}""");
        Assert.False(response["ok"]?.GetValue<bool>(),
            $"open with no command should fail: {response.ToJsonString()}");
        Assert.Equal("session.overlay.open requires a command", response["error"]?.GetValue<string>());
    }

    // session.overlay open/close lifecycle and the guards: a long-lived command (cat waits on stdin)
    // keeps the overlay up, so a second open errors; after close, closing again errors. (Headless the
    // command never spawns, but the store's overlay slot stays occupied exactly like a long-lived
    // cat keeps it upstream.)
    [Fact]
    public void OverlayOpenCloseLifecycle()
    {
        var id = CreateSession();

        var open = SendCommand($$$"""{"cmd":"session.overlay.open","target":"{{{id}}}","args":{"command":"cat"}}""");
        Assert.True(open["ok"]?.GetValue<bool>(), $"overlay open should succeed: {open.ToJsonString()}");

        var again = SendCommand($$$"""{"cmd":"session.overlay.open","target":"{{{id}}}","args":{"command":"cat"}}""");
        Assert.False(again["ok"]?.GetValue<bool>(), $"a second open while active should fail: {again.ToJsonString()}");
        Assert.Equal("overlay already open", again["error"]?.GetValue<string>());

        var close = SendCommand($$$"""{"cmd":"session.overlay.close","target":"{{{id}}}"}""");
        Assert.True(close["ok"]?.GetValue<bool>(), $"overlay close should succeed: {close.ToJsonString()}");

        var closeAgain = SendCommand($$$"""{"cmd":"session.overlay.close","target":"{{{id}}}"}""");
        Assert.False(closeAgain["ok"]?.GetValue<bool>(),
            $"closing with no overlay should fail: {closeAgain.ToJsonString()}");
        Assert.Equal("no overlay", closeAgain["error"]?.GetValue<string>());
    }

    // session.overlay.resize switches an open overlay between floating and full in place. This
    // asserts the COMMAND PATH: resize succeeds while the overlay is up (a percent AND --full) and
    // the overlay stays up across it, errors with no overlay, and the dispatcher rejects missing/
    // conflicting/out-of-range size args server-side; a raw JSON client (like this test) skips the
    // CLI's validate() entirely, so the dispatcher is the real enforcement boundary.
    [Fact]
    public void OverlayResizeSwitchesFloatingAndFull()
    {
        var id = CreateSession();

        // resizing with no overlay open errors.
        var noOverlay = SendCommand($$$"""{"cmd":"session.overlay.resize","target":"{{{id}}}","args":{"sizePercent":60}}""");
        Assert.False(noOverlay["ok"]?.GetValue<bool>(),
            $"resize with no overlay should fail: {noOverlay.ToJsonString()}");
        Assert.Equal("no overlay", noOverlay["error"]?.GetValue<string>());

        // open a full overlay (cat is long-lived), then resize it to floating and back to full.
        var open = SendCommand($$$"""{"cmd":"session.overlay.open","target":"{{{id}}}","args":{"command":"cat"}}""");
        Assert.True(open["ok"]?.GetValue<bool>(), $"overlay open should succeed: {open.ToJsonString()}");
        Assert.True(SessionFlag(id, "overlay"), "the overlay should be up");

        var toFloating = SendCommand($$$"""{"cmd":"session.overlay.resize","target":"{{{id}}}","args":{"sizePercent":60}}""");
        Assert.True(toFloating["ok"]?.GetValue<bool>(),
            $"resize to floating should succeed: {toFloating.ToJsonString()}");
        // the overlay stays up across the resize (in-place re-flow, never a re-spawn).
        Assert.True(SessionFlag(id, "overlay"), "the overlay stays up after resize");

        var toFull = SendCommand($$$"""{"cmd":"session.overlay.resize","target":"{{{id}}}","args":{"full":true}}""");
        Assert.True(toFull["ok"]?.GetValue<bool>(), $"resize back to full should succeed: {toFull.ToJsonString()}");

        // the dispatcher rejects the bad arg combos server-side.
        var neither = SendCommand($$$"""{"cmd":"session.overlay.resize","target":"{{{id}}}"}""");
        Assert.False(neither["ok"]?.GetValue<bool>(),
            $"resize with neither arg should fail: {neither.ToJsonString()}");
        var both = SendCommand($$$"""{"cmd":"session.overlay.resize","target":"{{{id}}}","args":{"sizePercent":50,"full":true}}""");
        Assert.False(both["ok"]?.GetValue<bool>(), $"resize with both args should fail: {both.ToJsonString()}");
        var oob = SendCommand($$$"""{"cmd":"session.overlay.resize","target":"{{{id}}}","args":{"sizePercent":150}}""");
        Assert.False(oob["ok"]?.GetValue<bool>(),
            $"resize with out-of-range percent should fail: {oob.ToJsonString()}");

        var close = SendCommand($$$"""{"cmd":"session.overlay.close","target":"{{{id}}}"}""");
        Assert.True(close["ok"]?.GetValue<bool>(), $"overlay close should succeed: {close.ToJsonString()}");
    }

    // session.overlay.open --background-color: a valid #rrggbb opens the overlay (this asserts the
    // arm accepts and applies it via the lifecycle), and a malformed color is rejected before the
    // overlay opens.
    [Fact]
    public void OverlayOpenWithBackgroundColorAndRejectsBadColor()
    {
        var id = CreateSession();

        // a malformed color is rejected up front; no overlay opens.
        var bad = SendCommand($$$"""{"cmd":"session.overlay.open","target":"{{{id}}}","args":{"command":"cat","color":"purple"}}""");
        Assert.False(bad["ok"]?.GetValue<bool>(), $"a malformed color should be rejected: {bad.ToJsonString()}");
        Assert.Equal("invalid color: purple (#rrggbb)", bad["error"]?.GetValue<string>());
        Assert.False(SessionFlag(id, "overlay"), "the rejected open must not open an overlay");

        // a valid color opens the overlay (cat is long-lived so it stays up); close tears it down.
        var open = SendCommand($$$"""{"cmd":"session.overlay.open","target":"{{{id}}}","args":{"command":"cat","color":"#2a1a3a"}}""");
        Assert.True(open["ok"]?.GetValue<bool>(),
            $"overlay open with a valid color should succeed: {open.ToJsonString()}");
        Assert.True(SessionFlag(id, "overlay"), "the colored overlay should be up");

        var close = SendCommand($$$"""{"cmd":"session.overlay.close","target":"{{{id}}}"}""");
        Assert.True(close["ok"]?.GetValue<bool>(), $"overlay close should succeed: {close.ToJsonString()}");
    }

    // the overlay auto-closes when its command exits (the SHOW_CHILD_EXITED path). Upstream the
    // command writes a marker file proving it ran; headless no program spawns, so the wrapper's
    // exit capture and the child-exited auto-close are simulated on the store, and the tree's
    // overlay flag clearing (with no explicit session.overlay.close request) is the assertion.
    [Fact]
    public void OverlayAutoClosesWhenCommandExits()
    {
        var id = CreateSession();

        var marker = JsonPath(Path.Combine(StateDir, "overlay-ran"));
        var open = SendCommand($$$"""{"cmd":"session.overlay.open","target":"{{{id}}}","args":{"command":"sh -c 'echo ran > {{{marker}}}'"}}""");
        Assert.True(open["ok"]?.GetValue<bool>(), $"overlay open should succeed: {open.ToJsonString()}");
        Assert.True(SessionFlag(id, "overlay"), "the overlay should be up before its command exits");

        // headless stand-in for the overlay program running to completion: the wrapper records the
        // exit status, then the SHOW_CHILD_EXITED path closes the overlay with no key press.
        Host.Store.RecordOverlayExit(Guid.Parse(id), 0);
        Host.Store.CloseOverlay(Guid.Parse(id));

        Assert.False(SessionFlag(id, "overlay"),
            "the overlay should auto-close when the command exits (no press-any-key prompt)");
    }

    // session.overlay.result reports the overlay program's exit status once it exits (the --block
    // path). While the program runs, result errors "overlay still running"; after exit it returns
    // result.exitCode. Headless the `sh -c 'exit 7'` run is simulated by recording the exit code
    // (the wrapper's `echo $?` capture) and auto-closing.
    [Fact]
    public void OverlayResultReportsExitCode()
    {
        var id = CreateSession();

        var open = SendCommand($$$"""{"cmd":"session.overlay.open","target":"{{{id}}}","args":{"command":"sh -c 'exit 7'"}}""");
        Assert.True(open["ok"]?.GetValue<bool>(), $"overlay open should succeed: {open.ToJsonString()}");

        // headless stand-in for the wrapper's exit capture + the child-exited auto-close.
        Host.Store.RecordOverlayExit(Guid.Parse(id), 7);
        Host.Store.CloseOverlay(Guid.Parse(id));

        // upstream polls session.overlay.result (errors while running) until the code is reported;
        // headless the exit already landed, so one read is authoritative.
        var res = SendCommand($$$"""{"cmd":"session.overlay.result","target":"{{{id}}}"}""");
        Assert.True(res["ok"]?.GetValue<bool>(), $"overlay result should succeed: {res.ToJsonString()}");
        Assert.Equal(7, res["result"]?["exitCode"]?.GetValue<int>());
    }

    // session.overlay.result errors "overlay still running" while the program is up, and "no overlay
    // result" after a force-close where the program never recorded a status (killed before the
    // wrapper's `echo $?`). Headless the never-spawned cat naturally never records a status.
    [Fact]
    public void OverlayResultStillRunningThenClosed()
    {
        var id = CreateSession();

        // cat with no input blocks indefinitely, so the overlay stays up.
        var open = SendCommand($$$"""{"cmd":"session.overlay.open","target":"{{{id}}}","args":{"command":"cat"}}""");
        Assert.True(open["ok"]?.GetValue<bool>(), $"overlay open should succeed: {open.ToJsonString()}");
        Assert.True(SessionFlag(id, "overlay"), "the overlay should be up");

        var running = SendCommand($$$"""{"cmd":"session.overlay.result","target":"{{{id}}}"}""");
        Assert.False(running["ok"]?.GetValue<bool>(), "result should error while the overlay is running");
        Assert.Equal("overlay still running", running["error"]?.GetValue<string>());

        var closed = SendCommand($$$"""{"cmd":"session.overlay.close","target":"{{{id}}}"}""");
        Assert.True(closed["ok"]?.GetValue<bool>(), $"overlay close should succeed: {closed.ToJsonString()}");
        Assert.False(SessionFlag(id, "overlay"), "the overlay should be gone");

        // cat was killed before the wrapper's `echo $?`, so no status was recorded.
        var after = SendCommand($$$"""{"cmd":"session.overlay.result","target":"{{{id}}}"}""");
        Assert.False(after["ok"]?.GetValue<bool>(), "result should error when no status was recorded");
        Assert.Equal("no overlay result", after["error"]?.GetValue<string>());
    }

    // closing an overlay must hand keyboard focus back to the underlying session terminal.
    [Fact(Skip = "PORT-GAP: needs live terminal — the oracle is a real keyboard first-responder "
        + "handoff (XCUI typeText into whichever surface holds focus, proven by a shell `read`/tty "
        + "marker); the headless host has no key-event routing or focus model")]
    public void OverlayCloseReturnsFocusToSession()
    {
        // Swift body (ControlOverlaySplitUITests.testOverlayCloseReturnsFocusToSession):
        //   let tree = try sendCommand(#"{"cmd":"tree"}"#) ... let id = sessions.first?["id"]
        //   // the session's tty, captured by injecting into its surface directly:
        //   sendCommand(typeRequest(text: "tty > '<sessionTTY>'\n", target: id, select: false))
        //   let sessionTtyValue = pollMarker(sessionTTY)
        //   // overlay shell captures one keyboard line, then stays alive (cat):
        //   session.overlay.open {command: "sh -c 'IFS= read -r x; printf %s \"$x\" > <ovl>; cat'"}
        //   app.typeText("OVLFOCUS"); app.typeKey(.return)
        //   XCTAssertEqual(pollMarker(ovlMarker), "OVLFOCUS")   // overlay holds first responder
        //   session.overlay.close → ok; pollSessionOverlay(id, false)
        //   let afterValue = keyboardTypeUntilMarker("tty > '<afterTTY>'", file: afterTTY)
        //   XCTAssertEqual(afterValue, sessionTtyValue)         // focus returned to the SAME tty
    }

    // a cover (overlay or scratch) is modal within its session: a sidebar click restores keyboard
    // focus to the cover ON TOP, not to the pane BEHIND it.
    [Fact(Skip = "PORT-GAP: needs live terminal — asserts which surface holds keyboard first "
        + "responder across a real sidebar row click (XCUI click + typeText); headless has no "
        + "sidebar view, click routing, or focus model")]
    public void SidebarClickKeepsFocusOnOverlayNotPaneBehind()
    {
        // Swift body (testSidebarClickKeepsFocusOnOverlayNotPaneBehind):
        //   let id = try activeSessionID()
        //   // two blocking reads then cat: pre-click line proves the overlay owns the keyboard,
        //   // post-click line is the assertion.
        //   session.overlay.open {command: "sh -c 'IFS= read -r a; printf %s \"$a\" > <pre>; "
        //                                  + "IFS= read -r b; printf %s \"$b\" > <post>; cat'",
        //                         sizePercent: 50}
        //   pollSessionOverlay(id, true)
        //   assertSidebarClickKeepsFocusOnCover(pre, post, cover: "overlay"):
        //     typeText("PRECLICK") → pollMarker(pre) == "PRECLICK"
        //     app.staticTexts["session-row"].firstMatch.click()
        //     typeText("OVLCLICK") → pollMarker(post) == "OVLCLICK"
    }

    // the scratch terminal is the other `topmostSurface` branch and is full-coverage, so a sidebar
    // click landing on the pane would type into a shell that is not even visible.
    [Fact(Skip = "PORT-GAP: needs live terminal — same keyboard-focus-across-sidebar-click oracle "
        + "as the overlay variant, for the scratch cover; headless has no click or focus routing")]
    public void SidebarClickKeepsFocusOnScratchNotPaneBehind()
    {
        // Swift body (testSidebarClickKeepsFocusOnScratchNotPaneBehind):
        //   session.scratch {mode: "on", command: "sh -c 'IFS= read -r a; ... > <pre>; "
        //                                          + "IFS= read -r b; ... > <post>; cat'"}
        //   pollActiveSessionScratch(true)
        //   assertSidebarClickKeepsFocusOnCover(pre, post, cover: "scratch")
    }

    // a pane's shell exiting collapses the split and re-hosts the survivor. Upstream additionally
    // proves (via the keyboard) that the survivor's onExit focus grab lands on the overlay, not the
    // pane — the first-responder half is a PORT-GAP; the assertable headless invariant is that the
    // main pane's exit collapses the split (the survivor is promoted) while the overlay stays open
    // and active, so a background pane finishing can never tear the cover down.
    [Fact]
    public void PaneExitUnderOverlayKeepsFocusOnOverlay()
    {
        var id = ActiveSessionId();
        var split = SendCommand($$$"""{"cmd":"session.split","target":"{{{id}}}","args":{"mode":"on"}}""");
        Assert.True(split["ok"]?.GetValue<bool>(), "opening the split should succeed");
        Assert.True(SessionFlag(id, "split"), "the split should be up");

        // attach fake pane surfaces (headless surfaces never realize on their own): the main pane
        // receives the pane-addressed session.type, the split pane is the promotion survivor.
        var session = Host.Store.SessionWithId(Guid.Parse(id))!;
        var mainPane = new FakeSurface();
        session.Surface = mainPane;
        session.SplitSurface = new FakeSurface();

        // upstream builds this request with JSONSerialization (no fixed literal): a floating
        // overlay whose shell captures two keyboard lines (pre/post) then holds with cat.
        var pre = JsonPath(Path.Combine(StateDir, "paneexit-pre"));
        var post = JsonPath(Path.Combine(StateDir, "paneexit-post"));
        var open = SendCommand($$$"""{"cmd":"session.overlay.open","target":"{{{id}}}","args":{"command":"sh -c 'IFS= read -r a; printf %s \"$a\" > {{{pre}}}; IFS= read -r b; printf %s \"$b\" > {{{post}}}; cat'","sizePercent":50}}""");
        Assert.True(open["ok"]?.GetValue<bool>(), "floating overlay open should succeed");
        Assert.True(SessionFlag(id, "overlay"), "the overlay should be up");

        // upstream: app.typeText("PRECLICK") proves the overlay owns the keyboard before the exit —
        // there is no first responder headless, so the pre-click focus probe has no analogue.

        // exit the MAIN pane's shell by injecting into its surface directly (injection is
        // focus-independent, so this drives closePrimaryPane -> onExit without touching focus).
        var type = SendCommand($$$"""{"cmd":"session.type","target":"{{{id}}}","args":{"text":"exit\n","pane":"left"}}""");
        Assert.True(type["ok"]?.GetValue<bool>(), "typing exit into the main pane should succeed");
        Assert.Contains("exit\n", mainPane.Injected);

        // headless stand-in for the shell actually exiting: the primary surface's exit path (the
        // upstream onExit) promotes the split pane to the sole pane and collapses the split.
        Host.Store.ClosePrimaryPane(Guid.Parse(id));
        Assert.False(SessionFlag(id, "split"), "the main pane's exit should collapse the split");

        // upstream: app.typeText("OVLCLICK") proves focus stayed on the overlay. Headless the
        // assertable invariant is that the pane exit left the overlay open on the session.
        Assert.True(SessionFlag(id, "overlay"),
            "a pane exit under an overlay must leave the overlay up, not tear it down with the pane");
        Assert.True(session.OverlayActive, "the store must still report the overlay active");
    }

    // a FULL overlay opened in a BACKGROUND (non-selected) session must NOT steal keyboard first
    // responder.
    [Fact(Skip = "PORT-GAP: needs live terminal — the oracle is real keyboard input reaching the "
        + "visible active session's tty rather than the hidden overlay's shell `read`; headless "
        + "has no keyboard routing or first responder")]
    public void BackgroundSessionOverlayDoesNotStealKeyboardFocus()
    {
        // Swift body (testBackgroundSessionOverlayDoesNotStealKeyboardFocus):
        //   sessionA = seeded (visible/active); sessionB = session.new; session.select A
        //   ttyAValue = typeUntilMarker("tty > '<ttyA>'\n", target: sessionA, select: false)
        //   session.overlay.open on B {command: "sh -c 'IFS= read -r x; printf %s \"$x\" > <ovl>; cat'"}
        //   pollSessionOverlay(B, true)
        //   afterValue = keyboardTypeUntilMarker("tty > '<afterTTY>'")
        //   XCTAssertEqual(afterValue, ttyAValue)  // keyboard reached the ACTIVE session A
        //   XCTAssertNil(pollMarker(ovlMarker))    // B's hidden overlay captured nothing
    }

    // a FULL overlay opened on a BACKGROUND target with no `follow` runs its program in the eager
    // deck WITHOUT changing the active session: create A (background), then B (active), open the
    // overlay on A, assert its program "ran" (overlay.result reports an exit code — headless the
    // wrapper's capture is simulated) and the active session is still B.
    [Fact]
    public void OverlayOpenDefaultDoesNotSwitchActiveSession()
    {
        var a = CreateSession(); // first session
        var b = CreateSession(); // second session becomes active, so A is a background session
        Assert.Equal(b, ActiveSessionId(), ignoreCase: true);

        var open = SendCommand($$$"""{"cmd":"session.overlay.open","target":"{{{a}}}","args":{"command":"sh -c 'exit 3'"}}""");
        Assert.True(open["ok"]?.GetValue<bool>(),
            $"opening a full overlay on the background session should succeed: {open.ToJsonString()}");

        // headless stand-in for the background program running and exiting (upstream
        // pollOverlayExitCode waits for the wrapper's capture + auto-close).
        Host.Store.RecordOverlayExit(Guid.Parse(a), 3);
        Host.Store.CloseOverlay(Guid.Parse(a));
        var res = SendCommand($$$"""{"cmd":"session.overlay.result","target":"{{{a}}}"}""");
        Assert.True(res["ok"]?.GetValue<bool>(), $"overlay result should succeed: {res.ToJsonString()}");
        Assert.Equal(3, res["result"]?["exitCode"]?.GetValue<int>());

        // the active session is unchanged — a default (no follow) open does NOT switch to the target.
        Assert.Equal(b, ActiveSessionId(), ignoreCase: true);
    }

    // a FLOATING overlay (sizePercent) opened on a BACKGROUND target with no `follow` runs its
    // program WITHOUT changing the active session — the core parity assertion.
    [Fact]
    public void FloatingOverlayOnBackgroundRunsWithoutSwitch()
    {
        var a = CreateSession();
        var b = CreateSession(); // active; A is background
        Assert.Equal(b, ActiveSessionId(), ignoreCase: true);

        var open = SendCommand($$$"""{"cmd":"session.overlay.open","target":"{{{a}}}","args":{"command":"sh -c 'exit 5'","sizePercent":70}}""");
        Assert.True(open["ok"]?.GetValue<bool>(),
            $"opening a floating overlay on the background session should succeed: {open.ToJsonString()}");

        // headless stand-in for the background floating program running and exiting.
        Host.Store.RecordOverlayExit(Guid.Parse(a), 5);
        Host.Store.CloseOverlay(Guid.Parse(a));
        var res = SendCommand($$$"""{"cmd":"session.overlay.result","target":"{{{a}}}"}""");
        Assert.True(res["ok"]?.GetValue<bool>(), $"overlay result should succeed: {res.ToJsonString()}");
        Assert.Equal(5, res["result"]?["exitCode"]?.GetValue<int>());

        Assert.Equal(b, ActiveSessionId(), ignoreCase: true);
    }

    // `follow: true` on a BACKGROUND target switches the active session to that target — for BOTH
    // the full and the floating overlay (two distinct background targets, since only one overlay may
    // be open per session). `cat` blocks so each overlay stays up; the assertion is purely that the
    // selection switched.
    [Fact]
    public void OverlayOpenFollowSwitchesToTarget()
    {
        var full = CreateSession();     // background target for the full overlay
        var floating = CreateSession(); // background target for the floating overlay
        var active = CreateSession();   // newest session is active; both targets are background
        Assert.Equal(active, ActiveSessionId(), ignoreCase: true);

        // FULL overlay with follow → the active session becomes the full target.
        var openFull = SendCommand($$$"""{"cmd":"session.overlay.open","target":"{{{full}}}","args":{"command":"cat","follow":true}}""");
        Assert.True(openFull["ok"]?.GetValue<bool>(),
            $"full overlay open with follow should succeed: {openFull.ToJsonString()}");
        Assert.Equal(full, ActiveSessionId(), ignoreCase: true);
        Assert.True(SessionFlag(full, "overlay"),
            "the full overlay must actually mount on its target, not just select it");

        // FLOATING overlay with follow → the active session becomes the (different) floating target.
        var openFloat = SendCommand($$$"""{"cmd":"session.overlay.open","target":"{{{floating}}}","args":{"command":"cat","sizePercent":70,"follow":true}}""");
        Assert.True(openFloat["ok"]?.GetValue<bool>(),
            $"floating overlay open with follow should succeed: {openFloat.ToJsonString()}");
        Assert.Equal(floating, ActiveSessionId(), ignoreCase: true);
        Assert.True(SessionFlag(floating, "overlay"),
            "the floating overlay must actually mount on its target, not just select it");
    }

    // `follow: true` targeting the ALREADY-active session succeeds and stays on it (a no-op select).
    [Fact]
    public void OverlayOpenFollowOnActiveSessionIsNoop()
    {
        var a = CreateSession();
        var b = CreateSession(); // B is active
        Assert.Equal(b, ActiveSessionId(), ignoreCase: true);

        var open = SendCommand($$$"""{"cmd":"session.overlay.open","target":"{{{b}}}","args":{"command":"cat","follow":true}}""");
        Assert.True(open["ok"]?.GetValue<bool>(),
            $"follow on the already-active session should succeed: {open.ToJsonString()}");
        Assert.True(SessionFlag(b, "overlay"), "the overlay should be up on B");
        Assert.Equal(b, ActiveSessionId(), ignoreCase: true);
    }

    // session.split toggle shows split:true in the tree; off hides it (keep-alive — the pane's
    // surface is NOT destroyed, only closeSplit on shell-exit does that), clearing split:false.
    [Fact]
    public void SessionSplitToggle()
    {
        var split = SendCommand("""{"cmd":"session.split","target":"active","args":{"mode":"toggle"}}""");
        Assert.True(split["ok"]?.GetValue<bool>(), $"session.split toggle should succeed: {split.ToJsonString()}");
        Assert.True(ActiveSessionFlag("split"), "the active session should report split:true");

        var unsplit = SendCommand("""{"cmd":"session.split","target":"active","args":{"mode":"off"}}""");
        Assert.True(unsplit["ok"]?.GetValue<bool>(), $"session.split off should succeed: {unsplit.ToJsonString()}");
        Assert.False(ActiveSessionFlag("split"), "off should clear the split");
    }

    // session.scratch toggle shows scratch:true in the tree; off hides it (keep-alive), clearing
    // scratch:false. An unknown mode is rejected.
    [Fact]
    public void SessionScratchToggle()
    {
        var on = SendCommand("""{"cmd":"session.scratch","target":"active","args":{"mode":"toggle"}}""");
        Assert.True(on["ok"]?.GetValue<bool>(), $"session.scratch toggle should succeed: {on.ToJsonString()}");
        Assert.True(ActiveSessionFlag("scratch"), "the active session should report scratch:true");

        // `on` while already shown is idempotent (the delta guard skips the redundant toggle, so it
        // does NOT flip back to hidden).
        var onAgain = SendCommand("""{"cmd":"session.scratch","target":"active","args":{"mode":"on"}}""");
        Assert.True(onAgain["ok"]?.GetValue<bool>(),
            $"session.scratch on (already on) should succeed: {onAgain.ToJsonString()}");
        Assert.True(ActiveSessionFlag("scratch"), "on while shown stays scratch:true");

        var off = SendCommand("""{"cmd":"session.scratch","target":"active","args":{"mode":"off"}}""");
        Assert.True(off["ok"]?.GetValue<bool>(), $"session.scratch off should succeed: {off.ToJsonString()}");
        Assert.False(ActiveSessionFlag("scratch"), "off should hide the scratch");

        // `off` while already hidden is idempotent.
        var offAgain = SendCommand("""{"cmd":"session.scratch","target":"active","args":{"mode":"off"}}""");
        Assert.True(offAgain["ok"]?.GetValue<bool>(),
            $"session.scratch off (already off) should succeed: {offAgain.ToJsonString()}");
        Assert.False(ActiveSessionFlag("scratch"), "off while hidden stays scratch:false");

        var bad = SendCommand("""{"cmd":"session.scratch","target":"active","args":{"mode":"bogus"}}""");
        Assert.False(bad["ok"]?.GetValue<bool>(), $"invalid scratch mode should fail: {bad.ToJsonString()}");
        Assert.Contains("invalid scratch mode", bad["error"]?.GetValue<string>() ?? "");
    }

    // Cmd-W with the scratch shown DISMISSES the scratch, not the session under it.
    [Fact(Skip = "PORT-GAP: needs live terminal — the Cmd-W cover-peel shortcut is app-side key "
        + "handling (no control command drives it) and the headless host has no key-event route")]
    public void CloseSessionShortcutHidesScratchInsteadOfClosingSession()
    {
        // Swift body (testCloseSessionShortcutHidesScratchInsteadOfClosingSession):
        //   session.scratch {mode: "on"} → ok; pollActiveSessionScratch(true)
        //   app.activate(); app.typeKey("w", modifierFlags: .command)
        //   pollActiveSessionScratch(false)  // Cmd-W hid the scratch
        //   pollSessionRowCount(1)           // the session behind it survived
    }

    // Cmd-W with a full overlay up DISMISSES the overlay (closes it), not the session under it.
    [Fact(Skip = "PORT-GAP: needs live terminal — same Cmd-W key-event route as the scratch "
        + "variant; not reachable over the control API")]
    public void CloseSessionShortcutClosesOverlayInsteadOfClosingSession()
    {
        // Swift body (testCloseSessionShortcutClosesOverlayInsteadOfClosingSession):
        //   session.overlay.open {command: "cat"} on the seeded session → ok
        //   pollSessionOverlay(seededID, true)
        //   app.activate(); app.typeKey("w", modifierFlags: .command)
        //   pollSessionOverlay(seededID, false); pollSessionRowCount(1)
    }

    // Cmd-W closes a FLOATING overlay (session visible behind it) without closing the session.
    [Fact(Skip = "PORT-GAP: needs live terminal — same Cmd-W key-event route; the floating overlay "
        + "holding first responder is a UI-focus fact with no headless analogue")]
    public void CloseSessionShortcutClosesFloatingOverlayInsteadOfClosingSession()
    {
        // Swift body (testCloseSessionShortcutClosesFloatingOverlayInsteadOfClosingSession):
        //   session.overlay.open {command: "cat", sizePercent: 70} → ok
        //   pollSessionOverlay(seededID, true)
        //   app.activate(); app.typeKey("w", modifierFlags: .command)
        //   pollSessionOverlay(seededID, false); pollSessionRowCount(1)
    }

    // Cmd-W peels stacked covers in z-order: overlay (zIndex 2) over scratch (zIndex 1).
    [Fact(Skip = "PORT-GAP: needs live terminal — the z-order peel precedence lives in the app's "
        + "Cmd-W handler; the headless host has no key routing and no cover-peel seam")]
    public void CloseSessionShortcutPeelsStackedCoversInPrecedenceOrder()
    {
        // Swift body (testCloseSessionShortcutPeelsStackedCoversInPrecedenceOrder):
        //   session.scratch {mode: "on"} → ok; pollActiveSessionScratch(true)
        //   session.overlay.open {command: "cat"} → ok; pollSessionOverlay(seededID, true)
        //   Cmd-W #1: pollSessionOverlay(seededID, false); pollActiveSessionScratch(true)
        //   Cmd-W #2: pollActiveSessionScratch(false); pollSessionRowCount(1)
    }

    // session.scratch --command runs the command AS the scratch's process (not a shell). Upstream
    // the marker file the command writes is the run oracle; headless no process spawns, so the
    // recorded scratch command (consumed run-once by the surface factory) plus the scratch being up
    // is the assertable command path.
    [Fact]
    public void SessionScratchCommandRunsAsProcess()
    {
        var marker = JsonPath(Path.Combine(StateDir, $"agterm-scratchcmd-{Guid.NewGuid():N}.txt"));
        // upstream builds this request with JSONSerialization (no fixed literal).
        var resp = SendCommand($$$"""{"cmd":"session.scratch","target":"active","args":{"mode":"on","command":"sh -c 'printf SCRATCHRAN > {{{marker}}}'"}}""");
        Assert.True(resp["ok"]?.GetValue<bool>(),
            $"session.scratch --command should succeed: {resp.ToJsonString()}");

        // headless stand-in for "the command ran as the scratch's process": the store armed exactly
        // this command as the scratch process and brought the scratch up.
        var session = Host.Store.SessionWithId(Host.Store.SelectedSessionId!.Value)!;
        Assert.Equal($"sh -c 'printf SCRATCHRAN > {marker}'", session.ScratchCommand);
        Assert.True(session.ScratchActive, "the scratch should be up running the command");
    }

    // session.scratch on a NON-active target selects it first (the scratch is full-coverage and
    // grabs focus on show, so it must be the visible session), then shows the scratch on it.
    [Fact]
    public void SessionScratchOnSelectsTarget()
    {
        // the seeded session is active; capture its id.
        var seededId = ActiveSessionId();

        // create a second session — session.new focuses it, so the seeded one is no longer active.
        var newId = CreateSession();
        Assert.NotEqual(newId.ToLowerInvariant(), seededId.ToLowerInvariant());

        // show scratch on the non-active seeded session: it should become active AND report scratch:true.
        var on = SendCommand($$$"""{"cmd":"session.scratch","target":"{{{seededId}}}","args":{"mode":"on"}}""");
        Assert.True(on["ok"]?.GetValue<bool>(),
            $"session.scratch on a non-active target should succeed: {on.ToJsonString()}");
        var seeded = SessionNode(seededId);
        Assert.True(Flag(seeded, "active") && Flag(seeded, "scratch"),
            "showing scratch should select the target and report scratch:true");
    }

    // session.focus errors on a non-split session, succeeds on each pane once split, and rejects an
    // unknown pane.
    [Fact]
    public void SessionFocusPane()
    {
        var notSplit = SendCommand("""{"cmd":"session.focus","target":"active","args":{"pane":"right"}}""");
        Assert.False(notSplit["ok"]?.GetValue<bool>(),
            $"focus on a non-split session should fail: {notSplit.ToJsonString()}");
        Assert.Contains("no split", notSplit["error"]?.GetValue<string>() ?? "");

        var split = SendCommand("""{"cmd":"session.split","target":"active","args":{"mode":"on"}}""");
        Assert.True(split["ok"]?.GetValue<bool>(), $"split on should succeed: {split.ToJsonString()}");
        Assert.True(ActiveSessionFlag("split"), "the active session should report split:true");

        var right = SendCommand("""{"cmd":"session.focus","target":"active","args":{"pane":"right"}}""");
        Assert.True(right["ok"]?.GetValue<bool>(), $"focus right should succeed: {right.ToJsonString()}");
        var left = SendCommand("""{"cmd":"session.focus","target":"active","args":{"pane":"left"}}""");
        Assert.True(left["ok"]?.GetValue<bool>(), $"focus left should succeed: {left.ToJsonString()}");

        var bad = SendCommand("""{"cmd":"session.focus","target":"active","args":{"pane":"bogus"}}""");
        Assert.False(bad["ok"]?.GetValue<bool>(), $"invalid pane should fail: {bad.ToJsonString()}");
        Assert.Contains("invalid pane", bad["error"]?.GetValue<string>() ?? "");
    }

    // session.resize errors on a non-split session, sets an absolute fraction (clamped) and a
    // relative nudge on a split, persists it to workspaces.json, and rejects a request carrying no
    // fraction.
    [Fact]
    public void SessionResizeSplitDivider()
    {
        var notSplit = SendCommand("""{"cmd":"session.resize","target":"active","args":{"ratio":0.7}}""");
        Assert.False(notSplit["ok"]?.GetValue<bool>(),
            $"resize on a non-split session should fail: {notSplit.ToJsonString()}");
        Assert.Contains("no split", notSplit["error"]?.GetValue<string>() ?? "");

        var split = SendCommand("""{"cmd":"session.split","target":"active","args":{"mode":"on"}}""");
        Assert.True(split["ok"]?.GetValue<bool>(), $"split on should succeed: {split.ToJsonString()}");
        Assert.True(ActiveSessionFlag("split"), "the active session should report split:true");

        // relative nudge from the nil base (0.5 default) before any absolute set: grow-left 0.1 -> 0.6.
        var fromDefault = SendCommand("""{"cmd":"session.resize","target":"active","args":{"ratioDelta":0.1}}""");
        Assert.True(fromDefault["ok"]?.GetValue<bool>(),
            $"nudge from default should succeed: {fromDefault.ToJsonString()}");
        Assert.Equal(0.6, fromDefault["result"]?["ratio"]?.GetValue<double>() ?? -1, 4);

        // server rejects both fraction forms at once — the CLI's validate() blocks this, but a raw
        // client can send it.
        var both = SendCommand("""{"cmd":"session.resize","target":"active","args":{"ratio":0.7,"ratioDelta":0.1}}""");
        Assert.False(both["ok"]?.GetValue<bool>(), $"both ratio and delta should fail: {both.ToJsonString()}");
        Assert.Contains("mutually exclusive", both["error"]?.GetValue<string>() ?? "");

        // absolute fraction: echoed in result.ratio and persisted to the snapshot.
        var abs = SendCommand("""{"cmd":"session.resize","target":"active","args":{"ratio":0.7}}""");
        Assert.True(abs["ok"]?.GetValue<bool>(), $"absolute resize should succeed: {abs.ToJsonString()}");
        Assert.Equal(0.7, abs["result"]?["ratio"]?.GetValue<double>() ?? -1, 4);
        Assert.True(PersistedSplitRatios().Any(r => r is { } v && Math.Abs(v - 0.7) < 0.0001),
            "0.7 should land in workspaces.json");

        // out-of-range absolute clamps to the cap (0.95).
        var clamped = SendCommand("""{"cmd":"session.resize","target":"active","args":{"ratio":2.0}}""");
        Assert.True(clamped["ok"]?.GetValue<bool>(), $"clamped resize should succeed: {clamped.ToJsonString()}");
        Assert.Equal(0.95, clamped["result"]?["ratio"]?.GetValue<double>() ?? -1, 4);

        // relative nudge: grow-right 0.1 (a negative delta) from 0.95 lands at 0.85.
        var nudged = SendCommand("""{"cmd":"session.resize","target":"active","args":{"ratioDelta":-0.1}}""");
        Assert.True(nudged["ok"]?.GetValue<bool>(), $"relative resize should succeed: {nudged.ToJsonString()}");
        Assert.Equal(0.85, nudged["result"]?["ratio"]?.GetValue<double>() ?? -1, 4);

        // neither a ratio nor a delta is a usage error.
        var empty = SendCommand("""{"cmd":"session.resize","target":"active","args":{}}""");
        Assert.False(empty["ok"]?.GetValue<bool>(), $"resize with no fraction should fail: {empty.ToJsonString()}");
    }

    // MARK: - helpers (the upstream polls collapse to synchronous reads on the headless host)

    /// <summary>The upstream newSession()/inline session.new unwrap: creates a session and returns
    /// its id (session.new focuses the new session, so it becomes the active one).</summary>
    private string CreateSession()
    {
        var created = SendCommand("""{"cmd":"session.new"}""");
        var id = (created["result"] as JsonObject)?["id"]?.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(id),
            $"session.new should return the new id: {created.ToJsonString()}");
        return id!;
    }

    /// <summary>The tree node of the session with id (case-insensitive, like the Swift polls).</summary>
    private JsonObject SessionNode(string id)
    {
        foreach (var workspace in Tree()["tree"]!["workspaces"]!.AsArray())
        {
            foreach (var session in workspace!["sessions"]!.AsArray())
            {
                if (string.Equals(session!["id"]?.GetValue<string>(), id,
                        StringComparison.OrdinalIgnoreCase))
                    return session.AsObject();
            }
        }
        Assert.Fail($"session {id} not in tree");
        return null!;
    }

    /// <summary>The tree node of the ACTIVE session (the pollActiveSession* subject).</summary>
    private JsonObject ActiveSessionNode()
    {
        foreach (var workspace in Tree()["tree"]!["workspaces"]!.AsArray())
        {
            foreach (var session in workspace!["sessions"]!.AsArray())
            {
                if (Flag(session!.AsObject(), "active")) return session.AsObject();
            }
        }
        Assert.Fail("no active session in tree");
        return null!;
    }

    /// <summary>The upstream activeSessionID() helper.</summary>
    private string ActiveSessionId() => ActiveSessionNode()["id"]!.GetValue<string>();

    /// <summary>pollSessionOverlay/pollActiveSessionSplit-style flag read, synchronous: the named
    /// bool on the session with id; absent treated as false, like the Swift polls.</summary>
    private bool SessionFlag(string id, string name) => Flag(SessionNode(id), name);

    /// <summary>pollActiveSessionSplit/pollActiveSessionScratch, synchronous.</summary>
    private bool ActiveSessionFlag(string name) => Flag(ActiveSessionNode(), name);

    private static bool Flag(JsonObject session, string name) =>
        session[name]?.GetValue<bool>() ?? false;

    /// <summary>Every persisted splitRatio in workspaces.json — the pollSplitRatio read-back
    /// (Save() is synchronous headless, so one read is authoritative).</summary>
    private IEnumerable<double?> PersistedSplitRatios()
    {
        var snapshot = JsonNode.Parse(
            File.ReadAllText(Path.Combine(StateDir, "workspaces.json")))!.AsObject();
        return snapshot["workspaces"]!.AsArray()
            .SelectMany(w => w!["sessions"]!.AsArray())
            .Select(s => s!["splitRatio"]?.GetValue<double>());
    }

    /// <summary>A path splice-safe inside a JSON string literal (forward slashes, no escapes) — the
    /// Swift tests splice POSIX marker paths the same way. Headless the marker is never written;
    /// the path only keeps the spliced request literal well-formed.</summary>
    private static string JsonPath(string path) => path.Replace('\\', '/');
}
