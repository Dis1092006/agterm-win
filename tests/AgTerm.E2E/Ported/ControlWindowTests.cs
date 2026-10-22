using System.Text.Json.Nodes;
using Xunit;

namespace AgTerm.E2E.Ported;

/// <summary>
/// Port of agtermUITests/ControlWindowUITests.swift — the window-command control-channel suite
/// (window.new/list/select/close/resize/move/zoom/fullscreen, the title-bar gestures, and the
/// window-scoped tree/list oracles). The headless host owns exactly ONE virtual window and no
/// compositor, so:
///  - list/select/rename/targeting semantics port verbatim against the single window;
///  - arms that need a real on-screen window (new/close/resize/move/zoom/fullscreen) are asserted
///    against the documented headless refusal ("window management is not available in the headless
///    host") with a comment naming the upstream expectation;
///  - multi-window scenarios and the mouse gestures are skipped with the Swift body preserved in
///    comments (PORT-GAP).
/// Upstream polls (pollWindowList/pollTreeSessionCount/...) collapse to single reads: the headless
/// host dispatches synchronously under one gate, so state is authoritative at command return.
/// </summary>
public class ControlWindowTests : ControlApiTestCase
{
    /// <summary>The pinned headless wire error for the window arms needing a real compositor
    /// window (HeadlessControlActions.WindowManagementUnavailable, documented in PLATFORM-NOTES).</summary>
    private const string WindowManagementUnavailable =
        "window management is not available in the headless host";

    // MARK: - Window commands

    // Upstream: window.new opens a second window; window.list shows two windows (the new one open,
    // exactly one active) and a real on-screen window materializes (pollAppWindows).
    // Headless: the seeded single-window list shape holds verbatim; window.new answers the pinned
    // refusal (upstream expects ok:true + result.id), and the list stays one open+active window.
    [Fact]
    public void WindowNewAndList()
    {
        // the seeded launch has exactly one window, and it's the active one.
        var initial = WindowList();
        Assert.True(initial.Count == 1, $"should start with the one seeded window: {initial.ToJsonString()}");
        Assert.True(initial[0]!["active"]?.GetValue<bool>(), "the seeded window should be active");

        var created = SendCommand("""{"cmd":"window.new","args":{"name":"second"}}""");
        // upstream: XCTAssertEqual(created["ok"], true) + the new window's result.id.
        Assert.False(created["ok"]?.GetValue<bool>(), $"headless window.new must refuse: {created.ToJsonString()}");
        Assert.Equal(WindowManagementUnavailable, created["error"]?.GetValue<string>());

        // upstream then polls window.list for two windows (the new one open, exactly one active)
        // and app.windows for the on-screen window — unreachable headless; the single seeded
        // window remains the only one, open and active (the active-flag invariant trivially holds).
        var list = WindowList();
        Assert.True(list.Count == 1, $"the headless host stays single-window: {list.ToJsonString()}");
        Assert.True(list[0]!["open"]?.GetValue<bool>(), "the single window is always open");
        Assert.True(list[0]!["active"]?.GetValue<bool>(), "the single window is always active");
    }

    // Upstream: window.resize sets the active window's frame size (ok:true) and the on-screen
    // window settles at 1000x700. Headless: no compositor — the implicit active target resolves,
    // then the arm answers the pinned refusal.
    [Fact]
    public void WindowResize()
    {
        var response = SendCommand("""{"cmd":"window.resize","args":{"width":1000,"height":700}}""");
        Assert.False(response["ok"]?.GetValue<bool>(), $"headless window.resize must refuse: {response.ToJsonString()}");
        Assert.Equal(WindowManagementUnavailable, response["error"]?.GetValue<string>());
        // upstream then polls window.frame.size toward 1000x700 — no frame exists headless.
    }

    // Upstream: window.move repositions the active window (both calls ok:true); the second move
    // right+down shifts the on-screen origin right+down. Headless: pinned refusal for both.
    [Fact]
    public void WindowMove()
    {
        var first = SendCommand("""{"cmd":"window.move","args":{"x":80,"y":80}}""");
        Assert.False(first["ok"]?.GetValue<bool>(), $"headless window.move must refuse: {first.ToJsonString()}");
        Assert.Equal(WindowManagementUnavailable, first["error"]?.GetValue<string>());

        var second = SendCommand("""{"cmd":"window.move","args":{"x":280,"y":240}}""");
        Assert.False(second["ok"]?.GetValue<bool>(), $"headless window.move must refuse: {second.ToJsonString()}");
        Assert.Equal(WindowManagementUnavailable, second["error"]?.GetValue<string>());
        // upstream then polls window.frame.origin for the right+down shift — no frame headless.
    }

    // Upstream: window.zoom toggles the active window between its normal frame and a maximized
    // frame (resize to 800x600 ok, first zoom grows, second zoom restores — all ok:true).
    // Headless: resize and both zooms answer the pinned refusal.
    [Fact]
    public void WindowZoom()
    {
        // upstream: ok:true, then the frame settles near 800x600 before the first zoom.
        var resized = SendCommand("""{"cmd":"window.resize","args":{"width":800,"height":600}}""");
        Assert.False(resized["ok"]?.GetValue<bool>());
        Assert.Equal(WindowManagementUnavailable, resized["error"]?.GetValue<string>());

        // upstream: ok:true + the window grows past normal+50.
        var zoomed = SendCommand("""{"cmd":"window.zoom"}""");
        Assert.False(zoomed["ok"]?.GetValue<bool>(), $"headless window.zoom must refuse: {zoomed.ToJsonString()}");
        Assert.Equal(WindowManagementUnavailable, zoomed["error"]?.GetValue<string>());

        // upstream: a second zoom (ok:true) restores toward the normal frame.
        var restored = SendCommand("""{"cmd":"window.zoom"}""");
        Assert.False(restored["ok"]?.GetValue<bool>());
        Assert.Equal(WindowManagementUnavailable, restored["error"]?.GetValue<string>());
    }

    // Upstream: window.fullscreen toggles native macOS full screen (resize ok, first call fills
    // the screen, second call restores — all ok:true). Headless: pinned refusal throughout.
    [Fact]
    public void WindowFullscreen()
    {
        var resized = SendCommand("""{"cmd":"window.resize","args":{"width":800,"height":600}}""");
        Assert.False(resized["ok"]?.GetValue<bool>());
        Assert.Equal(WindowManagementUnavailable, resized["error"]?.GetValue<string>());

        // upstream: ok:true + the window fills the display.
        var entered = SendCommand("""{"cmd":"window.fullscreen"}""");
        Assert.False(entered["ok"]?.GetValue<bool>(), $"headless window.fullscreen must refuse: {entered.ToJsonString()}");
        Assert.Equal(WindowManagementUnavailable, entered["error"]?.GetValue<string>());

        // upstream: a second call (ok:true) exits full screen and restores the frame.
        var exited = SendCommand("""{"cmd":"window.fullscreen"}""");
        Assert.False(exited["ok"]?.GetValue<bool>());
        Assert.Equal(WindowManagementUnavailable, exited["error"]?.GetValue<string>());
    }

    // MARK: - Title-bar gestures (PORT-GAP: no cursor, no on-screen window headless)

    // PORT-GAP: the double-click-header GESTURE (a real mouse event zooming the window) needs an
    // on-screen window and a driven cursor — nothing to click headless. Swift body:
    //   let window = app.windows.firstMatch
    //   sendCommand({"cmd":"window.resize","args":{"width":800,"height":600}}) == ok
    //   let normal = window.frame.size  // settles near 800x600
    //   emptyHeaderPoint(window).doubleClick()
    //   // window grows past normal+50, then a second double-click restores toward normal
    [Fact(Skip = "PORT-GAP: macOS AppKit title-bar double-click gesture; Windows uses WinUI window chrome.")]
    public void DoubleClickHeaderZoomsAndRestores()
    {
    }

    // PORT-GAP: pins the gesture to the macOS Desktop & Dock "double-click title bar: Do Nothing"
    // system setting (env-overridden in setUp) — no system setting or gesture headless. Swift body:
    //   sendCommand({"cmd":"window.resize","args":{"width":800,"height":600}}) == ok
    //   emptyHeaderPoint(window).doubleClick()
    //   // with 'None' set, the frame must NOT change (width/height within 8pt of normal)
    [Fact(Skip = "PORT-GAP: macOS Desktop & Dock 'Do Nothing' title-bar setting has no Windows equivalent.")]
    public void DoubleClickHeaderHonorsNoneSetting()
    {
    }

    // PORT-GAP: hit-testing the WindowControlArea layer against the toolbar buttons is pure
    // on-screen UI (quick-terminal-toggle button click vs header double-click). Swift body:
    //   emptyHeaderPoint(window).doubleClick()
    //   XCTAssertFalse(cover.waitForExistence(2))  // must not open the quick terminal
    //   app.buttons["quick-terminal-toggle"].click()
    //   XCTAssertTrue(cover.waitForExistence(5))   // the button still takes its click
    [Fact(Skip = "PORT-GAP: macOS AppKit header hit-testing gesture; Windows uses WinUI window chrome.")]
    public void HeaderButtonsStillReceiveClicksOverControlArea()
    {
    }

    // PORT-GAP: a click-drag on the custom header moving the window is a cursor gesture over an
    // on-screen frame. Swift body:
    //   sendCommand({"cmd":"window.resize","args":{"width":800,"height":600}}) == ok
    //   sendCommand({"cmd":"window.move","args":{"x":140,"y":140}}) == ok
    //   from.click(forDuration: 0.3, thenDragTo: to, ...)
    //   // window.frame.origin moves by more than 20pt
    [Fact(Skip = "PORT-GAP: macOS AppKit header drag gesture; Windows delegates caption dragging to WinUI.")]
    public void DragHeaderMovesWindow()
    {
    }

    // MARK: - Multi-window targeting (collapsed onto the single window where the semantics hold)

    // Upstream: window.new → window B; tree --window B succeeds while open; window.close B → ok;
    // then tree --window B → "window not open — window.select it first".
    // Headless: window.new refuses (pinned), so the routing round-trip runs against the one open
    // window instead; window.close of the only window resolves the target then answers the pinned
    // refusal (upstream: ok) — the closed-window error path is unreachable because the single
    // window is always open (a documented divergence).
    [Fact]
    public void ClosedWindowTargetingErrors()
    {
        // upstream: sendCommand({"cmd":"window.new"}) carries result.id (window B).
        var created = SendCommand("""{"cmd":"window.new"}""");
        Assert.False(created["ok"]?.GetValue<bool>(), $"headless window.new must refuse: {created.ToJsonString()}");
        Assert.Equal(WindowManagementUnavailable, created["error"]?.GetValue<string>());

        // routing into the still-open window works (upstream: tree --window B while open).
        var windowId = WindowList()[0]!["id"]!.GetValue<string>();
        var openTree = SendCommand($$$"""{"cmd":"tree","args":{"window":"{{{windowId}}}"}}""");
        Assert.True(openTree["ok"]?.GetValue<bool>(), $"tree --window should succeed while open: {openTree.ToJsonString()}");

        // upstream: window.close --target B → ok, then the list marks B open:false.
        var closed = SendCommand($$$"""{"cmd":"window.close","target":"{{{windowId}}}"}""");
        Assert.False(closed["ok"]?.GetValue<bool>(), $"headless window.close must refuse: {closed.ToJsonString()}");
        Assert.Equal(WindowManagementUnavailable, closed["error"]?.GetValue<string>());

        // upstream: tree --window B now fails with "window not open — window.select it first".
        // Headless: the only window is never closed, so targeting it keeps succeeding.
        var after = SendCommand($$$"""{"cmd":"tree","args":{"window":"{{{windowId}}}"}}""");
        Assert.True(after["ok"]?.GetValue<bool>(), $"the single window stays open and targetable: {after.ToJsonString()}");
    }

    // Upstream: session.new --window B lands in B's tree (two sessions) and NOT in frontmost A's
    // (still one). Headless: no window B — the portable half is the routing itself: session.new
    // with an explicit --window id resolves the window gate and lands in that window's tree.
    [Fact]
    public void WindowTargetingRoutesToTheRightTree()
    {
        var initial = WindowList();
        var windowA = initial[0]!["id"]!.GetValue<string>(); // the seeded window id

        // upstream: window.new → windowB, then session.new targets B; headless targets A.
        var created = SendCommand("""{"cmd":"window.new"}""");
        Assert.False(created["ok"]?.GetValue<bool>());
        Assert.Equal(WindowManagementUnavailable, created["error"]?.GetValue<string>());

        // add a session to the window by id (upstream: to window B).
        var added = SendCommand($$$"""{"cmd":"session.new","args":{"window":"{{{windowA}}}"}}""");
        Assert.True(added["ok"]?.GetValue<bool>(), $"session.new --window should succeed: {added.ToJsonString()}");

        // the targeted window's tree now holds two sessions (the seeded one + the new one).
        // upstream polls; headless state is authoritative at command return.
        var workspaces = WindowTreeWorkspaces(windowA);
        Assert.Equal(2, workspaces[0]!["sessions"]!.AsArray().Count);
        // upstream also asserts the OTHER window's tree stayed at one session — no window B headless.
    }

    // Upstream: a session id captured from window B resolves with no --window even while window A
    // is frontmost (the cross-window resolver arm). Headless: the resolver collapses to the single
    // store — the captured id still resolves with no --window, select echoes it, and the tree
    // read-back marks it active. window.select of the (already frontmost) single window is an
    // immediate ok+id, so the upstream selectWindowUntilActive loop collapses to one call.
    [Fact]
    public void CapturedIDResolvesWhileAnotherWindowFrontmost()
    {
        var initial = WindowList();
        var windowA = initial[0]!["id"]!.GetValue<string>(); // the seeded window id

        // upstream: window.new → window B (where the session gets created).
        var created = SendCommand("""{"cmd":"window.new"}""");
        Assert.False(created["ok"]?.GetValue<bool>());
        Assert.Equal(WindowManagementUnavailable, created["error"]?.GetValue<string>());

        // capture a session id created with an explicit --window (upstream: in window B).
        var added = SendCommand($$$"""{"cmd":"session.new","args":{"window":"{{{windowA}}}"}}""");
        Assert.True(added["ok"]?.GetValue<bool>(), $"session.new should succeed: {added.ToJsonString()}");
        var sessionId = added["result"]!["id"]!.GetValue<string>();

        // raise the window (upstream: selectWindowUntilActive(windowA)); the single window is
        // already frontmost, so select answers ok+id and the active flag reads back true at once.
        var raised = SendCommand($$$"""{"cmd":"window.select","target":"{{{windowA}}}"}""");
        Assert.True(raised["ok"]?.GetValue<bool>(), $"window.select of the one window should succeed: {raised.ToJsonString()}");
        Assert.True(WindowList()[0]!["active"]?.GetValue<bool>(), "the window should be active");

        // select the captured id with NO --window: it resolves (upstream: cross-window to B's store).
        var selected = SendCommand($$$"""{"cmd":"session.select","target":"{{{sessionId}}}"}""");
        Assert.True(selected["ok"]?.GetValue<bool>(),
            $"selecting the captured id with no --window should succeed: {selected.ToJsonString()}");
        Assert.Equal(sessionId, selected["result"]?["id"]?.GetValue<string>());

        // confirm it actually selected in the window's tree (upstream: pollTreeActiveSession in B).
        Assert.True(TreeSessionActive(windowA, sessionId),
            "the captured session should be active in the window's tree");
    }

    // Upstream: a WORKSPACE id captured from window B resolves cross-window with no --window (the
    // workspace resolver arm, distinct from the session one). Headless: same collapse — a workspace
    // created with an explicit --window resolves by captured id with no --window.
    [Fact]
    public void CapturedWorkspaceIDResolvesCrossWindow()
    {
        // upstream: window.new → window B (where the workspace gets created).
        var created = SendCommand("""{"cmd":"window.new"}""");
        Assert.False(created["ok"]?.GetValue<bool>());
        Assert.Equal(WindowManagementUnavailable, created["error"]?.GetValue<string>());

        var windowId = WindowList()[0]!["id"]!.GetValue<string>();

        // create a workspace with an explicit --window and capture its id (upstream: in window B).
        var madeWs = SendCommand($$$"""{"cmd":"workspace.new","args":{"window":"{{{windowId}}}","name":"betaws"}}""");
        Assert.True(madeWs["ok"]?.GetValue<bool>(), $"workspace.new --window should succeed: {madeWs.ToJsonString()}");
        var workspaceId = madeWs["result"]!["id"]!.GetValue<string>();

        // select the captured workspace id with NO --window (upstream: resolves cross-window to B).
        var selected = SendCommand($$$"""{"cmd":"workspace.select","target":"{{{workspaceId}}}"}""");
        Assert.True(selected["ok"]?.GetValue<bool>(),
            $"selecting the captured workspace id should succeed: {selected.ToJsonString()}");
        Assert.Equal(workspaceId, selected["result"]?["id"]?.GetValue<string>());
    }

    // Upstream: an unknown id with no --window is searched across ALL open windows and returns the
    // structured not-found error. Headless: the cross-window search collapses to the single store —
    // the pinned "no such session: <id>" string is identical (ControlResolve.ErrorMessage).
    [Fact]
    public void CrossWindowUnknownIDErrors()
    {
        // upstream: window.new → window B (so the miss spans two windows).
        var created = SendCommand("""{"cmd":"window.new"}""");
        Assert.False(created["ok"]?.GetValue<bool>());
        Assert.Equal(WindowManagementUnavailable, created["error"]?.GetValue<string>());

        var bogus = Guid.NewGuid().ToString("D").ToUpperInvariant(); // Swift UUID().uuidString
        var response = SendCommand($$$"""{"cmd":"session.select","target":"{{{bogus}}}"}""");
        Assert.False(response["ok"]?.GetValue<bool>(),
            $"an id matching no session should fail: {response.ToJsonString()}");
        Assert.Equal($"no such session: {bogus}", response["error"]?.GetValue<string>());
    }

    // Upstream: after closing the frontmost (just-created) window B, exactly one open window
    // remains and it is flagged active (the frontmost invariant survives a close). Headless: no
    // window B to create or close — window.new refuses (pinned), and the final invariant (exactly
    // one open window, exactly one active, same id) holds verbatim on the seeded window.
    [Fact]
    public void RemainingWindowBecomesActiveAfterClosingFrontmost()
    {
        // upstream: window.new → window B, then window.close --target B → ok.
        var created = SendCommand("""{"cmd":"window.new"}""");
        Assert.False(created["ok"]?.GetValue<bool>());
        Assert.Equal(WindowManagementUnavailable, created["error"]?.GetValue<string>());

        // the invariant the upstream test settles on: exactly one open window, exactly one active
        // window, and they are the same window.
        var list = WindowList();
        var open = list.Where(w => w!["open"]?.GetValue<bool>() == true).ToArray();
        var active = list.Where(w => w!["active"]?.GetValue<bool>() == true).ToArray();
        Assert.True(open.Length == 1 && active.Length == 1
                && open[0]!["id"]!.GetValue<string>() == active[0]!["id"]!.GetValue<string>(),
            $"the remaining open window should be the single active window: {list.ToJsonString()}");
    }

    // MARK: - Window oracles (the upstream windowList/windowTreeWorkspaces helpers)

    /// <summary>Sends window.list and returns the windows array.</summary>
    private JsonArray WindowList()
    {
        var result = SendOk("""{"cmd":"window.list"}""");
        var windows = result["windows"];
        Assert.True(windows is JsonArray, $"window.list should return windows: {result.ToJsonString()}");
        return (JsonArray)windows!;
    }

    /// <summary>Sends tree --window and returns its workspaces array.</summary>
    private JsonArray WindowTreeWorkspaces(string window)
    {
        var response = SendCommand($$$"""{"cmd":"tree","args":{"window":"{{{window}}}"}}""");
        Assert.True(response["ok"]?.GetValue<bool>(), $"tree --window should succeed: {response.ToJsonString()}");
        return response["result"]!["tree"]!["workspaces"]!.AsArray();
    }

    /// <summary>Whether the session with <paramref name="sessionId"/> is marked active in the
    /// window's tree (the upstream pollTreeActiveSession, single-read: the host is synchronous).</summary>
    private bool TreeSessionActive(string window, string sessionId)
    {
        foreach (var workspace in WindowTreeWorkspaces(window))
        {
            foreach (var session in workspace!["sessions"]!.AsArray())
            {
                if (string.Equals(session!["id"]?.GetValue<string>(), sessionId,
                        StringComparison.OrdinalIgnoreCase)
                    && session["active"]?.GetValue<bool>() == true)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
