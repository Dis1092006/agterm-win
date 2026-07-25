using System.Text.Json;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported 1:1 from agtermCore/Tests/agtermCoreTests/ControlEventProtocolTests.swift.</summary>
public class ControlEventProtocolTests
{
    private static readonly Guid Run = Guid.Parse("CBB5E3D0-7A9B-4C96-9EA2-18B14380DDB1");

    private static readonly (ControlEventKind Kind, string Wire)[] AllKinds =
    {
        (ControlEventKind.Status, "status"),
        (ControlEventKind.Notify, "notify"),
        (ControlEventKind.SessionCreated, "session.created"),
        (ControlEventKind.SessionClosed, "session.closed"),
        (ControlEventKind.TreeChanged, "tree.changed"),
    };

    private static ControlEvent[] SampleEvents(string? statusShape = null) => new[]
    {
        new ControlEvent
        {
            Seq = 1, Ts = 1.25, Kind = ControlEventKind.Status, Window = "win", Workspace = "work",
            Session = "sess",
            Payload = new ControlEventPayload
            {
                Name = "api", Status = "blocked", Pane = "right", Blink = true, Color = "#aabbcc",
                Shape = statusShape,
            },
        },
        new ControlEvent
        {
            Seq = 2, Ts = 2.5, Kind = ControlEventKind.Notify, Window = "win", Workspace = "work",
            Session = "sess",
            Payload = new ControlEventPayload { Name = "api", Title = "Done", Body = "tests passed" },
        },
        new ControlEvent
        {
            Seq = 3, Ts = 3.5, Kind = ControlEventKind.SessionCreated, Window = "win", Workspace = "work",
            Session = "created", Payload = new ControlEventPayload { Name = "new" },
        },
        new ControlEvent
        {
            Seq = 4, Ts = 4.5, Kind = ControlEventKind.SessionClosed, Window = "win", Workspace = "work",
            Session = "closed", Payload = new ControlEventPayload { Name = "old" },
        },
        new ControlEvent { Seq = 5, Ts = 5.5, Kind = ControlEventKind.TreeChanged, Window = "win" },
    };

    [Fact]
    public void EveryEventKindAndPayloadRoundTrips()
    {
        var events = SampleEvents();

        var json = ControlJson.Serialize(events);
        var decoded = ControlJson.Deserialize<ControlEvent[]>(json);

        Assert.Equal(CanonicalJson.Of(events), CanonicalJson.Of(decoded));
        foreach (var (_, wire) in AllKinds)
            Assert.Contains("\"kind\":\"" + wire + "\"", json);
    }

    [Fact]
    public void EveryEventKindMatchesGoldenWireShape()
    {
        var events = SampleEvents(statusShape: "triangle");
        var expected = new[]
        {
            "{\"kind\":\"status\",\"payload\":{\"blink\":true,\"color\":\"#aabbcc\",\"name\":\"api\",\"pane\":\"right\"," +
            "\"shape\":\"triangle\",\"status\":\"blocked\"},\"seq\":1,\"session\":\"sess\",\"ts\":1.25,\"window\":\"win\",\"workspace\":\"work\"}",
            "{\"kind\":\"notify\",\"payload\":{\"body\":\"tests passed\",\"name\":\"api\",\"title\":\"Done\"},\"seq\":2,\"session\":\"sess\",\"ts\":2.5,\"window\":\"win\",\"workspace\":\"work\"}",
            "{\"kind\":\"session.created\",\"payload\":{\"name\":\"new\"},\"seq\":3,\"session\":\"created\",\"ts\":3.5,\"window\":\"win\",\"workspace\":\"work\"}",
            "{\"kind\":\"session.closed\",\"payload\":{\"name\":\"old\"},\"seq\":4,\"session\":\"closed\",\"ts\":4.5,\"window\":\"win\",\"workspace\":\"work\"}",
            "{\"kind\":\"tree.changed\",\"payload\":{},\"seq\":5,\"ts\":5.5,\"window\":\"win\"}",
        };

        Assert.Equal(expected, events.Select(CanonicalJson.Of));
    }

    [Fact]
    public void StatusPayloadRoundTripsShapeAndOmitsItWhenNil()
    {
        var shaped = new ControlEvent
        {
            Seq = 1, Ts = 1.5, Kind = ControlEventKind.Status, Window = "win", Session = "sess",
            Payload = new ControlEventPayload { Status = "blocked", Shape = "triangle" },
        };
        var plain = new ControlEvent
        {
            Seq = 2, Ts = 2.5, Kind = ControlEventKind.Status, Window = "win", Session = "sess",
            Payload = new ControlEventPayload { Status = "blocked" },
        };

        var shapedJson = ControlJson.Serialize(shaped);
        Assert.Equal(CanonicalJson.Of(shaped), CanonicalJson.Of(ControlJson.Deserialize<ControlEvent>(shapedJson)));
        Assert.Equal("triangle", ControlJson.Deserialize<ControlEvent>(shapedJson)!.Payload.Shape);

        var plainJson = ControlJson.Serialize(plain);
        Assert.DoesNotContain("shape", plainJson);
    }

    [Fact]
    public void OptionalEventAndPayloadFieldsAreOmitted()
    {
        var evt = new ControlEvent { Seq = 1, Ts = 10, Kind = ControlEventKind.TreeChanged, Window = "win" };
        using var doc = JsonDocument.Parse(ControlJson.Serialize(evt));

        Assert.False(doc.RootElement.TryGetProperty("workspace", out _));
        Assert.False(doc.RootElement.TryGetProperty("session", out _));
        Assert.Empty(doc.RootElement.GetProperty("payload").EnumerateObject());
    }

    [Fact]
    public void BootstrapAndPopulatedBatchesRoundTripInResults()
    {
        var bootstrap = new ControlEventBatch { Run = Run, Next = 41, Items = Array.Empty<ControlEvent>() };
        var evt = new ControlEvent
        {
            Seq = 42, Ts = 42.25, Kind = ControlEventKind.Status, Window = "win", Workspace = "work",
            Session = "sess", Payload = new ControlEventPayload { Name = "api", Status = "active" },
        };
        var populated = new ControlEventBatch { Run = Run, Next = 42, Items = new[] { evt } };

        foreach (var batch in new[] { bootstrap, populated })
        {
            var response = new ControlResponse { Ok = true, Result = new ControlResult { Events = batch } };
            CanonicalJson.AssertRoundTrips(response);
        }
    }

    [Fact]
    public void ErrorResponseCanCarryCurrentEventAnchor()
    {
        var anchor = new ControlEventBatch { Run = Run, Next = 99, Items = Array.Empty<ControlEvent>() };
        var response = new ControlResponse
        {
            Ok = false,
            Result = new ControlResult { Events = anchor },
            Error = ControlEventReadError.CursorExpired.Message(),
        };

        var decoded = ControlJson.Deserialize<ControlResponse>(ControlJson.Serialize(response))!;

        Assert.Equal(CanonicalJson.Of(response), CanonicalJson.Of(decoded));
        Assert.Equal(CanonicalJson.Of(anchor), CanonicalJson.Of(decoded.Result!.Events));
        Assert.Equal(
            "{\"error\":\"event cursor expired\",\"ok\":false,\"result\":{\"events\":" +
            "{\"items\":[],\"next\":99,\"run\":\"CBB5E3D0-7A9B-4C96-9EA2-18B14380DDB1\"}}}",
            CanonicalJson.Of(response));
    }

    [Fact]
    public void EventReadRequestKeepsKindsAsRawStringsOnTheWire()
    {
        var request = new ControlRequest
        {
            Cmd = Command.EventsRead,
            Args = new ControlArgs
            {
                After = "7", Run = Run.ToString("D").ToUpperInvariant(),
                Kinds = new[] { "status", "future.kind" }, Limit = 250,
            },
        };

        var decoded = ControlJson.Deserialize<ControlRequest>(ControlJson.Serialize(request))!;

        Assert.Equal(CanonicalJson.Of(request), CanonicalJson.Of(decoded));
        Assert.Equal(new[] { "status", "future.kind" }, decoded.Args!.Kinds);
    }
}
