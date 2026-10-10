using System.Text.Json;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Protocol;

// Ported from agtermCore ControlProtocolTests.swift (pickCommandsRoundTrip, pickResultRoundTripsEveryOutcomeShape,
// controlResultPickOmitsWhenNil, controlTreePickPendingOmitsWhenNil, controlPickItemSubtitleOmitsWhenNil).
public class ControlPickTests
{
    [Theory]
    [InlineData("pick.open", Command.PickOpen)]
    [InlineData("pick.result", Command.PickResult)]
    [InlineData("pick.cancel", Command.PickCancel)]
    public void PickCommandsRoundTrip(string wire, Command command)
    {
        Assert.Equal(command, JsonSerializer.Deserialize<Command>($"\"{wire}\"", ControlJson.Options));
        Assert.Equal($"\"{wire}\"", JsonSerializer.Serialize(command, ControlJson.Options));
    }

    [Fact]
    public void PickResultRoundTripsEveryOutcomeShape()
    {
        var shapes = new (ControlPickResult Result, string Json)[]
        {
            (ControlPickResult.Pending(), "{\"result\":\"pending\"}"),
            (ControlPickResult.Picked("second", "Second choice", 1), "{\"result\":\"picked\",\"id\":\"second\",\"label\":\"Second choice\",\"index\":1}"),
            (ControlPickResult.Custom("A custom answer"), "{\"result\":\"custom\",\"query\":\"A custom answer\"}"),
            (ControlPickResult.Cancelled(), "{\"result\":\"cancelled\"}"),
        };
        foreach (var (result, json) in shapes)
        {
            Assert.Equal(json, ControlJson.Serialize(result));
            Assert.Equal(result, ControlJson.Deserialize<ControlPickResult>(json));
        }
    }

    [Fact]
    public void ResultPickIsOmittedWhenNullAndFollowsEvents()
    {
        Assert.Equal("{\"id\":\"x\"}", ControlJson.Serialize(new ControlResult { Id = "x" }));
        Assert.Equal("{\"pick\":{\"result\":\"pending\"}}",
            ControlJson.Serialize(new ControlResult { Pick = ControlPickResult.Pending() }));
    }

    [Fact]
    public void TreePickPendingIsOmittedWhenNullAndFollowsDashboardFontMode()
    {
        var tree = new ControlTree { Workspaces = Array.Empty<ControlWorkspaceNode>(), DashboardFontMode = "auto", PickPending = "P" };
        Assert.Equal("{\"workspaces\":[],\"dashboardFontMode\":\"auto\",\"pickPending\":\"P\"}", ControlJson.Serialize(tree));
        Assert.Equal("{\"workspaces\":[]}", ControlJson.Serialize(new ControlTree { Workspaces = Array.Empty<ControlWorkspaceNode>() }));
    }

    [Fact]
    public void ItemSubtitleIsOmittedWhenNull()
    {
        Assert.Equal("{\"id\":\"a\",\"label\":\"A\"}", ControlJson.Serialize(new ControlPickItem { Id = "a", Label = "A" }));
        Assert.Equal("{\"id\":\"a\",\"label\":\"A\",\"subtitle\":\"s\"}",
            ControlJson.Serialize(new ControlPickItem { Id = "a", Label = "A", Subtitle = "s" }));
    }

    [Fact]
    public void ItemWithoutIdOrLabelFailsToDecode()
    {
        Assert.ThrowsAny<JsonException>(() => ControlJson.Deserialize<ControlPickItem>("{\"label\":\"A\"}"));
        Assert.ThrowsAny<JsonException>(() => ControlJson.Deserialize<ControlPickItem>("{\"id\":\"a\"}"));
    }

    [Fact]
    public void AdoptedResultAndArgsExistUpstream()
    {
        Assert.All(ParitySurface.Adopted("result"), name => Assert.Contains(name, ParitySurface.Upstream("result")));
    }
}
