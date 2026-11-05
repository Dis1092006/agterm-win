using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported from agtermCore ControlDispatcherRecentTests.swift (upstream 6b564749, #744).</summary>
public class ControlDispatcherRecentTests
{
    [Fact]
    public async Task ListAndClearRouteToActions()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        var item = new ControlRecentItem
        {
            Id = "R1", Kind = "session", Title = "api", Subtitle = "project", ClosedAt = 1,
            WorkspaceId = "W1", SessionIds = new[] { "S1" },
        };
        var canned = new ControlResponse { Ok = true, Result = new ControlResult { Recent = new[] { item } } };
        actions.NextRecentResponse = canned;

        var list = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.RecentList });
        var clear = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.RecentClear });

        Assert.Same(canned, list);
        Assert.Same(canned, clear);
        Assert.Equal(
            new MockControlActions.Call[] { new MockControlActions.Call.RecentList(), new MockControlActions.Call.RecentClear() },
            actions.Calls);
    }

    [Fact]
    public async Task OpenPassesTheEntryAndWindowThrough()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        await dispatcher.Dispatch(new ControlRequest { Cmd = Command.RecentOpen });
        await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.RecentOpen, Target = "ab12", Args = new ControlArgs { Window = "w1" },
        });

        Assert.Equal(
            new MockControlActions.Call[]
            {
                new MockControlActions.Call.RecentOpen(null, null),
                new MockControlActions.Call.RecentOpen("ab12", "w1"),
            },
            actions.Calls);
    }

    [Theory]
    [InlineData(Command.RecentList, "recent.list")]
    [InlineData(Command.RecentClear, "recent.clear")]
    public async Task ListAndClearRefuseATargetOrWindowBeforeAnyAction(Command cmd, string wire)
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var targeted = await dispatcher.Dispatch(new ControlRequest { Cmd = cmd, Target = "active" });
        var windowed = await dispatcher.Dispatch(
            new ControlRequest { Cmd = cmd, Args = new ControlArgs { Window = "w1" } });

        var error = $"{wire} takes no target or --window";
        Assert.False(targeted!.Ok);
        Assert.Equal(error, targeted.Error);
        Assert.False(windowed!.Ok);
        Assert.Equal(error, windowed.Error);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public void TheSharedRefusalMatchesTheDispatcher()
    {
        Assert.Null(ControlDispatcher.RecentRefusal(new ControlRequest { Cmd = Command.RecentList }));
        Assert.Equal("recent.clear takes no target or --window",
            ControlDispatcher.RecentRefusal(new ControlRequest { Cmd = Command.RecentClear, Target = "x" })!.Error);
    }
}
