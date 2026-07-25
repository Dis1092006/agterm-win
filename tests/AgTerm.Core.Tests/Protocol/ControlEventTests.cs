using System.Text.Json;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Protocol;

public class ControlEventTests
{
    [Theory]
    [InlineData("status", ControlEventKind.Status)]
    [InlineData("notify", ControlEventKind.Notify)]
    [InlineData("session.created", ControlEventKind.SessionCreated)]
    [InlineData("session.closed", ControlEventKind.SessionClosed)]
    [InlineData("tree.changed", ControlEventKind.TreeChanged)]
    public void EventKindWireStringsRoundTrip(string wire, ControlEventKind kind)
    {
        Assert.Equal(kind, ControlJson.Deserialize<ControlEventKind>($"\"{wire}\""));
        Assert.Equal($"\"{wire}\"", ControlJson.Serialize(kind));
    }

    [Fact]
    public void UnknownEventKindFailsToDecode()
    {
        Assert.Throws<JsonException>(() => ControlJson.Deserialize<ControlEventKind>("\"session.exploded\""));
    }

    [Fact]
    public void EventEncodesPayloadEvenWhenEmpty()
    {
        var evt = new ControlEvent { Seq = 42, Ts = 1721900000.5, Kind = ControlEventKind.TreeChanged };
        Assert.Equal("{\"seq\":42,\"ts\":1721900000.5,\"kind\":\"tree.changed\",\"payload\":{}}",
            ControlJson.Serialize(evt));
    }

    [Fact]
    public void FullEventRoundTrips()
    {
        var json = "{\"seq\":7,\"ts\":1721900000.5,\"kind\":\"status\",\"window\":\"w\"," +
                   "\"workspace\":\"ws\",\"session\":\"s\",\"payload\":{\"name\":\"api\"," +
                   "\"status\":\"blocked\",\"pane\":\"left\",\"blink\":true,\"color\":\"#ff0000\"," +
                   "\"shape\":\"star\",\"title\":\"t\",\"body\":\"b\"}}";
        Assert.Equal(json, ControlJson.Serialize(ControlJson.Deserialize<ControlEvent>(json)));
    }

    [Fact]
    public void BatchEncodesRunAsUppercaseUuid()
    {
        var run = Guid.Parse("5d3c67f2-4e58-4e12-9dfb-0ddd64fec792");
        var batch = new ControlEventBatch { Run = run, Next = 10, Items = Array.Empty<ControlEvent>() };
        Assert.Equal("{\"run\":\"5D3C67F2-4E58-4E12-9DFB-0DDD64FEC792\",\"next\":10,\"items\":[]}",
            ControlJson.Serialize(batch));
    }

    [Fact]
    public void BatchDecodesRunCaseInsensitively()
    {
        var batch = ControlJson.Deserialize<ControlEventBatch>(
            "{\"run\":\"5d3c67f2-4e58-4e12-9dfb-0ddd64fec792\",\"next\":0,\"items\":[]}")!;
        Assert.Equal(Guid.Parse("5D3C67F2-4E58-4E12-9DFB-0DDD64FEC792"), batch.Run);
    }

    [Fact]
    public void CursorErrorStringsMatchUpstreamVerbatim()
    {
        Assert.Equal("event run changed", ControlEventReadError.RunChanged.Message());
        Assert.Equal("event cursor expired", ControlEventReadError.CursorExpired.Message());
        Assert.Equal("event cursor is ahead of the current sequence", ControlEventReadError.CursorAhead.Message());
    }

    [Fact]
    public void RequestValidationErrorStringsMatchUpstreamVerbatim()
    {
        Assert.Equal("events.read requires --run and --after together", ControlEventRequestError.CursorPair);
        Assert.Equal("invalid event cursor", ControlEventRequestError.InvalidCursor);
        Assert.Equal("invalid event run id", ControlEventRequestError.InvalidRun);
        Assert.Equal("event limit must be between 1 and 1000", ControlEventRequestError.InvalidLimit);
        Assert.Equal("invalid event kind: boom", ControlEventRequestError.InvalidKind("boom"));
    }
}
