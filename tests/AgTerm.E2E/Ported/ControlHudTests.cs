using System.Text.Json.Nodes;
using Xunit;

namespace AgTerm.E2E.Ported;

/// <summary>
/// Wire scenarios for session.hud.* and for the session.overlay.* commands against a HUD, ported from
/// the HUD cases of agtermTests/ControlServerSessionActionsTests.swift (upstream ed831ab). Request lines
/// are verbatim JSON, so these pin the dispatcher, the headless actions and the tree projection
/// together. The helper and body file cases have no port: the port draws the HUD in the page.
/// </summary>
public class ControlHudTests : ControlApiTestCase
{
    [Fact]
    public void HudOpenUpdateCloseReadBackOverTheWire()
    {
        var id = CreateSession();

        var open = SendCommand($$$"""{"cmd":"session.hud.open","target":"{{{id}}}","args":{"message":"gathering options","detail":"scanning 400 files","spinner":"braille","color":"#2a1a3a","textColor":"#e0e0e0","sizePercent":35,"position":"top"}}""");

        Assert.Equal($$$"""{"ok":true,"result":{"id":"{{{id}}}"}}""", open.ToJsonString());
        Assert.Equal(
            """{"message":"gathering options","detail":"scanning 400 files","spinner":"braille","backgroundColor":"#2a1a3a","textColor":"#e0e0e0","sizePercent":35,"heightPercent":10,"position":"top-center","hideAfter":0,"markdown":false,"sticky":false,"frame":true}""",
            SessionNode(id)["hud"]!.ToJsonString());
        Assert.False(SessionNode(id)["overlay"]!.GetValue<bool>());
        Assert.Null(SessionNode(id)["overlaySizePercent"]);

        var update = SendCommand($$$"""{"cmd":"session.hud.update","target":"{{{id}}}","args":{"message":"done","hideAfter":600}}""");

        Assert.Equal($$$"""{"ok":true,"result":{"id":"{{{id}}}"}}""", update.ToJsonString());
        Assert.Equal(
            """{"message":"done","spinner":"none","backgroundColor":"#2a1a3a","sizePercent":80,"heightPercent":10,"position":"center","hideAfter":600,"markdown":false,"sticky":false,"frame":true}""",
            SessionNode(id)["hud"]!.ToJsonString());

        var close = SendCommand($$$"""{"cmd":"session.hud.close","target":"{{{id}}}"}""");

        Assert.Equal($$$"""{"ok":true,"result":{"id":"{{{id}}}"}}""", close.ToJsonString());
        Assert.Null(SessionNode(id)["hud"]);
        var again = SendCommand($$$"""{"cmd":"session.hud.close","target":"{{{id}}}"}""");
        Assert.Equal("no hud", again["error"]?.GetValue<string>());
    }

    // the dispatcher hands close to the host with no checks, so invalid values never reach an error
    [Fact]
    public void HudCloseIgnoresEveryOtherArgument()
    {
        var response = SendCommand("""{"cmd":"session.hud.close","target":"active","args":{"message":"","color":"not a color","position":"nowhere"}}""");

        Assert.Equal("no hud", response["error"]?.GetValue<string>());
    }

    [Fact]
    public void TheDispatcherRefusesAMissingMessageAndStageOneMarkdown()
    {
        var noMessage = SendCommand("""{"cmd":"session.hud.open","target":"active"}""");
        Assert.Equal("session.hud.open requires a message", noMessage["error"]?.GetValue<string>());

        var markdown = SendCommand("""{"cmd":"session.hud.open","target":"active","args":{"message":"**ok**","markdown":true}}""");
        Assert.Equal("session.hud.open: --markdown is not supported yet", markdown["error"]?.GetValue<string>());
        Assert.Null(ActiveSessionNode()["hud"]);
    }

    [Fact]
    public void OverlayCommandsAgainstAHud()
    {
        var id = CreateSession();
        SendOk($$$"""{"cmd":"session.hud.open","target":"{{{id}}}","args":{"message":"working"}}""");

        SendOk($$$"""{"cmd":"session.overlay.resize","target":"{{{id}}}","args":{"sizePercent":5}}""");
        Assert.Equal(10, SessionNode(id)["hud"]!["sizePercent"]!.GetValue<int>());

        var full = SendCommand($$$"""{"cmd":"session.overlay.resize","target":"{{{id}}}","args":{"full":true}}""");
        Assert.Equal("a hud is always floating: pass --size-percent, not --full", full["error"]?.GetValue<string>());
        Assert.Equal(10, SessionNode(id)["hud"]!["sizePercent"]!.GetValue<int>());

        var result = SendCommand($$$"""{"cmd":"session.overlay.result","target":"{{{id}}}"}""");
        Assert.Equal("no overlay result: the slot holds a hud", result["error"]?.GetValue<string>());

        SendOk($$$"""{"cmd":"session.overlay.close","target":"{{{id}}}"}""");
        Assert.Null(SessionNode(id)["hud"]);
        var again = SendCommand($$$"""{"cmd":"session.overlay.close","target":"{{{id}}}"}""");
        Assert.Equal("no overlay", again["error"]?.GetValue<string>());
    }

    [Fact]
    public void OverlayOpenTakesTheSlotFromAHudAndAHudCannotTakeItBack()
    {
        var id = CreateSession();
        SendOk($$$"""{"cmd":"session.hud.open","target":"{{{id}}}","args":{"message":"working"}}""");

        SendOk($$$"""{"cmd":"session.overlay.open","target":"{{{id}}}","args":{"command":"cat"}}""");

        Assert.True(SessionNode(id)["overlay"]!.GetValue<bool>());
        Assert.Null(SessionNode(id)["hud"]);
        var refused = SendCommand($$$"""{"cmd":"session.hud.open","target":"{{{id}}}","args":{"message":"again"}}""");
        Assert.Equal("overlay already open", refused["error"]?.GetValue<string>());
        var close = SendCommand($$$"""{"cmd":"session.hud.close","target":"{{{id}}}"}""");
        Assert.Equal("no hud", close["error"]?.GetValue<string>());
        Assert.True(SessionNode(id)["overlay"]!.GetValue<bool>());
    }

    [Fact]
    public void APaneHudReadsBackItsCurrentRoleAndSurvivesAHiddenSplit()
    {
        var id = CreateSession();
        SendOk($$$"""{"cmd":"session.split","target":"{{{id}}}","args":{"mode":"on"}}""");
        SendOk($$$"""{"cmd":"session.hud.open","target":"{{{id}}}","args":{"message":"building","pane":"split"}}""");
        Assert.Equal("right", SessionNode(id)["hud"]!["pane"]!.GetValue<string>());

        SendOk($$$"""{"cmd":"session.focus","target":"{{{id}}}","args":{"pane":"left"}}""");
        SendOk($$$"""{"cmd":"session.split","target":"{{{id}}}","args":{"mode":"off"}}""");

        var hidden = SendCommand($$$"""{"cmd":"session.hud.open","target":"{{{id}}}","args":{"message":"again","pane":"right"}}""");
        Assert.Equal("pane not visible", hidden["error"]?.GetValue<string>());
        SendOk($$$"""{"cmd":"session.hud.update","target":"{{{id}}}","args":{"message":"still building","pane":"right"}}""");
        Assert.Equal("right", SessionNode(id)["hud"]!["pane"]!.GetValue<string>());
    }

    private string CreateSession()
    {
        var created = SendCommand("""{"cmd":"session.new"}""");
        var id = (created["result"] as JsonObject)?["id"]?.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(id), $"session.new should return the new id: {created.ToJsonString()}");
        return id!;
    }

    private JsonObject SessionNode(string id)
    {
        foreach (var workspace in Tree()["tree"]!["workspaces"]!.AsArray())
        {
            foreach (var session in workspace!["sessions"]!.AsArray())
            {
                if (string.Equals(session!["id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase))
                    return session.AsObject();
            }
        }
        Assert.Fail($"session {id} not in tree");
        return null!;
    }

    private JsonObject ActiveSessionNode()
    {
        foreach (var workspace in Tree()["tree"]!["workspaces"]!.AsArray())
        {
            foreach (var session in workspace!["sessions"]!.AsArray())
            {
                if (session!["active"]?.GetValue<bool>() == true) return session.AsObject();
            }
        }
        Assert.Fail("no active session in tree");
        return null!;
    }
}
