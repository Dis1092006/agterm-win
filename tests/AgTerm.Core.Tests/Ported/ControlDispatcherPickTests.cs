using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Ported;

// Ported from agtermCore ControlDispatcherPickTests.swift (upstream ed831ab).
public class ControlDispatcherPickTests
{
    private static ControlResponse Err(string error) => new() { Ok = false, Error = error };

    private static ControlPickItem Item(string id, string label, string? subtitle = null) =>
        new() { Id = id, Label = label, Subtitle = subtitle };

    private static async Task<(ControlResponse? Response, MockControlActions Actions)> Open(ControlArgs args)
    {
        var actions = new MockControlActions();
        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest { Cmd = Command.PickOpen, Args = args });
        return (response, actions);
    }

    private static void AssertRejected(string error, (ControlResponse? Response, MockControlActions Actions) run)
    {
        Assert.Equal(CanonicalJson.Of(Err(error)), CanonicalJson.Of(run.Response!));
        Assert.Empty(run.Actions.Calls);
    }

    [Fact]
    public async Task OpenRejectsMissingItemsWithoutCallingHost() =>
        AssertRejected("pick.open requires items", await Open(new ControlArgs()));

    [Fact]
    public async Task OpenRejectsEmptyItemsWithoutAllowCustom() =>
        AssertRejected("pick.open requires at least one item", await Open(new ControlArgs { Items = Array.Empty<ControlPickItem>() }));

    [Fact]
    public async Task OpenAcceptsEmptyItemsWithAllowCustom()
    {
        var (response, actions) = await Open(new ControlArgs { Items = Array.Empty<ControlPickItem>(), AllowCustom = true });
        Assert.True(response!.Ok);
        var call = Assert.IsType<MockControlActions.Call.PickOpen>(Assert.Single(actions.Calls));
        Assert.Empty(call.Pick.Items);
        Assert.True(call.Pick.AllowCustom);
    }

    [Fact]
    public async Task OpenRejectsItemsOverLimit() =>
        AssertRejected("too many items (max 1000)", await Open(new ControlArgs
        {
            Items = Enumerable.Range(0, 1001).Select(i => Item($"i{i}", $"L{i}")).ToArray(),
        }));

    [Fact]
    public async Task OpenRejectsEmptyLabelButAcceptsWhitespaceLabel()
    {
        AssertRejected("pick item label must not be empty", await Open(new ControlArgs { Items = new[] { Item("a", "") } }));
        Assert.True((await Open(new ControlArgs { Items = new[] { Item("a", "  ") } })).Response!.Ok);
    }

    [Fact]
    public async Task OpenRejectsDuplicateIds() =>
        AssertRejected("pick item ids must be unique", await Open(new ControlArgs { Items = new[] { Item("a", "A"), Item("a", "B") } }));

    [Fact]
    public async Task OpenTreatsCanonicallyEquivalentIdsAsDuplicates() =>
        AssertRejected("pick item ids must be unique", await Open(new ControlArgs
        {
            Items = new[] { Item("café", "A"), Item("café", "B") },
        }));

    [Fact]
    public async Task OpenRejectsControlCharactersInLabelAndSubtitle()
    {
        AssertRejected("item text must not contain control characters", await Open(new ControlArgs { Items = new[] { Item("a", "x\ny") } }));
        AssertRejected("item text must not contain control characters", await Open(new ControlArgs { Items = new[] { Item("a", "x", "s\u007Ft") } }));
        Assert.True((await Open(new ControlArgs { Items = new[] { Item("a\nb", "x") } })).Response!.Ok);
    }

    [Fact]
    public async Task OpenStillValidatesNonEmptyItemsWhenAllowCustomIsSet() =>
        AssertRejected("pick item label must not be empty", await Open(new ControlArgs { Items = new[] { Item("a", "") }, AllowCustom = true }));

    [Fact]
    public async Task OpenRejectsASelectionNamingNoItem()
    {
        AssertRejected("pick select must name an item id", await Open(new ControlArgs { Items = new[] { Item("a", "A") }, Selection = "b" }));
        AssertRejected("pick select must name an item id", await Open(new ControlArgs { Items = Array.Empty<ControlPickItem>(), AllowCustom = true, Selection = "b" }));
    }

    [Fact]
    public async Task OpenResolvesASelectionToTheMatchingItemsOwnId()
    {
        var (_, actions) = await Open(new ControlArgs { Items = new[] { Item("café", "A") }, Selection = "café" });
        var call = Assert.IsType<MockControlActions.Call.PickOpen>(Assert.Single(actions.Calls));
        Assert.Equal("café", call.Pick.Selection);
    }

    [Fact]
    public async Task OpenRoutesValidatedPickAndReturnsHostResponse()
    {
        var actions = new MockControlActions { NextPickOpenResponse = new ControlResponse { Ok = true, Result = new ControlResult { Id = "X" } } };
        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest
        {
            Cmd = Command.PickOpen,
            Args = new ControlArgs { Items = new[] { Item("a", "A", "sub") }, Prompt = "Choose", Query = "q", Window = "w1", Follow = true },
        });

        Assert.Equal("X", response!.Result!.Id);
        var call = Assert.IsType<MockControlActions.Call.PickOpen>(Assert.Single(actions.Calls));
        Assert.Equal("w1", call.Window);
        Assert.True(call.Follow);
        Assert.Equal("Choose", call.Pick.Prompt);
        Assert.Equal("q", call.Pick.Query);
        Assert.False(call.Pick.AllowCustom);
        Assert.Null(call.Pick.Selection);
        Assert.Equal(Item("a", "A", "sub"), Assert.Single(call.Pick.Items));
        Assert.True(Guid.TryParseExact(call.Pick.Id, "D", out _));
        Assert.Equal(call.Pick.Id.ToUpperInvariant(), call.Pick.Id);
    }

    [Fact]
    public async Task OpenLeavesQueryAndPromptNullWhenOmitted()
    {
        var (_, actions) = await Open(new ControlArgs { Items = new[] { Item("a", "A") } });
        var call = Assert.IsType<MockControlActions.Call.PickOpen>(Assert.Single(actions.Calls));
        Assert.Null(call.Pick.Query);
        Assert.Null(call.Pick.Prompt);
        Assert.False(call.Follow);
    }

    [Fact]
    public async Task ResultAndCancelRejectMissingTargetWithoutCallingHost()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        Assert.Equal("pick.result requires a pick id", (await dispatcher.Dispatch(new ControlRequest { Cmd = Command.PickResult }))!.Error);
        Assert.Equal("pick.cancel requires a pick id", (await dispatcher.Dispatch(new ControlRequest { Cmd = Command.PickCancel }))!.Error);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task ResultAndCancelRouteTargetAndWindow()
    {
        var actions = new MockControlActions
        {
            NextPickResultResponse = new ControlResponse { Ok = true, Result = new ControlResult { Pick = ControlPickResult.Pending() } },
        };
        var dispatcher = new ControlDispatcher(actions);

        var result = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.PickResult, Target = "P", Args = new ControlArgs { Window = "w" } });
        var cancel = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.PickCancel, Target = "P" });

        Assert.Equal(ControlPickResult.Pending(), result!.Result!.Pick);
        Assert.True(cancel!.Ok);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.PickResult("P", "w"),
            new MockControlActions.Call.PickCancel("P", null),
        }, actions.Calls);
    }
}
