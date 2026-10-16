using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Protocol;

// workspace.theme is Windows-only (parity/windows-only.json), so it has no ported dispatcher test.
public class WorkspaceThemeDispatchTests
{
    [Fact]
    public void The_wire_request_decodes()
    {
        var request = ControlJson.Deserialize<ControlRequest>(
            """{"cmd":"workspace.theme","target":"W","args":{"name":"Nord"}}""");

        Assert.Equal(Command.WorkspaceTheme, request.Cmd);
        Assert.Equal("W", request.Target);
        Assert.Equal("Nord", request.Args!.Name);
    }

    [Fact]
    public async Task The_target_window_and_name_reach_the_host()
    {
        var actions = new MockControlActions();

        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceTheme, Target = "W", Args = new ControlArgs { Name = "Nord", Window = "win" },
        });

        Assert.True(response!.Ok);
        Assert.Equal(new MockControlActions.Call.WorkspaceTheme("W", "win", "Nord"), Assert.Single(actions.Calls));
    }

    [Fact]
    public async Task No_args_asks_the_host_to_clear()
    {
        var actions = new MockControlActions();

        await new ControlDispatcher(actions).Dispatch(new ControlRequest { Cmd = Command.WorkspaceTheme, Target = "W" });

        Assert.Equal(new MockControlActions.Call.WorkspaceTheme("W", null, null), Assert.Single(actions.Calls));
    }
}
