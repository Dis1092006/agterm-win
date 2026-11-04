using System.Text.Json.Nodes;
using Xunit;

namespace AgTerm.E2E;

/// <summary>session.context and the tree fields context, realized and splitCwd over the control pipe
/// (upstream 73b63b71, 8e67daa4, 16af9dc3).</summary>
public class SessionContextE2ETests : ControlApiTestCase
{
    private JsonObject FirstSession() =>
        Tree()["tree"]!["workspaces"]![0]!["sessions"]![0]!.AsObject();

    [Fact]
    public void Set_reports_the_trimmed_text_in_the_tree_and_clear_removes_it()
    {
        Assert.False(FirstSession().ContainsKey("context"));

        var set = SendOk("""{"cmd":"session.context","args":{"mode":"set","text":"  PR #517: reap ordering  "}}""");

        Assert.Equal(FirstSession()["id"]!.GetValue<string>(), set["id"]!.GetValue<string>());
        Assert.Equal("PR #517: reap ordering", FirstSession()["context"]!.GetValue<string>());

        SendOk("""{"cmd":"session.context","args":{"mode":"clear"}}""");

        Assert.False(FirstSession().ContainsKey("context"));
    }

    [Fact]
    public void A_rejected_request_returns_the_upstream_error_and_changes_nothing()
    {
        var bad = SendCommand("""{"cmd":"session.context","args":{"mode":"set","text":"PR\n517"}}""");

        Assert.False(bad["ok"]!.GetValue<bool>());
        Assert.Equal("context must not contain control characters or line breaks", bad["error"]!.GetValue<string>());
        Assert.False(FirstSession().ContainsKey("context"));
    }

    [Fact]
    public void An_unknown_target_is_an_error()
    {
        var bad = SendCommand("""{"cmd":"session.context","target":"no-such","args":{"mode":"set","text":"x"}}""");

        Assert.False(bad["ok"]!.GetValue<bool>());
    }

    [Fact]
    public void Realized_follows_the_main_pane_surface()
    {
        var id = Guid.Parse(FirstSession()["id"]!.GetValue<string>());
        var surface = new AgTerm.ControlServer.FakeSurface();
        Host.Store.SessionWithId(id)!.Surface = surface;

        Assert.True(FirstSession()["realized"]!.GetValue<bool>());

        surface.Realized = false;

        Assert.False(FirstSession()["realized"]!.GetValue<bool>());
    }

    [Fact]
    public void The_cli_sets_and_clears_the_context()
    {
        var set = RunCtl("session", "context", "  issue 12  ");

        Assert.Equal(0, set.Exit);
        Assert.Equal("issue 12", FirstSession()["context"]!.GetValue<string>());

        Assert.Equal(0, RunCtl("session", "context", "--clear").Exit);
        Assert.False(FirstSession().ContainsKey("context"));

        var bad = RunCtl("session", "context");
        Assert.NotEqual(0, bad.Exit);
        Assert.Contains("provide exactly one of a TEXT or --clear", bad.Error);
    }

    [Fact]
    public void The_tree_always_reports_realized_and_omits_splitCwd_without_a_split()
    {
        var session = FirstSession();

        Assert.True(session.ContainsKey("realized"));
        Assert.IsAssignableFrom<JsonValue>(session["realized"]);
        Assert.False(session.ContainsKey("splitCwd"));
    }
}
