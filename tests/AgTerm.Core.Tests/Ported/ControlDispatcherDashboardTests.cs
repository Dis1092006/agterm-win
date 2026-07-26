using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Ported;

/// <summary>
/// Ported 1:1 from agtermCore ControlDispatcherDashboardTests.swift: dispatcher tests for the
/// dashboard --mru open path. They live here rather than in ControlDispatcherTests.cs only because
/// the upstream file was split at its 2000-line test-file cap; they share MockControlActions.
/// </summary>
public class ControlDispatcherDashboardTests
{
    private static ControlResponse Ok(ControlResult? result = null) => new() { Ok = true, Result = result };
    private static ControlResponse Err(string error) => new() { Ok = false, Error = error };

    private static void ExpectResponse(ControlResponse expected, ControlResponse? actual)
        => Assert.Equal(CanonicalJson.Of(expected), CanonicalJson.Of(actual!));

    [Fact]
    public async Task DashboardMruRoutesWithMruTrueAndNoTargets()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var plain = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard, Args = new ControlArgs { Mru = true },
        });
        var autoSized = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard, Args = new ControlArgs { Window = "win", AutoSize = true, Mru = true },
        });
        var fixedSize = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard, Args = new ControlArgs { FontSize = 12, Mru = true },
        });

        ExpectResponse(Ok(), plain);
        ExpectResponse(Ok(), autoSized);
        ExpectResponse(Ok(), fixedSize);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.Dashboard(Array.Empty<string>(), null, Close: false,
                new DashboardFontMode.Untouched(), Mru: true),
            new MockControlActions.Call.Dashboard(Array.Empty<string>(), "win", Close: false,
                new DashboardFontMode.Auto(), Mru: true),
            new MockControlActions.Call.Dashboard(Array.Empty<string>(), null, Close: false,
                new DashboardFontMode.Fixed(12), Mru: true),
        }, actions.Calls);
    }

    [Fact]
    public async Task DashboardMruRejectsExplicitIdsAndClose()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var withIds = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard, Args = new ControlArgs { Targets = new[] { "a" }, Mru = true },
        });
        var withClose = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.Dashboard, Args = new ControlArgs { Close = true, Mru = true },
        });

        ExpectResponse(Err("dashboard --mru cannot be combined with explicit session ids"), withIds);
        ExpectResponse(Err("dashboard --close takes no ids, --mru, or font options"), withClose);
        Assert.Empty(actions.Calls);
    }
}
