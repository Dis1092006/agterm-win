using System.Text.Json.Nodes;
using AgTerm.ControlServer;
using Xunit;

namespace AgTerm.E2E.Ported;

/// <summary>
/// Port of agtermUITests/ControlSurfaceZoomUITests.swift onto the headless E2E fixture. Request
/// JSON literals are byte-identical to the upstream Swift. The upstream visual observables (the
/// "terminal-zoom-exit" button, the zoom cover, the traffic-light chrome) map to the control
/// read-back the zoom controller feeds the tree: the top-level <c>zoomedSurface</c> id
/// ("surface:&lt;UUID&gt;:kind" or "quick"), omitted while nothing is zoomed. The headless host is
/// synchronous, so the upstream bounded polls collapse to single reads.
/// </summary>
public class ControlSurfaceZoomTests : ControlApiTestCase
{
    [Fact]
    public void SurfaceZoomRendersAndMouseExitPreservesSplitRatio()
    {
        var originalSessionId = ActiveSessionId();
        var created = SendCommand("""{"cmd":"session.new"}""");
        Assert.True(created["ok"]?.GetValue<bool>(),
            $"creating a second session should succeed: {created.ToJsonString()}");
        Assert.True(
            SendCommand($$$"""{"cmd":"session.select","target":"{{{originalSessionId}}}"}""")["ok"]?.GetValue<bool>(),
            "selecting the original session should succeed");

        var split = SendCommand("""{"cmd":"session.split","args":{"mode":"on"}}""");
        Assert.True(split["ok"]?.GetValue<bool>(), $"split on should succeed: {split.ToJsonString()}");

        var resize = SendCommand("""{"cmd":"session.resize","args":{"ratio":0.33}}""");
        Assert.True(resize["ok"]?.GetValue<bool>(), $"split resize should succeed: {resize.ToJsonString()}");
        // upstream pollSplitRatio: the ratio lands asynchronously in the live app; the headless
        // host applies it synchronously, so one tree read replaces the poll.
        Assert.Equal(0.33, ActiveSession()["splitRatio"]?.GetValue<double>());

        var leftSurface = ActiveSurfaceId("left");
        var zoom = SendCommand(
            $$$"""{"cmd":"surface.zoom","target":"{{{leftSurface}}}","args":{"mode":"show"}}""");
        Assert.True(zoom["ok"]?.GetValue<bool>(), $"surface zoom show should succeed: {zoom.ToJsonString()}");

        // upstream: the "terminal-zoom-exit" button appears and the traffic-light close control
        // stays visible — visual zoom-cover assertions. The headless read-back for "zoom is
        // rendered" is the tree echoing the zoomed surface id.
        Assert.Equal(leftSurface, TreeZoomedSurface());

        // PORT-GAP: upstream types Cmd-J and Ctrl-Tab through the real keyboard to prove mutating
        // shortcuts are inert while zoomed — there is no key-event path into the headless host.
        // The state read-backs behind those keystrokes still pin that the hidden session state is
        // untouched while zoomed.
        //   app.typeKey("j", modifierFlags: .command)
        //   app.typeKey("\t", modifierFlags: .control)
        Assert.False(ActiveSessionFlag("scratch"),
            "hidden session state should not change while zoomed");
        Assert.Equal(originalSessionId, ActiveSessionId());

        // upstream clicks the mouse-friendly zoom-exit button; the control-plane equivalent of
        // that exit is surface.zoom hide on the active target.
        Assert.True(SendCommand("""{"cmd":"surface.zoom","args":{"mode":"hide"}}""")["ok"]?.GetValue<bool>(),
            "exiting zoom should succeed");
        Assert.Null(TreeZoomedSurface()); // the zoom cover is gone
        Assert.Equal(0.33, ActiveSession()["splitRatio"]?.GetValue<double>()); // zoom round-trip must not change the split ratio
    }

    [Fact]
    public void ScratchOpenedWhileZoomedRealizesSurface()
    {
        var leftSurface = ActiveSurfaceId("left");
        var zoom = SendCommand(
            $$$"""{"cmd":"surface.zoom","target":"{{{leftSurface}}}","args":{"mode":"show"}}""");
        Assert.True(zoom["ok"]?.GetValue<bool>(), $"surface zoom show should succeed: {zoom.ToJsonString()}");
        // upstream waits for the zoom-exit button; headless: the tree reports the zoom as active.
        Assert.Equal(leftSurface, TreeZoomedSurface());

        var scratch = SendCommand("""{"cmd":"session.scratch","args":{"mode":"on"}}""");
        Assert.True(scratch["ok"]?.GetValue<bool>(),
            $"scratch on should succeed while zoomed: {scratch.ToJsonString()}");
        // upstream's deck keeps realizing surfaces behind the zoom layer (pollScratchReadable spins
        // until session.text --pane scratch answers). The headless host never fabricates surfaces —
        // attaching the FakeSurface stands in for the deck's realization — and the same
        // control-observable proof applies: the scratch must be readable without exiting zoom.
        Host.Store.SessionWithId(Guid.Parse(ActiveSessionId()))!.ScratchSurface = new FakeSurface();
        var text = SendCommand("""{"cmd":"session.text","args":{"pane":"scratch"}}""");
        Assert.True(text["ok"]?.GetValue<bool>(),
            $"a control-opened scratch must be readable while the session is zoomed: {text.ToJsonString()}");

        Assert.True(SendCommand("""{"cmd":"surface.zoom","args":{"mode":"hide"}}""")["ok"]?.GetValue<bool>(),
            "zoom hide should succeed");
        // hide is idempotent for explicit targets too: hiding an already-un-zoomed surface is an ok no-op.
        var rehide = SendCommand(
            $$$"""{"cmd":"surface.zoom","target":"{{{leftSurface}}}","args":{"mode":"hide"}}""");
        Assert.True(rehide["ok"]?.GetValue<bool>(),
            $"explicit-target hide should be idempotent: {rehide.ToJsonString()}");
    }

    [Fact]
    public void QuickZoomEmitsAndAcceptsQuickTargetId()
    {
        Assert.True(SendCommand("""{"cmd":"quick","args":{"mode":"show"}}""")["ok"]?.GetValue<bool>(),
            "quick show should succeed");

        var zoom = SendCommand("""{"cmd":"surface.zoom","args":{"mode":"show"}}""");
        Assert.True(zoom["ok"]?.GetValue<bool>(), $"surface zoom show should succeed: {zoom.ToJsonString()}");
        Assert.Equal("quick", zoom["result"]?["id"]?.GetValue<string>());

        // the API must accept the address it just emitted: hide the zoom by its returned id.
        var hide = SendCommand("""{"cmd":"surface.zoom","target":"quick","args":{"mode":"hide"}}""");
        Assert.True(hide["ok"]?.GetValue<bool>(),
            $"the id surface.zoom emitted must be accepted back as a target: {hide.ToJsonString()}");

        // zoom the quick terminal again and dismiss it with plain `quick hide`: dismissal must stay
        // unconditional for scripts — a zoomed quick terminal exits its zoom first, never an error.
        Assert.True(SendCommand("""{"cmd":"surface.zoom","args":{"mode":"show"}}""")["ok"]?.GetValue<bool>(),
            "re-zooming the quick terminal should succeed");
        var quickHide = SendCommand("""{"cmd":"quick","args":{"mode":"hide"}}""");
        Assert.True(quickHide["ok"]?.GetValue<bool>(),
            $"quick hide must succeed while the quick terminal is zoomed: {quickHide.ToJsonString()}");
        // upstream waits for the zoom-exit button to vanish; headless: hiding the zoomed quick
        // terminal must have exited zoom in the tree read-back.
        Assert.Null(TreeZoomedSurface());
    }

    [Fact]
    public void ZoomedSurfaceTreeReadBackAndScopedErrorPaths()
    {
        // unzoomed: the tree must OMIT zoomedSurface (nil closure result → key absent, not null).
        Assert.False(Tree()["tree"]!.AsObject().ContainsKey("zoomedSurface"),
            "zoomedSurface should be absent while nothing is zoomed");

        // zoom the active session's left surface and read the SAME id back from the tree top level —
        // the record-then-restore leg of the write-only surface.zoom command.
        var leftSurface = ActiveSurfaceId("left");
        var zoom = SendCommand(
            $$$"""{"cmd":"surface.zoom","target":"{{{leftSurface}}}","args":{"mode":"show"}}""");
        Assert.True(zoom["ok"]?.GetValue<bool>(), $"surface zoom show should succeed: {zoom.ToJsonString()}");
        Assert.Equal(leftSurface, TreeZoomedSurface());

        Assert.True(SendCommand("""{"cmd":"surface.zoom","args":{"mode":"hide"}}""")["ok"]?.GetValue<bool>(),
            "zoom hide should succeed");
        Assert.False(Tree()["tree"]!.AsObject().ContainsKey("zoomedSurface"),
            "zoomedSurface should clear on zoom exit");

        // error paths. a well-formed id whose session exists nowhere → no such surface.
        var ghost = "surface:00000000-0000-0000-0000-000000000000:left";
        var noSuch = SendCommand(
            $$$"""{"cmd":"surface.zoom","target":"{{{ghost}}}","args":{"mode":"show"}}""");
        Assert.False(noSuch["ok"]?.GetValue<bool>(),
            $"zooming an unknown surface should fail: {noSuch.ToJsonString()}");
        Assert.Equal($"no such surface: {ghost}", noSuch["error"]?.GetValue<string>());

        // a malformed target → invalid surface.
        var invalid = SendCommand("""{"cmd":"surface.zoom","target":"not-a-surface","args":{"mode":"show"}}""");
        Assert.False(invalid["ok"]?.GetValue<bool>(),
            $"a malformed target should fail: {invalid.ToJsonString()}");
        Assert.Equal("invalid surface: not-a-surface", invalid["error"]?.GetValue<string>());

        // PORT-GAP: the remaining upstream legs need a SECOND window (window.new "zoom-err"), and
        // the headless host is single-window by design — window.new answers the documented refusal
        // ("window management is not available in the headless host"), so the window-scoped error
        // paths are unreachable here:
        //   surface.zoom --window <other window> on a surface it does not own
        //       → "no such surface: <leftSurface>"
        //   window.close <other window>, then surface.zoom --window <closed window>
        //       → "window not open — window.select it first"
    }

    [Fact(Skip = "PORT-GAP: a live multi-window keyboard test must exit zoom in a background " +
                 "window and assert typed text still reaches the frontmost window's search field.")]
    public void BackgroundWindowZoomExitDoesNotStealFrontmostFocus()
    {
        // Upstream body, for reference:
        //   windowA = window.list → first id; zoom windowA's left surface (surface.zoom show)
        //   {"cmd":"window.new","args":{"name":"zoom-focus"}} → window B materializes and takes key
        //   {"cmd":"session.search"} in B; click the search field; type "ab" → field owns keyboard
        //   {"cmd":"surface.zoom","args":{"mode":"hide","window":"<windowA>"}} → ok
        //   type "cd" → field reads "abcd": a background window's zoom exit is scoped to window A
        //   and must not steal the frontmost window's keyboard focus (the pinned regression).
    }

    /// <summary>The tree's top-level zoomedSurface, or null when omitted (nothing zoomed).</summary>
    private string? TreeZoomedSurface() =>
        Tree()["tree"]!.AsObject()["zoomedSurface"]?.GetValue<string>();

    private string ActiveSurfaceId(string kind)
    {
        foreach (var surface in ActiveSession()["surfaces"]!.AsArray())
        {
            if (surface!["kind"]?.GetValue<string>() == kind)
                return surface["id"]!.GetValue<string>();
        }
        Assert.Fail($"active session should expose a {kind} surface");
        return null!;
    }

    private bool ActiveSessionFlag(string key) =>
        ActiveSession()[key]?.GetValue<bool>() ?? false;

    private string ActiveSessionId() => ActiveSession()["id"]!.GetValue<string>();

    private JsonObject ActiveSession()
    {
        var tree = Tree()["tree"]!.AsObject();
        foreach (var workspace in tree["workspaces"]!.AsArray())
        {
            foreach (var session in workspace!["sessions"]!.AsArray())
            {
                if (session!["active"]?.GetValue<bool>() == true) return session.AsObject();
            }
        }
        Assert.Fail("tree should expose an active session");
        return null!;
    }
}
