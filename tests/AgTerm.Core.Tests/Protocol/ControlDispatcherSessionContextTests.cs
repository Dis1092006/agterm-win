using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Protocol;

// session.context (upstream 73b63b71): the dispatcher trims and validates, so a rejected request never
// reaches the host, and clear reaches it as null
public class ControlDispatcherSessionContextTests
{
    private readonly MockControlActions _actions = new();
    private readonly ControlDispatcher _dispatcher;

    public ControlDispatcherSessionContextTests() => _dispatcher = new ControlDispatcher(_actions);

    private Task<ControlResponse?> Send(string? mode, string? text, string? target = "session", string? window = null) =>
        _dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionContext, Target = target,
            Args = new ControlArgs { Mode = mode, Text = text, Window = window },
        });

    [Fact]
    public async Task SetPassesTheTrimmedTextToTheHost()
    {
        var response = await Send("set", "  PR #517: reap ordering  ", window: "win");

        Assert.True(response!.Ok);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionContext("session", "win", "PR #517: reap ordering"),
        }, _actions.Calls);
    }

    [Fact]
    public async Task ClearPassesNullToTheHost()
    {
        var response = await Send("clear", null);

        Assert.True(response!.Ok);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionContext("session", null, null),
        }, _actions.Calls);
    }

    [Theory]
    [InlineData("set", null, "session.context set requires text")]
    [InlineData("clear", "x", "session.context clear takes no text")]
    [InlineData(null, "x", "invalid context mode:  (set|clear)")]
    [InlineData("toggle", "x", "invalid context mode: toggle (set|clear)")]
    [InlineData("set", "   ", "context must not be empty (use --clear to remove it)")]
    [InlineData("set", "PR\n517", "context must not contain control characters or line breaks")]
    [InlineData("set", "PR #517\n", "context must not contain control characters or line breaks")]
    public async Task ARejectedRequestNeverReachesTheHost(string? mode, string? text, string error)
    {
        var response = await Send(mode, text);

        Assert.False(response!.Ok);
        Assert.Equal(error, response!.Error);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public async Task AnOversizedContextIsRejected()
    {
        var response = await Send("set", new string('a', 257));

        Assert.False(response!.Ok);
        Assert.Equal("context must be at most 256 UTF-8 bytes", response!.Error);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void TheWireRequestMapsToTheCommandModeAndText()
    {
        var request = ControlJson.Deserialize<ControlRequest>(
            "{\"cmd\":\"session.context\",\"target\":\"s\",\"args\":{\"mode\":\"set\",\"text\":\"PR #517\"}}")!;

        Assert.Equal(Command.SessionContext, request.Cmd);
        Assert.Equal("set", request.Args!.Mode);
        Assert.Equal("PR #517", request.Args.Text);
    }
}
