using AgTerm.Cli.Tests.TestSupport;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests.Ported;

/// <summary>Port of agtermctlKitTests/WorkspaceCommandsTests.swift (upstream 14afbfd5): `agtermctl
/// workspace flag` argv parsing.</summary>
public class WorkspaceCommandsTests
{
    private static ControlRequest Request(params string[] argv) => CliFrameworkTests.Request(argv);

    [Fact]
    public void WorkspaceFlagDefaultsToggleOnTheActiveWorkspace()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.WorkspaceFlag, Target = "active", Args = new ControlArgs { Mode = "toggle" },
            },
            Request("workspace", "flag"));
    }

    [Theory]
    [InlineData("on")]
    [InlineData("off")]
    [InlineData("toggle")]
    public void WorkspaceFlagThreadsModeTargetAndWindow(string mode)
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.WorkspaceFlag, Target = "9f3c",
                Args = new ControlArgs { Mode = mode, Window = "win" },
            },
            Request("workspace", "flag", mode, "--target", "9f3c", "--window", "win"));
    }

    [Fact]
    public void WorkspaceFlagRejectsBadMode()
    {
        var error = Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "workspace", "flag", "clear" }));
        Assert.Equal("mode must be on, off, or toggle", Agtermctl.Message(error));
    }
}
