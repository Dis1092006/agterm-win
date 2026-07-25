using System.Text.Json;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Protocol;

public class ControlRequestResponseTests
{
    [Fact]
    public void RequestRoundTripsWithTargetAndArgs()
    {
        var json = "{\"cmd\":\"session.new\",\"target\":\"active\",\"args\":{\"cwd\":\"/tmp\"}}";
        var request = ControlJson.Deserialize<ControlRequest>(json)!;

        Assert.Equal(Command.SessionNew, request.Cmd);
        Assert.Equal("active", request.Target);
        Assert.Equal("/tmp", request.Args!.Cwd);
        Assert.Equal(json, ControlJson.Serialize(request));
    }

    [Fact]
    public void BareRequestSerializesToCmdOnly()
    {
        Assert.Equal("{\"cmd\":\"tree\"}", ControlJson.Serialize(new ControlRequest { Cmd = Command.Tree }));
    }

    [Fact]
    public void RequestWithUnknownCmdFailsToDecode()
    {
        Assert.Throws<JsonException>(() =>
            ControlJson.Deserialize<ControlRequest>("{\"cmd\":\"no.such\"}"));
    }

    [Fact]
    public void RequestWithMissingCmdFailsToDecode()
    {
        Assert.Throws<JsonException>(() =>
            ControlJson.Deserialize<ControlRequest>("{\"target\":\"active\"}"));
    }

    [Fact]
    public void ErrorResponseRoundTrips()
    {
        var json = "{\"ok\":false,\"error\":\"not found\"}";
        var response = ControlJson.Deserialize<ControlResponse>(json)!;

        Assert.False(response.Ok);
        Assert.Equal("not found", response.Error);
        Assert.Null(response.Result);
        Assert.Equal(json, ControlJson.Serialize(response));
    }

    [Fact]
    public void SuccessResponseWithIdRoundTrips()
    {
        var json = "{\"ok\":true,\"result\":{\"id\":\"5D3C67F2-4E58-4E12-9DFB-0DDD64FEC792\"}}";
        var response = ControlJson.Deserialize<ControlResponse>(json)!;

        Assert.True(response.Ok);
        Assert.Equal("5D3C67F2-4E58-4E12-9DFB-0DDD64FEC792", response.Result!.Id);
        Assert.Equal(json, ControlJson.Serialize(response));
    }

    [Fact]
    public void SharedErrorStringsMatchUpstreamVerbatim()
    {
        Assert.Equal("overlay still running", OverlayResultError.StillRunning);
        Assert.Equal("no overlay result", OverlayResultError.NoResult);
        Assert.Equal(
            "badge updated, but \"Show notification banners\" is off, so no banner was posted",
            ControlNotify.BannersOffNote);
    }
}

public class ControlResultTests
{
    [Fact]
    public void EmptyResultSerializesToEmptyObject()
    {
        Assert.Equal("{}", ControlJson.Serialize(new ControlResult()));
    }

    [Fact]
    public void ScalarFieldsRoundTrip()
    {
        var json = "{\"id\":\"abc\",\"text\":\"2 of 5\",\"exitCode\":1,\"count\":5,\"affected\":2," +
                   "\"theme\":\"nord\",\"themes\":[\"nord\",\"agterm\"],\"ratio\":0.35}";
        var result = ControlJson.Deserialize<ControlResult>(json)!;

        Assert.Equal("abc", result.Id);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(5, result.Count);
        Assert.Equal(2, result.Affected);
        Assert.Equal(0.35, result.Ratio);
        Assert.Equal(json, ControlJson.Serialize(result));
    }

    [Fact]
    public void ThemePairFieldsRoundTrip()
    {
        var json = "{\"sync\":true,\"light\":\"day\",\"dark\":\"night\"}";
        Assert.Equal(json, ControlJson.Serialize(ControlJson.Deserialize<ControlResult>(json)));
    }
}
