using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported 1:1 from agtermCore ControlEventDispatcherTests.swift (names preserved).</summary>
public class ControlEventDispatcherTests
{
    private static readonly Guid Run = Guid.Parse("CBB5E3D0-7A9B-4C96-9EA2-18B14380DDB1");

    private static ControlResponse Ok(ControlResult? result = null) => new() { Ok = true, Result = result };
    private static ControlResponse Err(string error) => new() { Ok = false, Error = error };

    private static void ExpectResponse(ControlResponse expected, ControlResponse? actual)
        => Assert.Equal(CanonicalJson.Of(expected), CanonicalJson.Of(actual!));

    [Fact]
    public async Task BootstrapRoutesWithDefaultsAndReturnsActionResponse()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        var batch = new ControlEventBatch { Run = Run, Next = 12, Items = Array.Empty<ControlEvent>() };
        actions.NextEventsReadResponse = Ok(new ControlResult { Events = batch });

        var response = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.EventsRead });

        ExpectResponse(actions.NextEventsReadResponse, response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.EventsRead(new ControlEventReadOptions(null, null, 100)),
        }, actions.Calls);
    }

    [Fact]
    public async Task ParsesCursorKindsAndMaximumLimit()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        _ = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.EventsRead,
            Args = new ControlArgs
            {
                After = "42", Run = Run.ToString().ToUpperInvariant(),
                Kinds = new[] { "status, notify", "tree.changed", "status" }, Limit = 1_000,
            },
        });

        var expected = new ControlEventReadOptions(
            new ControlEventCursor(Run, 42),
            new[] { ControlEventKind.Status, ControlEventKind.Notify, ControlEventKind.TreeChanged },
            1_000);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.EventsRead(expected),
        }, actions.Calls);
    }

    [Fact]
    public async Task CursorFieldsMustBePaired()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var runOnly = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.EventsRead, Args = new ControlArgs { Run = Run.ToString().ToUpperInvariant() },
        });
        var afterOnly = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.EventsRead, Args = new ControlArgs { After = "1" },
        });

        ExpectResponse(Err("events.read requires --run and --after together"), runOnly);
        ExpectResponse(Err("events.read requires --run and --after together"), afterOnly);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task RejectsInvalidRunAndDecimalCursor()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var badRun = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.EventsRead, Args = new ControlArgs { After = "1", Run = "not-a-uuid" },
        });
        var badCursor = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.EventsRead,
            Args = new ControlArgs { After = "-1", Run = Run.ToString().ToUpperInvariant() },
        });

        ExpectResponse(Err("invalid event run id"), badRun);
        ExpectResponse(Err("invalid event cursor"), badCursor);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task RejectsUnknownKindAfterRawRequestDecoding()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        var json = """{"cmd":"events.read","args":{"kinds":["status","future.kind"]}}""";
        var request = ControlJson.Deserialize<ControlRequest>(json);

        var response = await dispatcher.Dispatch(request!);

        ExpectResponse(Err("invalid event kind: future.kind"), response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task RejectsEmptyKindComponents()
    {
        foreach (var kinds in new[] { new[] { "" }, new[] { "," }, new[] { "status," } })
        {
            var actions = new MockControlActions();
            var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest
            {
                Cmd = Command.EventsRead, Args = new ControlArgs { Kinds = kinds },
            });

            ExpectResponse(Err("invalid event kind: "), response);
            Assert.Empty(actions.Calls);
        }
    }

    [Fact]
    public async Task RejectsLimitsOutsideInclusiveBounds()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var zero = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.EventsRead, Args = new ControlArgs { Limit = 0 },
        });
        var tooLarge = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.EventsRead, Args = new ControlArgs { Limit = 1_001 },
        });

        ExpectResponse(Err("event limit must be between 1 and 1000"), zero);
        ExpectResponse(Err("event limit must be between 1 and 1000"), tooLarge);
        Assert.Empty(actions.Calls);
    }
}
