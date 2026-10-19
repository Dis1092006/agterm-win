using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public class SurfaceEnvironmentTests
{
    private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid WindowId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WorkspaceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string Socket = @"C:\state\agterm.sock";

    [Fact]
    public void Session_environment_carries_all_known_identifiers()
    {
        var env = SurfaceEnvironment.Session(SessionId, WindowId, WorkspaceId, Socket);

        Assert.Equal(new Dictionary<string, string>
        {
            ["AGTERM_ENABLED"] = "1",
            ["AGTERM_SESSION_ID"] = "11111111-1111-1111-1111-111111111111",
            ["AGTERM_WINDOW_ID"] = "22222222-2222-2222-2222-222222222222",
            ["AGTERM_WORKSPACE_ID"] = "33333333-3333-3333-3333-333333333333",
            ["AGTERM_SOCKET"] = Socket,
        }, env);
    }

    [Fact]
    public void Uuids_are_upper_case()
    {
        var id = Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789");

        var env = SurfaceEnvironment.Session(id, id, id, Socket);

        Assert.Equal("ABCDEF01-2345-6789-ABCD-EF0123456789", env["AGTERM_SESSION_ID"]);
        Assert.Equal("ABCDEF01-2345-6789-ABCD-EF0123456789", env["AGTERM_WINDOW_ID"]);
        Assert.Equal("ABCDEF01-2345-6789-ABCD-EF0123456789", env["AGTERM_WORKSPACE_ID"]);
        Assert.Equal("ABCDEF01-2345-6789-ABCD-EF0123456789", SurfaceEnvironment.QuickTerminal(id, Socket)["AGTERM_WINDOW_ID"]);
    }

    [Fact]
    public void Session_environment_omits_unknown_window_and_workspace()
    {
        var env = SurfaceEnvironment.Session(SessionId, null, null, Socket);

        Assert.DoesNotContain("AGTERM_WINDOW_ID", env.Keys);
        Assert.DoesNotContain("AGTERM_WORKSPACE_ID", env.Keys);
        Assert.Equal("1", env["AGTERM_ENABLED"]);
    }

    [Theory]
    [InlineData(StatusPane.Left, "left")]
    [InlineData(StatusPane.Right, "right")]
    [InlineData(StatusPane.Scratch, "scratch")]
    public void Session_environment_injects_pane_when_given(StatusPane pane, string wire)
    {
        Assert.Equal(wire, SurfaceEnvironment.Session(SessionId, WindowId, WorkspaceId, Socket, pane)["AGTERM_PANE"]);
    }

    [Fact]
    public void Session_environment_omits_pane_when_null()
    {
        Assert.DoesNotContain("AGTERM_PANE", SurfaceEnvironment.Session(SessionId, WindowId, WorkspaceId, Socket).Keys);
    }

    [Fact]
    public void Session_environment_injects_pane_token_when_given()
    {
        var env = SurfaceEnvironment.Session(SessionId, WindowId, WorkspaceId, Socket, StatusPane.Left, "tok-1");

        Assert.Equal("tok-1", env["AGTERM_PANE_ID"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Session_environment_omits_pane_token_when_null_or_empty(string? token)
    {
        var env = SurfaceEnvironment.Session(SessionId, WindowId, WorkspaceId, Socket, StatusPane.Left, token);

        Assert.DoesNotContain("AGTERM_PANE_ID", env.Keys);
    }

    [Fact]
    public void Quick_terminal_environment_omits_session_and_workspace_identifiers()
    {
        var env = SurfaceEnvironment.QuickTerminal(WindowId, Socket);

        Assert.Equal(new Dictionary<string, string>
        {
            ["AGTERM_ENABLED"] = "1",
            ["AGTERM_WINDOW_ID"] = "22222222-2222-2222-2222-222222222222",
            ["AGTERM_SOCKET"] = Socket,
        }, env);
    }

    [Fact]
    public void Empty_socket_path_is_still_emitted()
    {
        Assert.Equal("", SurfaceEnvironment.Session(SessionId, null, null, "")["AGTERM_SOCKET"]);
        Assert.Equal("", SurfaceEnvironment.QuickTerminal(WindowId, "")["AGTERM_SOCKET"]);
    }

    [Fact]
    public void Null_socket_path_omits_the_socket()
    {
        // The AGTERM_CONTROL_SOCKET override names a pipe, not a path: nothing to advertise.
        Assert.DoesNotContain("AGTERM_SOCKET", SurfaceEnvironment.Session(SessionId, WindowId, WorkspaceId, null).Keys);
        Assert.DoesNotContain("AGTERM_SOCKET", SurfaceEnvironment.QuickTerminal(WindowId, null).Keys);
    }

    [Fact]
    public void Builder_sets_no_terminal_identity()
    {
        // TERM and TERM_PROGRAM belong to TerminalIdentity; TERM_PROGRAM_VERSION is never set here.
        var env = SurfaceEnvironment.Session(SessionId, WindowId, WorkspaceId, Socket, StatusPane.Left, "tok");

        Assert.DoesNotContain("TERM_PROGRAM", env.Keys);
        Assert.DoesNotContain("TERM_PROGRAM_VERSION", env.Keys);
        Assert.DoesNotContain("TERM", env.Keys);
    }

    [Fact]
    public void Owned_variables_cover_everything_the_builder_can_emit()
    {
        var full = SurfaceEnvironment.Session(SessionId, WindowId, WorkspaceId, Socket, StatusPane.Left, "tok");

        Assert.Equal(
            new[] { "AGTERM_ENABLED", "AGTERM_SESSION_ID", "AGTERM_WINDOW_ID", "AGTERM_WORKSPACE_ID",
                    "AGTERM_SOCKET", "AGTERM_PANE", "AGTERM_PANE_ID" },
            SurfaceEnvironment.OwnedVariables);
        Assert.Subset(SurfaceEnvironment.OwnedVariables.ToHashSet(), full.Keys.ToHashSet());
        Assert.Equal(7, full.Count);
    }
}
