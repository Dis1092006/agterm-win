using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Protocol;

/// <summary>Dispatcher arms of version (5d22904a), sidebar.width (7977f202), workspace.go (3624dd05)
/// and window.go (99c8c50d), and the wire shape of their args and results.</summary>
public class ControlVersionWidthGoDispatchTests
{
    private static ControlResponse Err(string error) => new() { Ok = false, Error = error };

    private static void ExpectResponse(ControlResponse expected, ControlResponse? actual)
        => Assert.Equal(CanonicalJson.Of(expected), CanonicalJson.Of(actual!));

    [Fact]
    public async Task VersionAsksTheActionsAndCarriesNoTarget()
    {
        var actions = new MockControlActions();
        var identity = new ControlResult { App = new ControlAppIdentity { Version = "0.1.73" } };
        actions.NextAppVersionResponse = new ControlResponse { Ok = true, Result = identity };

        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest { Cmd = Command.Version });

        ExpectResponse(new ControlResponse { Ok = true, Result = identity }, response);
        Assert.Equal(new MockControlActions.Call[] { new MockControlActions.Call.AppVersion() }, actions.Calls);
    }

    [Fact]
    public async Task SidebarWidthForwardsTheWidthAndTheWindow()
    {
        var actions = new MockControlActions();
        actions.NextSidebarWidthResponse = new ControlResponse
        {
            Ok = true, Result = new ControlResult { SidebarWidth = 560 },
        };

        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest
        {
            Cmd = Command.SidebarWidth, Args = new ControlArgs { SidebarWidth = 9000, Window = "win" },
        });

        ExpectResponse(new ControlResponse { Ok = true, Result = new ControlResult { SidebarWidth = 560 } }, response);
        Assert.Equal(new MockControlActions.Call[] { new MockControlActions.Call.SidebarWidth(9000, "win") },
            actions.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task SidebarWidthRefusesAMissingOrNonFiniteWidth(double? width)
    {
        var actions = new MockControlActions();

        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest
        {
            Cmd = Command.SidebarWidth, Args = new ControlArgs { SidebarWidth = width },
        });

        ExpectResponse(Err("sidebar.width requires a width in points"), response);
        Assert.Empty(actions.Calls);
    }

    [Theory]
    [InlineData("next", WorkspaceNavigation.Next)]
    [InlineData("prev", WorkspaceNavigation.Previous)]
    [InlineData("previous", WorkspaceNavigation.Previous)]
    public async Task WorkspaceGoForwardsTheDirectionAndTheWindow(string to, WorkspaceNavigation expected)
    {
        var actions = new MockControlActions();
        actions.NextWorkspaceGoResponse = new ControlResponse { Ok = true, Result = new ControlResult { Id = "ws" } };

        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceGo, Args = new ControlArgs { To = to, Window = "win" },
        });

        ExpectResponse(new ControlResponse { Ok = true, Result = new ControlResult { Id = "ws" } }, response);
        Assert.Equal(new MockControlActions.Call[] { new MockControlActions.Call.WorkspaceGo("win", expected) },
            actions.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("first")]
    [InlineData("up")]
    public async Task WorkspaceGoRefusesAMissingOrUnknownDirection(string? to)
    {
        var actions = new MockControlActions();

        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest
        {
            Cmd = Command.WorkspaceGo, Args = new ControlArgs { To = to },
        });

        ExpectResponse(Err("workspace.go requires --to next|prev"), response);
        Assert.Empty(actions.Calls);
    }

    [Theory]
    [InlineData("next", WorkspaceNavigation.Next)]
    [InlineData("prev", WorkspaceNavigation.Previous)]
    public async Task WindowGoForwardsTheDirection(string to, WorkspaceNavigation expected)
    {
        var actions = new MockControlActions();
        actions.NextWindowGoResponse = new ControlResponse { Ok = true, Result = new ControlResult { Id = "win" } };

        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest
        {
            Cmd = Command.WindowGo, Args = new ControlArgs { To = to },
        });

        ExpectResponse(new ControlResponse { Ok = true, Result = new ControlResult { Id = "win" } }, response);
        Assert.Equal(new MockControlActions.Call[] { new MockControlActions.Call.WindowGo(expected) }, actions.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("last")]
    public async Task WindowGoRefusesAMissingOrUnknownDirection(string? to)
    {
        var actions = new MockControlActions();

        var response = await new ControlDispatcher(actions).Dispatch(new ControlRequest
        {
            Cmd = Command.WindowGo, Args = new ControlArgs { To = to },
        });

        ExpectResponse(Err("window.go requires --to next|prev"), response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public void WireNamesDecode()
    {
        Assert.Equal(Command.Version, ControlJson.Deserialize<Command>("\"version\""));
        Assert.Equal(Command.SidebarWidth, ControlJson.Deserialize<Command>("\"sidebar.width\""));
        Assert.Equal(Command.WorkspaceGo, ControlJson.Deserialize<Command>("\"workspace.go\""));
        Assert.Equal(Command.WindowGo, ControlJson.Deserialize<Command>("\"window.go\""));
    }

    [Fact]
    public void IdentityOmitsAnEmptyCommit()
    {
        Assert.Equal("{\"version\":\"1.2.3\"}", ControlJson.Serialize(new ControlAppIdentity { Version = "1.2.3" }));
        Assert.Equal("{\"version\":\"1.2.3\",\"commit\":\"abc1234\"}",
            ControlJson.Serialize(new ControlAppIdentity { Version = "1.2.3", Commit = "abc1234" }));
    }

    [Fact]
    public void SidebarWidthRidesTheWireAsAnUnquotedNumber()
    {
        Assert.Equal("{\"sidebarWidth\":300.5}", ControlJson.Serialize(new ControlArgs { SidebarWidth = 300.5 }));
        Assert.Equal(300.0,
            ControlJson.Deserialize<ControlResult>("{\"sidebarWidth\":300}")!.SidebarWidth);
    }
}
