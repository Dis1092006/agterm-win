using AgTerm.ControlServer;
using Xunit;

namespace AgTerm.E2E;

/// <summary>surface.cursor over the control pipe and the agtermctl CLI (upstream 2279c722).</summary>
public class SurfaceCursorE2ETests : ControlApiTestCase
{
    private void GiveActiveSessionASurface(int? column, string token = "tok")
    {
        var session = Host.Store.ActiveSession!;
        session.Surface = new FakeSurface { PaneToken = token, CursorColumn = column };
    }

    [Fact]
    public void The_wire_command_answers_the_column_and_the_surface_id()
    {
        GiveActiveSessionASurface(7);

        var result = SendOk("""{"cmd":"surface.cursor"}""");

        Assert.Equal(7, result["cursor"]!["column"]!.GetValue<int>());
        Assert.StartsWith("surface:", result["id"]!.GetValue<string>());
    }

    [Fact]
    public void The_cli_prints_the_column_alone()
    {
        GiveActiveSessionASurface(12);

        var (exit, output, _) = RunCtl("surface", "cursor");

        Assert.Equal(0, exit);
        Assert.Equal("12", output.Trim());
    }

    [Fact]
    public void The_cli_resolves_a_pane_token_against_the_session()
    {
        GiveActiveSessionASurface(3, "pane-tok");

        var (exit, output, _) = RunCtl("surface", "cursor", "--pane-id", "pane-tok");

        Assert.Equal(0, exit);
        Assert.Equal("3", output.Trim());
    }

    [Fact]
    public void An_unknown_pane_token_is_an_error()
    {
        GiveActiveSessionASurface(3);

        var bad = SendCommand("""{"cmd":"surface.cursor","args":{"paneID":"nope"}}""");

        Assert.False(bad["ok"]!.GetValue<bool>());
        Assert.Equal("unknown pane id: nope", bad["error"]!.GetValue<string>());
    }
}
