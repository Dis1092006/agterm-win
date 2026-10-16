using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using AgTerm.ControlServer;
using Xunit;

namespace AgTerm.E2E;

/// <summary>workspace.theme over the control pipe (Windows-only, parity/windows-only.json). The
/// fixture host bundles no themes, so each test starts a second host over the same state dir with
/// a small theme list, as ControlApiThemeTests does.</summary>
public class WorkspaceThemeE2ETests : ControlApiTestCase
{
    private HeadlessHost ThemedHost()
    {
        var host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = StateDir,
            ConfigDir = StateDir,
            PipeName = "agterm-e2e-" + Guid.NewGuid().ToString("N"),
            AvailableThemes = new[] { "agterm", "Nord" },
        });
        host.Start();
        return host;
    }

    private static JsonObject Send(HeadlessHost host, string line)
    {
        using var pipe = new NamedPipeClientStream(".", host.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        pipe.Connect(10_000);
        pipe.Write(Encoding.UTF8.GetBytes(line + "\n"));
        pipe.Flush();
        using var reader = new StreamReader(pipe, Encoding.UTF8);
        return (JsonObject)JsonNode.Parse(reader.ReadLine()!)!;
    }

    private static JsonObject Workspace(HeadlessHost host, string name) =>
        Send(host, """{"cmd":"tree"}""")["result"]!["tree"]!["workspaces"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(node => node["name"]!.GetValue<string>() == name);

    [Fact]
    public void Sets_and_clears_the_active_workspace_theme_and_tree_reports_it_last()
    {
        using var host = ThemedHost();
        var before = Workspace(host, "workspace 1");
        Assert.False(before.ContainsKey("theme"));

        var set = Send(host, """{"cmd":"workspace.theme","args":{"name":"Nord"}}""");

        Assert.True(set["ok"]!.GetValue<bool>(), set.ToJsonString());
        Assert.Equal(before["id"]!.GetValue<string>(), set["result"]!["id"]!.GetValue<string>());
        var themed = Workspace(host, "workspace 1");
        Assert.Equal("theme", themed.Select(p => p.Key).Last());
        Assert.Equal("Nord", themed["theme"]!.GetValue<string>());

        var cleared = Send(host, """{"cmd":"workspace.theme","target":"active"}""");

        Assert.True(cleared["ok"]!.GetValue<bool>(), cleared.ToJsonString());
        Assert.False(Workspace(host, "workspace 1").ContainsKey("theme"));
    }

    [Fact]
    public void A_blank_name_clears_the_theme()
    {
        using var host = ThemedHost();
        Send(host, """{"cmd":"workspace.theme","args":{"name":"Nord"}}""");

        var cleared = Send(host, """{"cmd":"workspace.theme","args":{"name":"   "}}""");

        Assert.True(cleared["ok"]!.GetValue<bool>(), cleared.ToJsonString());
        Assert.False(Workspace(host, "workspace 1").ContainsKey("theme"));
    }

    [Fact]
    public void A_name_outside_the_theme_list_is_refused_with_the_theme_set_error()
    {
        using var host = ThemedHost();

        var refused = Send(host, """{"cmd":"workspace.theme","args":{"name":"NotARealTheme"}}""");

        Assert.False(refused["ok"]!.GetValue<bool>());
        Assert.Equal("unknown theme: NotARealTheme", refused["error"]!.GetValue<string>());
        Assert.False(Workspace(host, "workspace 1").ContainsKey("theme"));
    }

    [Fact]
    public void The_target_resolves_like_workspace_rename()
    {
        using var host = ThemedHost();
        var created = Send(host, """{"cmd":"workspace.new","args":{"name":"second"}}""");
        var id = created["result"]!["id"]!.GetValue<string>();

        var byId = Send(host, $$$"""{"cmd":"workspace.theme","target":"{{{id}}}","args":{"name":"Nord"}}""");
        var renameMiss = Send(host, """{"cmd":"workspace.rename","target":"nope","args":{"name":"x"}}""");
        var themeMiss = Send(host, """{"cmd":"workspace.theme","target":"nope","args":{"name":"Nord"}}""");

        Assert.True(byId["ok"]!.GetValue<bool>(), byId.ToJsonString());
        Assert.Equal("Nord", Workspace(host, "second")["theme"]!.GetValue<string>());
        Assert.False(Workspace(host, "workspace 1").ContainsKey("theme"));
        Assert.False(themeMiss["ok"]!.GetValue<bool>());
        Assert.Equal(renameMiss["error"]!.GetValue<string>(), themeMiss["error"]!.GetValue<string>());
    }

    [Fact]
    public void The_theme_survives_a_restart()
    {
        using (var first = ThemedHost())
        {
            Send(first, """{"cmd":"workspace.theme","args":{"name":"Nord"}}""");
        }

        using var second = ThemedHost();

        Assert.Equal("Nord", Workspace(second, "workspace 1")["theme"]!.GetValue<string>());
    }

    [Fact]
    public void Agtermctl_prints_what_workspace_rename_prints_and_reports_an_unknown_theme()
    {
        // the fixture host lists no themes: a clear succeeds, any name is unknown
        var renamed = RunCtl("workspace", "rename", "renamed");
        var cleared = RunCtl("workspace", "theme");
        var refused = RunCtl("workspace", "theme", "Nord");

        Assert.Equal(0, renamed.Exit);
        Assert.Equal(0, cleared.Exit);
        Assert.Equal(renamed.Out, cleared.Out);
        Assert.NotEqual(0, refused.Exit);
        Assert.Contains("unknown theme: Nord", refused.Error);
    }
}
