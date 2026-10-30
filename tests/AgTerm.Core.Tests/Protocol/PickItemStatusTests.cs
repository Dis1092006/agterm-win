using System.Text.Json;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Protocol;

public sealed class PickItemStatusTests
{
    [Fact]
    public void Nested_status_fields_are_recorded_as_post_pin_adoptions()
    {
        Assert.Equal(new[] { "status", "statusColor", "statusShape" }, ParitySurface.Adopted("pickItem"));
    }

    [Fact]
    public void Status_fields_round_trip_without_changing_plain_items()
    {
        const string json = """{"id":"a","label":"A","status":"blocked","statusColor":"#ff8800","statusShape":"star"}""";
        Assert.Equal(json, ControlJson.Serialize(ControlJson.Deserialize<ControlPickItem>(json)!));
        Assert.Equal("""{"id":"a","label":"A"}""",
            ControlJson.Serialize(ControlJson.Deserialize<ControlPickItem>("""{"id":"a","label":"A","status":null,"statusColor":null,"statusShape":null}""")!));
    }

    [Theory]
    [InlineData("status", "busy", "invalid pick item status (idle|active|completed|blocked)")]
    [InlineData("status", "", "invalid pick item status (idle|active|completed|blocked)")]
    [InlineData("statusColor", "red", "invalid pick item color (expected #rrggbb)")]
    [InlineData("statusColor", "#123", "invalid pick item color (expected #rrggbb)")]
    [InlineData("statusShape", "hexagon", "invalid pick item shape (circle|square|triangle|diamond|capsule|star)")]
    public async Task Invalid_status_fields_are_rejected_before_selection_without_calling_host(
        string field, string value, string error)
    {
        var item = ControlJson.Deserialize<ControlPickItem>(
            $$"""{"id":"a","label":"A","{{field}}":{{JsonSerializer.Serialize(value)}}}""")!;
        var actions = new MockControlActions();
        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest
        {
            Cmd = Command.PickOpen,
            Args = new ControlArgs { Items = [item], Selection = "missing" },
        });
        Assert.Equal(error, response!.Error);
        Assert.False(response.Ok);
        Assert.Empty(actions.Calls);
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("active")]
    [InlineData("completed")]
    [InlineData("blocked")]
    public async Task Valid_status_fields_reach_the_host_unchanged(string status)
    {
        var json = $$"""{"id":"a","label":"A","status":"{{status}}","statusColor":"#AAbB00","statusShape":"diamond"}""";
        var actions = new MockControlActions();
        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest
        {
            Cmd = Command.PickOpen,
            Args = new ControlArgs { Items = [ControlJson.Deserialize<ControlPickItem>(json)!] },
        });
        Assert.True(response!.Ok);
        var call = Assert.IsType<MockControlActions.Call.PickOpen>(Assert.Single(actions.Calls));
        Assert.Equal(json, ControlJson.Serialize(Assert.Single(call.Pick.Items)));
    }
}
