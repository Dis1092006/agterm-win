using System.Text.Json;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Protocol;

// Ported from agtermCore AskTests.swift (result encoding) and ControlProtocolTests.swift ask cases (upstream ed831ab).
public class ControlAskTests
{
    [Theory]
    [InlineData("ask.open", Command.AskOpen)]
    [InlineData("ask.result", Command.AskResult)]
    [InlineData("ask.cancel", Command.AskCancel)]
    public void AskCommandsRoundTrip(string wire, Command command)
    {
        Assert.Equal(command, JsonSerializer.Deserialize<Command>($"\"{wire}\"", ControlJson.Options));
        Assert.Equal($"\"{wire}\"", JsonSerializer.Serialize(command, ControlJson.Options));
    }

    [Fact]
    public void AskResultRoundTripsEveryOutcomeShape()
    {
        var shapes = new (ControlAskResult Result, string Json)[]
        {
            (ControlAskResult.Pending(), "{\"result\":\"pending\"}"),
            (ControlAskResult.Answered("yes", "Yes", 0), "{\"result\":\"answered\",\"id\":\"yes\",\"label\":\"Yes\",\"index\":0}"),
            (ControlAskResult.Escaped(), "{\"result\":\"escaped\"}"),
            (ControlAskResult.Cancelled(), "{\"result\":\"cancelled\"}"),
        };
        foreach (var (result, json) in shapes)
        {
            Assert.Equal(json, ControlJson.Serialize(result));
            Assert.Equal(result, ControlJson.Deserialize<ControlAskResult>(json));
        }
    }

    [Fact]
    public void AResultFromAServerWithAReasonStillDecodes() =>
        Assert.Equal(ControlAskResult.Cancelled(),
            ControlJson.Deserialize<ControlAskResult>("{\"result\":\"cancelled\",\"reason\":\"presentation-lost\"}"));

    [Fact]
    public void ResultPaneFollowsRatioAndAskFollowsPick()
    {
        Assert.Equal("{\"id\":\"x\"}", ControlJson.Serialize(new ControlResult { Id = "x" }));
        Assert.Equal("{\"id\":\"x\",\"ratio\":0.5,\"pane\":\"right\"}",
            ControlJson.Serialize(new ControlResult { Id = "x", Ratio = 0.5, Pane = "right" }));
        Assert.Equal("{\"pick\":{\"result\":\"pending\"},\"ask\":{\"result\":\"escaped\"}}",
            ControlJson.Serialize(new ControlResult { Pick = ControlPickResult.Pending(), Ask = ControlAskResult.Escaped() }));
    }

    [Fact]
    public void TreeAskPendingFollowsPickPendingAndIsOmittedWhenNull()
    {
        var tree = new ControlTree { Workspaces = Array.Empty<ControlWorkspaceNode>(), PickPending = "P", AskPending = "A" };
        Assert.Equal("{\"workspaces\":[],\"pickPending\":\"P\",\"askPending\":\"A\"}", ControlJson.Serialize(tree));
        Assert.Equal("{\"workspaces\":[]}", ControlJson.Serialize(new ControlTree { Workspaces = Array.Empty<ControlWorkspaceNode>() }));
    }

    [Fact]
    public void SessionAskOmitsPaneForASessionWideQuestion()
    {
        Assert.Equal("{\"id\":\"A\"}", ControlJson.Serialize(new ControlSessionAsk { Id = "A" }));
        Assert.Equal("{\"id\":\"A\",\"pane\":\"left\"}", ControlJson.Serialize(new ControlSessionAsk { Id = "A", Pane = "left" }));
    }

    [Fact]
    public void SessionNodeAskSitsBetweenOverlaySizePercentAndScratch()
    {
        var node = new ControlSessionNode
        {
            Id = "i", Name = "n", Cwd = "/", Active = false, Split = false,
            Overlay = true, OverlaySizePercent = 50, Ask = new ControlSessionAsk { Id = "A" },
        };
        Assert.Equal(
            "{\"id\":\"i\",\"name\":\"n\",\"cwd\":\"/\",\"active\":false,\"split\":false," +
            "\"overlay\":true,\"overlaySizePercent\":50,\"ask\":{\"id\":\"A\"},\"scratch\":false,\"flagged\":false}",
            ControlJson.Serialize(node));
    }

    [Fact]
    public void ButtonHotkeyIsOmittedWhenNull()
    {
        Assert.Equal("{\"id\":\"y\",\"label\":\"Yes\"}", ControlJson.Serialize(new ControlAskButton { Id = "y", Label = "Yes" }));
        Assert.Equal("{\"id\":\"y\",\"label\":\"Yes\",\"hotkey\":\"y\"}",
            ControlJson.Serialize(new ControlAskButton { Id = "y", Label = "Yes", Hotkey = "y" }));
    }

    [Fact]
    public void ButtonWithoutIdOrLabelFailsToDecode()
    {
        Assert.ThrowsAny<JsonException>(() => ControlJson.Deserialize<ControlAskButton>("{\"label\":\"A\"}"));
        Assert.ThrowsAny<JsonException>(() => ControlJson.Deserialize<ControlAskButton>("{\"id\":\"a\"}"));
    }

    [Theory]
    [InlineData("terminal", ControlAskStyle.Terminal)]
    [InlineData("gui", ControlAskStyle.Gui)]
    public void StyleParsesItsWireNames(string wire, ControlAskStyle expected) =>
        Assert.Equal(expected, ControlAskStyles.FromWire(wire));

    [Theory]
    [InlineData("left", ControlAskAlignment.Left)]
    [InlineData("center", ControlAskAlignment.Center)]
    [InlineData("right", ControlAskAlignment.Right)]
    public void AlignParsesItsWireNames(string wire, ControlAskAlignment expected) =>
        Assert.Equal(expected, ControlAskAlignments.FromWire(wire));

    [Theory]
    [InlineData("")]
    [InlineData("GUI")]
    [InlineData("other")]
    public void UnknownStyleAndAlignParseToNull(string wire)
    {
        Assert.Null(ControlAskStyles.FromWire(wire));
        Assert.Null(ControlAskAlignments.FromWire(wire));
    }
}
