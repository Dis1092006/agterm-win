using System.Text.Json;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Protocol;

public class TreeNodeTests
{
    private static List<string> SurfaceFieldNames(string section)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.File("parity/surface.json")));
        return doc.RootElement.GetProperty("control").GetProperty(section)
            .EnumerateArray().Select(f => f.GetProperty("name").GetString()!).ToList();
    }

    private static List<string> PropertyNames(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).ToList();

    private static ControlSessionNode FullSessionNode() => new()
    {
        Id = "id", Name = "n", Cwd = "/w", Title = "t", Active = true, Split = true,
        SplitRatio = 0.4, SplitFocused = true, Overlay = true, OverlaySizePercent = 60,
        Hud = new ControlHudNode { Message = "m", Spinner = "bar", Position = "center", HideAfter = 0 },
        Ask = new ControlSessionAsk { Id = "A", Pane = "left" },
        Scratch = true, Flagged = true, CommandWait = true,
        Foreground = new[] { "ssh", "host" }, SplitForeground = new[] { "top" },
        RestoreCommand = "make", SplitRestoreCommand = "",
        Status = "blocked", StatusPane = "left", StatusBlink = true,
        StatusColor = "#ff0000", StatusShape = "star",
        Background = new BackgroundWatermark { Kind = WatermarkKind.Text, Text = "wip" },
        Unseen = 2, FontSize = 13, SplitFontSize = 14, ScratchFontSize = 15,
        Surfaces = new[] { new ControlSurfaceNode { Id = "surface:x:left", Kind = "left", Active = true, Visible = true } },
    };

    [Fact]
    public void SessionNodeFieldNamesMatchPinPlusAdoptedInUpstreamOrder()
    {
        Assert.All(ParitySurface.Adopted("sessionNode"), name => Assert.Contains(name, ParitySurface.Upstream("sessionNode")));
        Assert.Equal(ParitySurface.Expected("sessionNode", "sessionNode"), PropertyNames(ControlJson.Serialize(FullSessionNode())));
    }

    [Fact]
    public void SessionNodeRoundTrips()
    {
        var json = ControlJson.Serialize(FullSessionNode());
        Assert.Equal(json, ControlJson.Serialize(ControlJson.Deserialize<ControlSessionNode>(json)));
    }

    [Fact]
    public void MinimalSessionNodeOmitsAllOptionals()
    {
        var node = new ControlSessionNode { Id = "i", Name = "n", Cwd = "/", Active = false, Split = false };
        Assert.Equal(
            "{\"id\":\"i\",\"name\":\"n\",\"cwd\":\"/\",\"active\":false,\"split\":false," +
            "\"overlay\":false,\"scratch\":false,\"flagged\":false}",
            ControlJson.Serialize(node));
    }

    [Fact]
    public void WorkspaceNodeFieldNamesMatchParitySurfacePlusWindowsOnlyInOrder()
    {
        var node = new ControlWorkspaceNode
        {
            Id = "i", Name = "n", Active = true, Focused = true, Collapsed = true,
            Sessions = new[] { FullSessionNode() }, Theme = "Nord",
        };
        Assert.Equal(
            SurfaceFieldNames("workspaceNode").Concat(ParitySurface.WindowsOnly("workspaceNode")).ToList(),
            PropertyNames(ControlJson.Serialize(node)));
    }

    [Fact]
    public void WorkspaceNodeOmitsAThemeItDoesNotHave()
    {
        var node = new ControlWorkspaceNode
        {
            Id = "i", Name = "n", Active = true, Sessions = Array.Empty<ControlSessionNode>(),
        };
        Assert.DoesNotContain("theme", PropertyNames(ControlJson.Serialize(node)));
    }

    [Fact]
    public void WindowNodeFieldNamesMatchParitySurfaceInOrder()
    {
        var node = new ControlWindowNode
        {
            Id = "i", Name = "n", Open = true, Active = false, AutoFollowMs = 500,
            SidebarVisible = true,
            Geometry = new ControlWindowFrame { X = 0, Y = 0, Width = 800, Height = 600, Display = 0 },
            Fullscreen = false, Zoomed = false,
        };
        Assert.Equal(SurfaceFieldNames("windowNode"), PropertyNames(ControlJson.Serialize(node)));
    }

    [Fact]
    public void WindowFrameRoundTrips()
    {
        var json = "{\"x\":10,\"y\":-5,\"width\":800,\"height\":600,\"display\":1}";
        Assert.Equal(json, ControlJson.Serialize(ControlJson.Deserialize<ControlWindowFrame>(json)));
    }

    [Fact]
    public void ControlTreeRoundTripsWithTopLevelReadBacks()
    {
        var json = "{\"workspaces\":[{\"id\":\"w\",\"name\":\"work\",\"active\":true,\"sessions\":[]}]," +
                   "\"idleMs\":120,\"autoFollowMs\":1500,\"sidebarVisible\":true,\"sidebarMode\":\"tree\"," +
                   "\"quickVisible\":false,\"zoomedSurface\":\"quick\"," +
                   "\"dashboardMembers\":[\"a:left\",\"a:right\"],\"dashboardHighlighted\":\"a:left\"," +
                   "\"dashboardFontSize\":12,\"dashboardFontMode\":\"fixed\"}";
        var tree = ControlJson.Deserialize<ControlTree>(json)!;

        Assert.Single(tree.Workspaces);
        Assert.Equal("quick", tree.ZoomedSurface);
        Assert.Equal(json, ControlJson.Serialize(tree));
    }

    [Fact]
    public void EmptyControlTreeSerializesWorkspacesOnly()
    {
        Assert.Equal("{\"workspaces\":[]}",
            ControlJson.Serialize(new ControlTree { Workspaces = Array.Empty<ControlWorkspaceNode>() }));
    }
}
