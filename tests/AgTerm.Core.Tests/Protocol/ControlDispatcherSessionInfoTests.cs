using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Protocol;

// session.info (upstream 80f822bf): the dispatcher builds and validates the whole line, so a refused
// set never reaches the host and leaves the line and its timer standing; clear reaches it as null
public class ControlDispatcherSessionInfoTests
{
    private readonly MockControlActions _actions = new();
    private readonly ControlDispatcher _dispatcher;

    public ControlDispatcherSessionInfoTests() => _dispatcher = new ControlDispatcher(_actions);

    private Task<ControlResponse?> Send(ControlArgs args, string? target = null) =>
        _dispatcher.Dispatch(new ControlRequest { Cmd = Command.SessionInfo, Target = target, Args = args });

    private static SessionInfo Info(string source, bool markdown, double hideAfter) =>
        SessionInfo.Make(source, markdown, hideAfter, out _)!;

    [Fact]
    public async Task SetAndClearReachTheHost()
    {
        var set = await Send(new ControlArgs
        {
            Text = "^[run](color: \"green\")", Mode = "set", HideAfter = 30, Markdown = true, Window = "win",
        }, target: "session");
        var plain = await Send(new ControlArgs { Text = "**x**", Mode = "set" });
        var cleared = await Send(new ControlArgs { Mode = "clear" });

        Assert.True(set!.Ok);
        Assert.True(plain!.Ok);
        Assert.True(cleared!.Ok);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionInfo("session", "win", Info("^[run](color: \"green\")", true, 30)),
            new MockControlActions.Call.SessionInfo(null, null, Info("**x**", false, 0)),
            new MockControlActions.Call.SessionInfo(null, null, null),
        }, _actions.Calls);
    }

    [Fact]
    public void TheRequestEncodesItsWireShape()
    {
        var request = new ControlRequest
        {
            Cmd = Command.SessionInfo, Target = "s1",
            Args = new ControlArgs { Text = "**3**", Mode = "set", HideAfter = 5, Markdown = true },
        };

        var json = ControlJson.Serialize(request);

        Assert.Contains("\"cmd\":\"session.info\"", json);
        Assert.Contains("\"mode\":\"set\"", json);
        Assert.Contains("\"markdown\":true", json);
        Assert.Contains("\"hideAfter\":5", json);
        var decoded = ControlJson.Deserialize<ControlRequest>(json)!;
        Assert.Equal(Command.SessionInfo, decoded.Cmd);
        Assert.Equal("**3**", decoded.Args!.Text);
        Assert.Equal(5, decoded.Args.HideAfter);
        Assert.True(decoded.Args.Markdown);
    }

    public static TheoryData<string?, string?, bool?, double?, string> Rejections => new()
    {
        { "set", null, null, null, "session.info set requires text" },
        { null, "x", null, null, "invalid info mode:  (set|clear)" },
        { "toggle", "x", null, null, "invalid info mode: toggle (set|clear)" },
        { "clear", "x", null, null, "session.info clear takes no text, markdown or hide-after" },
        { "clear", null, true, null, "session.info clear takes no text, markdown or hide-after" },
        { "clear", null, null, 5, "session.info clear takes no text, markdown or hide-after" },
        { "set", "x", null, -1, "session.info: --hide-after must be 0...86400 seconds" },
        { "set", "x", null, 86_401, "session.info: --hide-after must be 0...86400 seconds" },
        { "set", "   ", null, null, "session.info: info text is empty" },
        { "set", "a\nb", null, null, "session.info: info text must be one line" },
        { "set", "^[a](color: \"pink\")", true, null,
            $"session.info: unknown info color: pink ({TintColors.ValidNamesList})" },
        { "set", "^[a](color: red)", true, null,
            "session.info: invalid info markdown; quote attribute values: ^[text](color: \"green\")" },
        { "set", new string('a', 513), null, null, "session.info: info text is over 512 bytes" },
    };

    [Theory]
    [MemberData(nameof(Rejections))]
    public async Task ARejectedRequestNeverReachesTheHost(
        string? mode, string? text, bool? markdown, double? hideAfter, string error)
    {
        var response = await Send(new ControlArgs { Mode = mode, Text = text, Markdown = markdown, HideAfter = hideAfter });

        Assert.False(response!.Ok);
        Assert.Equal(error, response.Error);
        Assert.Empty(_actions.Calls);
    }
}
