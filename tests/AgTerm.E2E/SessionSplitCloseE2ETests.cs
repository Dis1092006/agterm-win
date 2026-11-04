using System.Text.Json.Nodes;
using Xunit;

namespace AgTerm.E2E;

/// <summary>session.split.close and the tree field hasSplit over the control pipe (upstream 79852e71,
/// a5856949).</summary>
public class SessionSplitCloseE2ETests : ControlApiTestCase
{
    private JsonObject FirstSession() =>
        Tree()["tree"]!["workspaces"]![0]!["sessions"]![0]!.AsObject();

    [Fact]
    public void Close_removes_a_hidden_split_and_the_tree_stops_reporting_it()
    {
        SendOk("""{"cmd":"session.split","args":{"mode":"on"}}""");
        Assert.True(FirstSession()["hasSplit"]!.GetValue<bool>());

        SendOk("""{"cmd":"session.split","args":{"mode":"off"}}""");
        Assert.False(FirstSession()["split"]!.GetValue<bool>());
        Assert.True(FirstSession()["hasSplit"]!.GetValue<bool>());

        var closed = SendOk("""{"cmd":"session.split.close"}""");

        Assert.Equal(FirstSession()["id"]!.GetValue<string>(), closed["id"]!.GetValue<string>());
        Assert.False(FirstSession().ContainsKey("hasSplit"));
        Assert.False(FirstSession()["split"]!.GetValue<bool>());
    }

    [Fact]
    public void Close_without_a_split_is_a_no_op_that_still_answers_the_id()
    {
        var closed = SendOk("""{"cmd":"session.split.close"}""");

        Assert.Equal(FirstSession()["id"]!.GetValue<string>(), closed["id"]!.GetValue<string>());
        Assert.False(FirstSession().ContainsKey("hasSplit"));
    }

    [Fact]
    public void An_unknown_target_is_an_error()
    {
        var bad = SendCommand("""{"cmd":"session.split.close","target":"no-such"}""");

        Assert.False(bad["ok"]!.GetValue<bool>());
    }

    [Fact]
    public void The_cli_closes_the_split()
    {
        SendOk("""{"cmd":"session.split","args":{"mode":"on"}}""");

        Assert.Equal(0, RunCtl("session", "split", "close").Exit);

        Assert.False(FirstSession().ContainsKey("hasSplit"));
    }

    [Fact]
    public void The_cli_keeps_the_default_visibility_form()
    {
        Assert.Equal(0, RunCtl("session", "split", "on").Exit);
        Assert.True(FirstSession()["split"]!.GetValue<bool>());

        Assert.Equal(0, RunCtl("session", "split", "visibility", "off").Exit);
        Assert.False(FirstSession()["split"]!.GetValue<bool>());
        Assert.True(FirstSession()["hasSplit"]!.GetValue<bool>());
    }
}
