using System.Text;
using AgTerm.Cli;
using AgTerm.Cli.Tests.TestSupport;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests.Ported;

// Port of agtermctlKitTests/EventCommandsTests.swift (1:1, names preserved).
//
// Seam mapping (see the header comment in src/AgTerm.Cli/EventCommands.cs):
//   Events.parse(argv)          → Agtermctl.ParseAsRoot(["events", ...argv]) cast to EventCommands.Events
//   makeInitialRequest()        → MakeRequest()
//   EventStreamDependencies     → CliEnvironment (Send / Sleep / Out)
//   EventStreamError            → CliValidationException
//   writeLine                   → env.Out.WriteLine (captured via a line-collecting TextWriter)
// Protocol value equality (`request == ControlRequest(...)`) goes through CanonicalJson because
// ControlArgs carries arrays, which C# record equality compares by reference.
public class EventCommandsTests
{
    private static readonly Guid Run = Guid.Parse("CBB5E3D0-7A9B-4C96-9EA2-18B14380DDB1");

    /// <summary>Swift run.uuidString — the uppercase canonical form.</summary>
    private const string RunUuidString = "CBB5E3D0-7A9B-4C96-9EA2-18B14380DDB1";

    private static EventCommands.Events Parse(params string[] argv) =>
        (EventCommands.Events)Agtermctl.ParseAsRoot(new[] { "events" }.Concat(argv).ToArray());

    /// <summary>A TextWriter that records each written line — the port of the writeLine dependency.</summary>
    private sealed class LineWriter : TextWriter
    {
        public List<string> Lines { get; } = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value) => Lines.Add(value ?? "");
    }

    /// <summary>A TextWriter whose WriteLine always fails — the port of the throwing writeLine.</summary>
    private sealed class ThrowingLineWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value) => throw new CliValidationException("output failed");
    }

    [Fact]
    public void ParserBuildsBootstrapAndCursoredRequests()
    {
        var plain = Parse();
        CanonicalJson.AssertEqual(new ControlRequest { Cmd = WireCommand.EventsRead }, plain.MakeRequest());

        var resumed = Parse(
            "--json", "--kind", "status,notify", "--kind", "tree.changed",
            "--run", RunUuidString, "--after", "42", "--limit", "1000");
        Assert.True(resumed.Basic.Json);
        CanonicalJson.AssertEqual(new ControlRequest
        {
            Cmd = WireCommand.EventsRead,
            Args = new ControlArgs
            {
                After = "42", Run = RunUuidString,
                Kinds = new[] { "status", "notify", "tree.changed" }, Limit = 1_000,
            },
        }, resumed.MakeRequest());
    }

    [Fact]
    public void ParserRejectsInvalidPairsKindsAndLimits()
    {
        foreach (var argv in new[]
                 {
                     new[] { "--run", RunUuidString }, new[] { "--after", "1" },
                     new[] { "--run", "bad", "--after", "1" },
                     new[] { "--run", RunUuidString, "--after", "-1" }, new[] { "--kind", "future" },
                     new[] { "--kind", "" }, new[] { "--kind", "," }, new[] { "--kind", "status," },
                     new[] { "--limit", "0" }, new[] { "--limit", "1001" },
                 })
        {
            Assert.ThrowsAny<Exception>(() => Parse(argv));
        }
    }

    [Fact]
    public void StreamStateBootstrapsAndAdvancesCursor()
    {
        var state = new EventStreamState(cursor: null,
            kinds: new HashSet<ControlEventKind> { ControlEventKind.Status }, limit: 2);
        CanonicalJson.AssertEqual(new ControlRequest
        {
            Cmd = WireCommand.EventsRead,
            Args = new ControlArgs { Kinds = new[] { "status" }, Limit = 2 },
        }, state.MakeRequest());

        var empty = new ControlEventBatch { Run = Run, Next = 5, Items = Array.Empty<ControlEvent>() };
        var emptyEvents = state.Consume(new ControlResponse
        {
            Ok = true, Result = new ControlResult { Events = empty },
        });
        Assert.Empty(emptyEvents);
        Assert.Equal("5", state.MakeRequest().Args?.After);
        Assert.Equal(RunUuidString, state.MakeRequest().Args?.Run);

        var @event = new ControlEvent
        {
            Seq = 6, Ts = 10, Kind = ControlEventKind.Status,
            Payload = new ControlEventPayload { Name = "api", Status = "active" },
        };
        var page = new ControlEventBatch { Run = Run, Next = 6, Items = new[] { @event } };
        var pageEvents = state.Consume(new ControlResponse
        {
            Ok = true, Result = new ControlResult { Events = page },
        });
        Assert.Equal(new[] { @event }, pageEvents);
        Assert.Equal("6", state.MakeRequest().Args?.After);
    }

    [Fact]
    public void RunPollsEmptyPagesWritesNDJSONImmediatelyAndResumesFromCursor()
    {
        var @event = new ControlEvent
        {
            Seq = 6, Ts = 10, Kind = ControlEventKind.Status,
            Payload = new ControlEventPayload { Name = "api", Status = "active" },
        };
        var responses = new Queue<ControlResponse>(new[]
        {
            new ControlResponse
            {
                Ok = true, Result = new ControlResult
                {
                    Events = new ControlEventBatch { Run = Run, Next = 5, Items = Array.Empty<ControlEvent>() },
                },
            },
            new ControlResponse
            {
                Ok = true, Result = new ControlResult
                {
                    Events = new ControlEventBatch { Run = Run, Next = 6, Items = new[] { @event } },
                },
            },
        });
        var requests = new List<ControlRequest>();
        var sleeps = new List<TimeSpan>();
        var lines = new LineWriter();
        var env = new CliEnvironment
        {
            GetEnv = _ => null,
            Send = (_, request) =>
            {
                requests.Add(request);
                return responses.Dequeue();
            },
            Sleep = delay => sleeps.Add(delay),
            Out = lines,
        };
        var command = Parse("--json");
        var state = command.MakeState();

        command.Poll(state, env);
        command.Poll(state, env);

        Assert.Equal(2, requests.Count);
        CanonicalJson.AssertEqual(new ControlRequest { Cmd = WireCommand.EventsRead }, requests[0]);
        Assert.Equal(RunUuidString, requests[1].Args?.Run);
        Assert.Equal("5", requests[1].Args?.After);
        Assert.Equal(new[] { TimeSpan.FromSeconds(0.25) }, sleeps);
        Assert.Single(lines.Lines);
        Assert.DoesNotContain("\n", lines.Lines[0]);
        Assert.Equal(@event, ControlJson.Deserialize<ControlEvent>(lines.Lines[0]));
    }

    [Fact]
    public void RunStopsImmediatelyOnTransportServerAndOutputFailures()
    {
        var command = Parse();
        var transportState = command.MakeState();
        var transportSends = 0;
        Assert.Throws<CliValidationException>(() =>
            command.Poll(transportState, new CliEnvironment
            {
                GetEnv = _ => null,
                Send = (_, _) =>
                {
                    transportSends += 1;
                    throw new CliValidationException("transport failed");
                },
                Sleep = _ => { },
                Out = new LineWriter(),
            }));
        Assert.Equal(1, transportSends);

        var serverState = command.MakeState();
        var serverSends = 0;
        Assert.Throws<CliValidationException>(() =>
            command.Poll(serverState, new CliEnvironment
            {
                GetEnv = _ => null,
                Send = (_, _) =>
                {
                    serverSends += 1;
                    return new ControlResponse
                    {
                        Ok = false, Error = ControlEventReadError.CursorExpired.Message(),
                    };
                },
                Sleep = _ => { },
                Out = new LineWriter(),
            }));
        Assert.Equal(1, serverSends);

        var outputState = command.MakeState();
        var outputSends = 0;
        Assert.Throws<CliValidationException>(() =>
            command.Poll(outputState, new CliEnvironment
            {
                GetEnv = _ => null,
                Send = (_, _) =>
                {
                    outputSends += 1;
                    var @event = new ControlEvent { Seq = 1, Ts = 0, Kind = ControlEventKind.TreeChanged };
                    return new ControlResponse
                    {
                        Ok = true, Result = new ControlResult
                        {
                            Events = new ControlEventBatch { Run = Run, Next = 1, Items = new[] { @event } },
                        },
                    };
                },
                Sleep = _ => { },
                Out = new ThrowingLineWriter(),
            }));
        Assert.Equal(1, outputSends);
    }

    [Fact]
    public void StreamStatePreservesSuppliedCursorAndRejectsServerFailures()
    {
        var state = new EventStreamState(new ControlEventCursor(Run, 9), kinds: null, limit: null);
        Assert.Equal("9", state.MakeRequest().Args?.After);
        Assert.Throws<CliValidationException>(() =>
            state.Consume(new ControlResponse
            {
                Ok = false, Error = ControlEventReadError.CursorExpired.Message(),
            }));
        Assert.Throws<CliValidationException>(() =>
            state.Consume(new ControlResponse { Ok = true }));
    }

    [Fact]
    public void FormattersCoverEveryKindAndNDJSONIsOneBareEvent()
    {
        var events = new[]
        {
            new ControlEvent
            {
                Seq = 1, Ts = 0, Kind = ControlEventKind.Status,
                Payload = new ControlEventPayload { Name = "api", Status = "blocked", Pane = "right", Blink = true },
            },
            new ControlEvent
            {
                Seq = 2, Ts = 0, Kind = ControlEventKind.Notify,
                Payload = new ControlEventPayload { Name = "api", Title = "Done", Body = "ok" },
            },
            new ControlEvent
            {
                Seq = 3, Ts = 0, Kind = ControlEventKind.SessionCreated,
                Payload = new ControlEventPayload { Name = "new" },
            },
            new ControlEvent
            {
                Seq = 4, Ts = 0, Kind = ControlEventKind.SessionClosed,
                Payload = new ControlEventPayload { Name = "old" },
            },
            new ControlEvent { Seq = 5, Ts = 0, Kind = ControlEventKind.TreeChanged, Window = "win" },
            new ControlEvent
            {
                Seq = 6, Ts = 0, Kind = ControlEventKind.Status,
                Payload = new ControlEventPayload
                {
                    Name = "api", Status = "active", Color = "#ff8800", Shape = "triangle",
                },
            },
            // shape WITHOUT a color: `shape=` must be its own arm, not nested inside the color one
            new ControlEvent
            {
                Seq = 7, Ts = 0, Kind = ControlEventKind.Status,
                Payload = new ControlEventPayload { Name = "api", Status = "completed", Shape = "star" },
            },
        };
        // Swift TimeZone(secondsFromGMT: 0) → TimeZoneInfo.Utc.
        var human = events.Select(e => EventFormatter.Human(e, TimeZoneInfo.Utc)).ToArray();
        Assert.Equal("00:00:00 status api blocked pane=right blink", human[0]);
        Assert.Equal("00:00:00 notify api Done: ok", human[1]);
        Assert.Equal("00:00:00 session.created new", human[2]);
        Assert.Equal("00:00:00 session.closed old", human[3]);
        Assert.Equal("00:00:00 tree.changed win", human[4]);
        Assert.Equal("00:00:00 status api active color=#ff8800 shape=triangle", human[5]);
        Assert.Equal("00:00:00 status api completed shape=star", human[6]);

        foreach (var @event in events)
        {
            var line = EventFormatter.Json(@event);
            Assert.DoesNotContain("\n", line);
            Assert.Equal(@event, ControlJson.Deserialize<ControlEvent>(line));
        }
    }
}
