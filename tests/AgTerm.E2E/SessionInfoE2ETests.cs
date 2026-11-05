using System.Text.Json.Nodes;
using Xunit;

namespace AgTerm.E2E;

/// <summary>session.info and the tree fields info, infoMarkdown and infoHideAfter over the control pipe
/// (upstream 80f822bf).</summary>
public class SessionInfoE2ETests : ControlApiTestCase
{
    private JsonObject FirstSession() =>
        Tree()["tree"]!["workspaces"]![0]!["sessions"]![0]!.AsObject();

    [Fact]
    public void Set_reports_the_line_in_the_tree_and_clear_removes_it()
    {
        Assert.False(FirstSession().ContainsKey("info"));

        var set = SendOk("""{"cmd":"session.info","args":{"mode":"set","text":"3 agents, 1 blocked","markdown":false,"hideAfter":0}}""");

        var session = FirstSession();
        Assert.Equal(session["id"]!.GetValue<string>(), set["id"]!.GetValue<string>());
        Assert.Equal("3 agents, 1 blocked", session["info"]!.GetValue<string>());
        Assert.False(session["infoMarkdown"]!.GetValue<bool>());
        Assert.Equal(0, session["infoHideAfter"]!.GetValue<double>());

        SendOk("""{"cmd":"session.info","args":{"mode":"clear"}}""");

        session = FirstSession();
        Assert.False(session.ContainsKey("info"));
        Assert.False(session.ContainsKey("infoMarkdown"));
        Assert.False(session.ContainsKey("infoHideAfter"));
    }

    [Fact]
    public void Markdown_and_hide_after_are_read_back()
    {
        SendOk("""{"cmd":"session.info","args":{"mode":"set","text":"**build** ^[ok](color: \"green\")","markdown":true,"hideAfter":30}}""");

        var session = FirstSession();
        Assert.Equal("**build** ^[ok](color: \"green\")", session["info"]!.GetValue<string>());
        Assert.True(session["infoMarkdown"]!.GetValue<bool>());
        Assert.Equal(30, session["infoHideAfter"]!.GetValue<double>());
    }

    [Fact]
    public void A_rejected_request_returns_the_upstream_error_and_keeps_the_line()
    {
        SendOk("""{"cmd":"session.info","args":{"mode":"set","text":"kept"}}""");

        var bad = SendCommand("""{"cmd":"session.info","args":{"mode":"set","text":"a\nb"}}""");

        Assert.False(bad["ok"]!.GetValue<bool>());
        Assert.Equal("session.info: info text must be one line", bad["error"]!.GetValue<string>());
        Assert.Equal("kept", FirstSession()["info"]!.GetValue<string>());
    }

    [Fact]
    public void An_unknown_target_is_an_error()
    {
        var bad = SendCommand("""{"cmd":"session.info","target":"no-such","args":{"mode":"set","text":"x"}}""");

        Assert.False(bad["ok"]!.GetValue<bool>());
        Assert.Equal("no such session: no-such", bad["error"]!.GetValue<string>());
    }

    [Fact]
    public void The_cli_sets_and_clears_the_line()
    {
        var set = RunCtl("session", "info", "^[ok](color: \"green\")", "--markdown", "--hide-after", "60");

        Assert.Equal(0, set.Exit);
        var session = FirstSession();
        Assert.Equal("^[ok](color: \"green\")", session["info"]!.GetValue<string>());
        Assert.True(session["infoMarkdown"]!.GetValue<bool>());
        Assert.Equal(60, session["infoHideAfter"]!.GetValue<double>());

        Assert.Equal(0, RunCtl("session", "info", "--clear").Exit);
        Assert.False(FirstSession().ContainsKey("info"));

        var bad = RunCtl("session", "info", "^[ok](color: green)", "--markdown");
        Assert.NotEqual(0, bad.Exit);
        Assert.Contains("session.info: invalid info markdown; quote attribute values", bad.Error);
    }
}
