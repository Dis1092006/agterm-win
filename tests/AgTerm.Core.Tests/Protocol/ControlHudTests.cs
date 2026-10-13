using System.Text.Json;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Protocol;

// Ported from agtermCore ControlProjection.swift ControlHudNode, AppStoreHudTests.swift
// controlTreeReportsHudWithEveryField and ControlDispatcherHudTests.swift
// stickyAndFrameAreOmittedFromTheWireWhenUnsetAndRoundTripWhenSet (upstream ed831ab).
public class ControlHudTests
{
    private static ControlHudNode MinimalNode() => new()
    {
        Message = "m", Spinner = "none", Position = "center", HideAfter = 0,
    };

    private static ControlHudNode FullNode() => new()
    {
        Message = "building", Detail = "step 2 of 5", Spinner = "braille",
        BackgroundColor = "#2a1a3a", TextColor = "#e0e0e0", SizePercent = 35, HeightPercent = 12,
        Position = "top-center", Pane = "left", HideAfter = 2.5, Markdown = true, FontSize = 14.5,
        Sticky = true, Frame = false,
    };

    [Theory]
    [InlineData("session.hud.open", Command.SessionHudOpen)]
    [InlineData("session.hud.update", Command.SessionHudUpdate)]
    [InlineData("session.hud.close", Command.SessionHudClose)]
    public void HudCommandsRoundTrip(string wire, Command command)
    {
        Assert.Equal(command, JsonSerializer.Deserialize<Command>($"\"{wire}\"", ControlJson.Options));
        Assert.Equal($"\"{wire}\"", JsonSerializer.Serialize(command, ControlJson.Options));
    }

    [Fact]
    public void AFullNodeKeepsUpstreamKeyOrder() =>
        Assert.Equal(
            "{\"message\":\"building\",\"detail\":\"step 2 of 5\",\"spinner\":\"braille\"," +
            "\"backgroundColor\":\"#2a1a3a\",\"textColor\":\"#e0e0e0\",\"sizePercent\":35,\"heightPercent\":12," +
            "\"position\":\"top-center\",\"pane\":\"left\",\"hideAfter\":2.5,\"markdown\":true,\"fontSize\":14.5," +
            "\"sticky\":true,\"frame\":false}",
            ControlJson.Serialize(FullNode()));

    [Fact]
    public void AMinimalNodeWritesOnlyTheAlwaysPresentFields() =>
        Assert.Equal(
            "{\"message\":\"m\",\"spinner\":\"none\",\"position\":\"center\",\"hideAfter\":0," +
            "\"markdown\":false,\"sticky\":false,\"frame\":true}",
            ControlJson.Serialize(MinimalNode()));

    [Fact]
    public void TheUpstreamTreeExampleSerializesAsUpstream() =>
        Assert.Equal(
            "{\"message\":\"gathering options\",\"detail\":\"scanning 400 files\",\"spinner\":\"braille\"," +
            "\"backgroundColor\":\"#2a1a3a\",\"textColor\":\"#e0e0e0\",\"sizePercent\":35,\"heightPercent\":12," +
            "\"position\":\"top-center\",\"pane\":\"left\",\"hideAfter\":0,\"markdown\":false,\"sticky\":false,\"frame\":true}",
            ControlJson.Serialize(new ControlHudNode
            {
                Message = "gathering options", Detail = "scanning 400 files", Spinner = "braille",
                BackgroundColor = "#2a1a3a", TextColor = "#e0e0e0", SizePercent = 35, HeightPercent = 12,
                Position = "top-center", Pane = "left", HideAfter = 0,
            }));

    [Fact]
    public void AnEmptyDetailIsWrittenNotOmitted() =>
        Assert.Equal(
            "{\"message\":\"m\",\"detail\":\"\",\"spinner\":\"none\",\"position\":\"center\",\"hideAfter\":0," +
            "\"markdown\":false,\"sticky\":false,\"frame\":true}",
            ControlJson.Serialize(MinimalNode() with { Detail = "" }));

    [Fact]
    public void AFullNodeRoundTrips()
    {
        var json = ControlJson.Serialize(FullNode());
        Assert.Equal(FullNode(), ControlJson.Deserialize<ControlHudNode>(json));
    }

    [Fact]
    public void AbsentMarkdownStickyAndFrameDecodeToAPlainFramedPanel()
    {
        var node = ControlJson.Deserialize<ControlHudNode>(
            "{\"message\":\"m\",\"spinner\":\"none\",\"position\":\"center\",\"hideAfter\":0}")!;
        Assert.False(node.Markdown);
        Assert.False(node.Sticky);
        Assert.True(node.Frame);
    }

    [Theory]
    [InlineData("{\"spinner\":\"none\",\"position\":\"center\",\"hideAfter\":0}")]
    [InlineData("{\"message\":\"m\",\"position\":\"center\",\"hideAfter\":0}")]
    [InlineData("{\"message\":\"m\",\"spinner\":\"none\",\"hideAfter\":0}")]
    [InlineData("{\"message\":\"m\",\"spinner\":\"none\",\"position\":\"center\"}")]
    public void ANodeWithoutARequiredFieldFailsToDecode(string json) =>
        Assert.ThrowsAny<JsonException>(() => ControlJson.Deserialize<ControlHudNode>(json));

    [Fact]
    public void SessionNodeHudSitsBetweenOverlaySizePercentAndAsk()
    {
        var node = new ControlSessionNode
        {
            Id = "i", Name = "n", Cwd = "/", Active = false, Split = false,
            Hud = MinimalNode(), Ask = new ControlSessionAsk { Id = "A" },
        };
        Assert.Equal(
            "{\"id\":\"i\",\"name\":\"n\",\"cwd\":\"/\",\"active\":false,\"split\":false,\"overlay\":false," +
            "\"hud\":{\"message\":\"m\",\"spinner\":\"none\",\"position\":\"center\",\"hideAfter\":0," +
            "\"markdown\":false,\"sticky\":false,\"frame\":true}," +
            "\"ask\":{\"id\":\"A\"},\"scratch\":false,\"flagged\":false}",
            ControlJson.Serialize(node));
    }

    [Fact]
    public void HudArgsFollowTheSwiftDeclarationOrder() =>
        Assert.Equal(
            "{\"color\":\"#111111\",\"textColor\":\"#eeeeee\",\"shape\":\"s\",\"message\":\"m\",\"detail\":\"d\"," +
            "\"spinner\":\"bar\",\"hideAfter\":2.5,\"markdown\":true,\"sticky\":true,\"frame\":false,\"items\":[]}",
            ControlJson.Serialize(new ControlArgs
            {
                Color = "#111111", TextColor = "#eeeeee", Shape = "s", Message = "m", Detail = "d", Spinner = "bar",
                HideAfter = 2.5, Markdown = true, Sticky = true, Frame = false, Items = Array.Empty<ControlPickItem>(),
            }));

    [Fact]
    public void StickyAndFrameAreOmittedFromTheWireWhenUnsetAndRoundTripWhenSet()
    {
        Assert.Equal("{\"cmd\":\"session.hud.open\",\"args\":{\"message\":\"x\"}}",
            ControlJson.Serialize(new ControlRequest { Cmd = Command.SessionHudOpen, Args = new ControlArgs { Message = "x" } }));

        var set = new ControlRequest { Cmd = Command.SessionHudOpen, Args = new ControlArgs { Message = "x", Sticky = true, Frame = false } };
        var json = ControlJson.Serialize(set);
        Assert.Equal("{\"cmd\":\"session.hud.open\",\"args\":{\"message\":\"x\",\"sticky\":true,\"frame\":false}}", json);
        Assert.Equal(set, ControlJson.Deserialize<ControlRequest>(json));
    }

    [Fact]
    public void ErrorStringsAreUpstreamVerbatim()
    {
        Assert.Equal("no hud", HudErrors.NoHud);
        Assert.Equal("a hud is always floating: pass --size-percent, not --full", HudErrors.FullResize);
        Assert.Equal("no overlay result: the slot holds a hud", HudErrors.NoOverlayResult);
        Assert.Equal("hud pane must be left or right", HudErrors.InvalidPane);
        Assert.Equal("hud text must not contain control characters", HudErrors.TextControlCharacters);
    }
}
