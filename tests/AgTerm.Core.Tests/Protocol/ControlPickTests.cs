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
    public void AdoptedResultFieldsExistUpstream()
    {
        Assert.All(ParitySurface.Pin("result"), name => Assert.Contains(name, ParitySurface.Upstream("result")));
        Assert.All(ParitySurface.Adopted("result"), name => Assert.Contains(name, ParitySurface.Upstream("result")));
    }

    [Fact]
    public void ResultPropertyNamesMatchPinPlusAdoptedInUpstreamOrder()
    {
        var result = new ControlResult
        {
            Id = "s", Tree = new ControlTree { Workspaces = Array.Empty<ControlWorkspaceNode>() }, Text = "s",
            Windows = Array.Empty<ControlWindowNode>(), ExitCode = 1, Count = 1, Affected = 1, Theme = "s",
            Themes = new[] { "a" }, Ratio = 0.5, Pane = "s", Sync = true, Light = "s", Dark = "s",
            Events = new ControlEventBatch { Run = Guid.NewGuid(), Next = 1, Items = Array.Empty<ControlEvent>() },
            Keymap = new ControlKeymap
            {
                Path = "s", Actions = Array.Empty<ControlKeymapAction>(),
                Commands = Array.Empty<ControlKeymapCommand>(), Diagnostics = Array.Empty<ControlKeymapDiagnostic>(),
            },
            Pick = ControlPickResult.Pending(),
            Ask = ControlAskResult.Pending(),
        };

        using var serialized = JsonDocument.Parse(ControlJson.Serialize(result));
        var actual = serialized.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(ParitySurface.Expected("result", "result"), actual);
    }
}
