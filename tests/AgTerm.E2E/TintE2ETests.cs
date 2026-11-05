using System.Text.Json.Nodes;
using AgTerm.Core.Model;
using Xunit;

namespace AgTerm.E2E;

/// <summary>session.tint, workspace.tint and the creation tint over the control pipe (upstream
/// f3fea57b, ControlServerTintTests).</summary>
public class TintE2ETests : ControlApiTestCase
{
    private JsonObject FirstWorkspace() => Tree()["tree"]!["workspaces"]![0]!.AsObject();

    private JsonObject FirstSession() => FirstWorkspace()["sessions"]![0]!.AsObject();

    private static string[] Parts(JsonNode? node) => node!.AsArray().Select(p => p!.GetValue<string>()).ToArray();

    [Fact]
    public void Session_tint_sets_keeps_the_placement_on_recolor_and_clears()
    {
        var id = FirstSession()["id"]!.GetValue<string>();
        Assert.False(FirstSession().ContainsKey("tint"));

        var placed = SendOk($$$"""{"cmd":"session.tint","target":"{{{id}}}","args":{"mode":"set","tint":"green","tintOn":["name"]}}""");

        Assert.Equal(id, placed["id"]!.GetValue<string>());
        Assert.Equal("green", FirstSession()["tint"]!.GetValue<string>());
        Assert.Equal(["name"], Parts(FirstSession()["tintOn"]));

        SendOk($$$"""{"cmd":"session.tint","target":"{{{id}}}","args":{"mode":"set","tint":"red"}}""");

        Assert.Equal(new SidebarTint(TintColor.Red, TintParts.Name), Host.Store.SessionWithId(Guid.Parse(id))!.Tint);

        SendOk($$$"""{"cmd":"session.tint","target":"{{{id}}}","args":{"mode":"clear"}}""");

        Assert.False(FirstSession().ContainsKey("tint"));
        Assert.False(FirstSession().ContainsKey("tintOn"));
    }

    [Fact]
    public void Session_tint_reports_an_unknown_target()
    {
        var bad = SendCommand($$$"""{"cmd":"session.tint","target":"{{{Guid.NewGuid()}}}","args":{"mode":"set","tint":"red"}}""");

        Assert.False(bad["ok"]!.GetValue<bool>());
    }

    [Fact]
    public void Workspace_tint_colors_only_the_workspace()
    {
        var id = FirstWorkspace()["id"]!.GetValue<string>();

        var response = SendOk($$$"""{"cmd":"workspace.tint","target":"{{{id}}}","args":{"mode":"set","tint":"blue","tintOn":["icon","name"]}}""");

        Assert.Equal(id, response["id"]!.GetValue<string>());
        Assert.Equal("blue", FirstWorkspace()["tint"]!.GetValue<string>());
        Assert.Equal(["icon", "name"], Parts(FirstWorkspace()["tintOn"]));
        Assert.False(FirstSession().ContainsKey("tint"));
    }

    [Fact]
    public void A_rejected_request_returns_the_upstream_error_and_changes_nothing()
    {
        var bad = SendCommand("""{"cmd":"session.tint","args":{"mode":"set","tint":"teal"}}""");

        Assert.False(bad["ok"]!.GetValue<bool>());
        Assert.Equal("invalid tint: teal (red|orange|yellow|green|blue|purple|gray)", bad["error"]!.GetValue<string>());
        Assert.False(FirstSession().ContainsKey("tint"));
    }

    [Fact]
    public void Workspace_and_session_creation_carry_their_creation_tint()
    {
        SendOk("""{"cmd":"workspace.new","args":{"name":"prod","tint":"red","tintOn":["name"]}}""");
        SendOk("""{"cmd":"session.new","args":{"workspaceName":"prod","name":"db","noSelect":true,"tint":"orange"}}""");

        var node = Tree()["tree"]!["workspaces"]!.AsArray().Single(w => w!["name"]!.GetValue<string>() == "prod")!;
        Assert.Equal("red", node["tint"]!.GetValue<string>());
        Assert.Equal(["name"], Parts(node["tintOn"]));
        Assert.Equal("orange", node["sessions"]![0]!["tint"]!.GetValue<string>());
        Assert.Equal(["icon"], Parts(node["sessions"]![0]!["tintOn"]));
    }

    [Fact]
    public void Every_session_placement_path_carries_the_creation_tint()
    {
        var workspaceId = FirstWorkspace()["id"]!.GetValue<string>();

        var byId = SendOk($$$"""{"cmd":"session.new","args":{"workspace":"{{{workspaceId}}}","name":"by-id","noSelect":true,"tint":"blue"}}""");
        var anchor = byId["id"]!.GetValue<string>();
        SendOk($$$"""{"cmd":"session.new","args":{"after":"{{{anchor}}}","name":"by-anchor","noSelect":true,"tint":"gray","tintOn":["name"]}}""");
        SendOk("""{"cmd":"workspace.new","args":{"name":"named"}}""");
        SendOk("""{"cmd":"session.new","args":{"workspaceName":"named","name":"by-name","noSelect":true,"tint":"purple"}}""");
        SendOk("""{"cmd":"session.new","args":{"workspaceName":"fresh","createWorkspace":true,"name":"by-create","noSelect":true,"tint":"yellow"}}""");

        var sessions = Host.Store.Workspaces.SelectMany(w => w.Sessions).ToList();
        Assert.Equal(new SidebarTint(TintColor.Blue, TintParts.Icon), sessions.Single(s => s.CustomName == "by-id").Tint);
        Assert.Equal(new SidebarTint(TintColor.Gray, TintParts.Name), sessions.Single(s => s.CustomName == "by-anchor").Tint);
        Assert.Equal(new SidebarTint(TintColor.Purple, TintParts.Icon), sessions.Single(s => s.CustomName == "by-name").Tint);
        Assert.Equal(new SidebarTint(TintColor.Yellow, TintParts.Icon), sessions.Single(s => s.CustomName == "by-create").Tint);
    }

    [Fact]
    public void The_cli_tints_creates_tinted_and_clears()
    {
        Assert.Equal(0, RunCtl("session", "tint", "Purple", "--on", "icon,name").Exit);
        Assert.Equal("purple", FirstSession()["tint"]!.GetValue<string>());
        Assert.Equal(["icon", "name"], Parts(FirstSession()["tintOn"]));

        Assert.Equal(0, RunCtl("session", "tint", "clear").Exit);
        Assert.False(FirstSession().ContainsKey("tint"));

        Assert.Equal(0, RunCtl("workspace", "new", "ops", "--tint", "green", "--tint-on", "name").Exit);
        var ops = Tree()["tree"]!["workspaces"]!.AsArray().Single(w => w!["name"]!.GetValue<string>() == "ops")!;
        Assert.Equal("green", ops["tint"]!.GetValue<string>());
        Assert.Equal(["name"], Parts(ops["tintOn"]));

        var bad = RunCtl("workspace", "tint", "teal");
        Assert.NotEqual(0, bad.Exit);
        Assert.Contains("tint must be one of: red|orange|yellow|green|blue|purple|gray, or clear", bad.Error);
    }
}
