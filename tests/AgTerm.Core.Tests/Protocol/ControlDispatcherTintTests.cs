using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Protocol;

// session.tint, workspace.tint and the creation tint of session.new and workspace.new
// (upstream f3fea57b, ControlDispatcherSessionMetadataTests)
public class ControlDispatcherTintTests
{
    private readonly MockControlActions _actions = new();
    private readonly ControlDispatcher _dispatcher;

    public ControlDispatcherTintTests() => _dispatcher = new ControlDispatcher(_actions);

    private Task<ControlResponse?> Send(Command cmd, ControlArgs? args, string? target = null) =>
        _dispatcher.Dispatch(new ControlRequest { Cmd = cmd, Target = target, Args = args });

    private static string[]? Split(string? joined) => joined?.Split('|');

    [Fact]
    public async Task SessionTintSetRoutesColorAndParsedParts()
    {
        var bare = await Send(Command.SessionTint, new ControlArgs { Mode = "set", Window = "w", Tint = "Red" }, "s");
        var placed = await Send(Command.SessionTint, new ControlArgs { Mode = "set", Tint = "blue", TintOn = ["name", "icon"] });
        var cleared = await Send(Command.SessionTint, new ControlArgs { Mode = "clear" });

        Assert.True(bare!.Ok && placed!.Ok && cleared!.Ok);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionTint("s", "w", ControlTintUpdate.Set(TintColor.Red, null)),
            new MockControlActions.Call.SessionTint(null, null, ControlTintUpdate.Set(TintColor.Blue, TintParts.Icon | TintParts.Name)),
            new MockControlActions.Call.SessionTint(null, null, ControlTintUpdate.Clear),
        }, _actions.Calls);
    }

    [Fact]
    public async Task WorkspaceTintRoutesToTheWorkspaceAction()
    {
        var response = await Send(Command.WorkspaceTint, new ControlArgs { Mode = "set", Tint = "gray", TintOn = ["name"] }, "ws");

        Assert.Equal(new ControlResponse { Ok = true }, response);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.WorkspaceTint("ws", null, ControlTintUpdate.Set(TintColor.Gray, TintParts.Name)),
        }, _actions.Calls);
    }

    [Theory]
    [InlineData("set", null, null, "session.tint set requires a color")]
    [InlineData("set", "teal", null, "invalid tint: teal (red|orange|yellow|green|blue|purple|gray)")]
    [InlineData("set", "Teal", null, "invalid tint: Teal (red|orange|yellow|green|blue|purple|gray)")]
    [InlineData("set", "red", "strip", "invalid tint placement: strip (icon,name,flag)")]
    [InlineData("set", "red", "", "invalid tint placement:  (icon,name,flag)")]
    [InlineData("clear", "red", null, "session.tint clear takes no color or placement")]
    [InlineData("clear", null, "icon", "session.tint clear takes no color or placement")]
    [InlineData(null, "red", null, "invalid tint mode:  (set|clear)")]
    [InlineData("toggle", "red", null, "invalid tint mode: toggle (set|clear)")]
    public async Task SessionTintRefusesContradictoryOrUnknownInput(string? mode, string? tint, string? tintOn, string error)
    {
        // "" stands for an empty tintOn list
        var parts = tintOn == "" ? [] : Split(tintOn);
        var response = await Send(Command.SessionTint, new ControlArgs { Mode = mode, Tint = tint, TintOn = parts });

        Assert.Equal(new ControlResponse { Ok = false, Error = error }, response);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public async Task WorkspaceTintNamesItsOwnCommandInTheRefusal()
    {
        var response = await Send(Command.WorkspaceTint, new ControlArgs { Mode = "set" });

        Assert.Equal("workspace.tint set requires a color", response!.Error);
        Assert.Empty(_actions.Calls);
    }

    // Windows-only: a session tint can show on the flag; a workspace row has no flag.
    [Fact]
    public async Task OnlyASessionTintTakesTheFlag()
    {
        var session = await Send(Command.SessionTint, new ControlArgs { Mode = "set", Tint = "red", TintOn = ["flag"] });
        var workspace = await Send(Command.WorkspaceTint, new ControlArgs { Mode = "set", Tint = "red", TintOn = ["icon", "flag"] });
        var strip = await Send(Command.WorkspaceTint, new ControlArgs { Mode = "set", Tint = "red", TintOn = ["strip"] });

        Assert.True(session!.Ok);
        Assert.Equal(new ControlResponse { Ok = false, Error = "invalid tint placement: icon,flag (icon,name)" }, workspace);
        Assert.Equal(new ControlResponse { Ok = false, Error = "invalid tint placement: strip (icon,name)" }, strip);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionTint(null, null, ControlTintUpdate.Set(TintColor.Red, TintParts.Flag)),
        }, _actions.Calls);
    }

    // a host predating tints must refuse the tint, never create an untinted session and answer ok.
    [Fact]
    public async Task SessionNewThroughTheDefaultOverloadRefusesATint()
    {
        var plain = await Send(Command.SessionNew, new ControlArgs { Name = "s" });
        var tinted = await Send(Command.SessionNew, new ControlArgs { Tint = "red", TintOn = ["name"] });

        Assert.True(plain!.Ok);
        Assert.Equal(new ControlResponse { Ok = false, Error = "session.new --tint is not supported on this platform" }, tinted);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionNew(new ControlSessionCreateOptions(
                Window: null, Cwd: null, Workspace: null, WorkspaceName: null, CreateWorkspace: null, Command: null, Name: "s")),
        }, _actions.Calls);
    }

    [Theory]
    [InlineData(null, "icon", "--tint-on requires --tint", "--tint-on requires --tint")]
    [InlineData("teal", null, "invalid tint: teal (red|orange|yellow|green|blue|purple|gray)",
        "invalid tint: teal (red|orange|yellow|green|blue|purple|gray)")]
    [InlineData("red", "both", "invalid tint placement: both (icon,name,flag)", "invalid tint placement: both (icon,name)")]
    [InlineData("red", "name|flag", null, "invalid tint placement: name,flag (icon,name)")]
    public async Task CreationRefusesABadTintBeforeCreatingAnything(string? tint, string? tintOn, string? sessionError, string workspaceError)
    {
        var session = await Send(Command.SessionNew, new ControlArgs
        {
            Tint = tint, TintOn = Split(tintOn), WorkspaceName = "new", CreateWorkspace = true,
        });
        var workspace = await Send(Command.WorkspaceNew, new ControlArgs { Tint = tint, TintOn = Split(tintOn) });

        if (sessionError is not null)
            Assert.Equal(new ControlResponse { Ok = false, Error = sessionError }, session);
        else
            Assert.Equal("session.new --tint is not supported on this platform", session!.Error);
        Assert.Equal(new ControlResponse { Ok = false, Error = workspaceError }, workspace);
        Assert.Empty(_actions.Calls);
    }

    // a host predating tints must refuse the tint, never create an untinted workspace and answer ok.
    [Fact]
    public async Task WorkspaceNewThroughTheDefaultOverloadRefusesATint()
    {
        var plain = await Send(Command.WorkspaceNew, new ControlArgs { Name = "w" });
        var tinted = await Send(Command.WorkspaceNew, new ControlArgs { Name = "t", Tint = "red" });

        Assert.Equal(new ControlResponse { Ok = true }, plain);
        Assert.Equal(new ControlResponse { Ok = false, Error = "workspace.new --tint is not supported on this platform" }, tinted);
        Assert.Equal(new MockControlActions.Call[] { new MockControlActions.Call.WorkspaceNew(null, "w", false) }, _actions.Calls);
    }
}
