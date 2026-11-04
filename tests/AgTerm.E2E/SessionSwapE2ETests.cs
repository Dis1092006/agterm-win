using System.Text.Json.Nodes;
using Xunit;

namespace AgTerm.E2E;

/// <summary>session.swap over the control pipe (upstream db62080d).</summary>
public class SessionSwapE2ETests : ControlApiTestCase
{
    private JsonObject FirstSession() =>
        Tree()["tree"]!["workspaces"]![0]!["sessions"]![0]!.AsObject();

    // headless surfaces never realize on their own; attaching fakes stands in for the panes being live
    private void SplitWithPanes()
    {
        SendOk("""{"cmd":"session.split","args":{"mode":"on"}}""");
        var session = Host.Store.SessionWithId(Guid.Parse(FirstSession()["id"]!.GetValue<string>()))!;
        session.Surface = new AgTerm.ControlServer.FakeSurface();
        session.SplitSurface = new AgTerm.ControlServer.FakeSurface();
    }

    [Fact]
    public void Swap_answers_the_session_id_and_keeps_the_split()
    {
        SplitWithPanes();

        var swapped = SendOk("""{"cmd":"session.swap"}""");

        Assert.Equal(FirstSession()["id"]!.GetValue<string>(), swapped["id"]!.GetValue<string>());
        Assert.True(FirstSession()["split"]!.GetValue<bool>());
        Assert.True(FirstSession()["hasSplit"]!.GetValue<bool>());
    }

    [Fact]
    public void Swap_works_on_a_hidden_split()
    {
        SplitWithPanes();
        SendOk("""{"cmd":"session.split","args":{"mode":"off"}}""");

        SendOk("""{"cmd":"session.swap"}""");

        Assert.False(FirstSession()["split"]!.GetValue<bool>());
        Assert.True(FirstSession()["hasSplit"]!.GetValue<bool>());
    }

    [Fact]
    public void Swap_without_a_split_is_an_error()
    {
        var bad = SendCommand("""{"cmd":"session.swap"}""");

        Assert.False(bad["ok"]!.GetValue<bool>());
        Assert.Equal("session has no split pane", bad["error"]!.GetValue<string>());
    }

    [Fact]
    public void An_unknown_target_is_an_error()
    {
        var bad = SendCommand("""{"cmd":"session.swap","target":"no-such"}""");

        Assert.False(bad["ok"]!.GetValue<bool>());
    }

    [Fact]
    public void The_cli_swaps_the_panes()
    {
        SplitWithPanes();

        Assert.Equal(0, RunCtl("session", "swap").Exit);
    }

    [Fact]
    public void The_cli_fails_without_a_split()
    {
        Assert.NotEqual(0, RunCtl("session", "swap").Exit);
    }
}
