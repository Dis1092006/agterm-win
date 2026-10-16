using AgTerm.Cli.Tests.TestSupport;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests;

// workspace theme is Windows-only (parity/windows-only.json), so it has no ported CLI test.
public class WorkspaceThemeCommandTests
{
    private static ControlRequest Request(params string[] argv) => CliFrameworkTests.Request(argv);

    [Fact]
    public void A_name_sets_the_theme_of_the_target()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.WorkspaceTheme, Target = "9f3c",
                Args = new ControlArgs { Name = "Nord" },
            },
            Request("workspace", "theme", "Nord", "--target", "9f3c"));
    }

    [Fact]
    public void No_name_clears_the_theme_of_the_active_workspace()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WorkspaceTheme, Target = "active", Args = new ControlArgs() },
            Request("workspace", "theme"));
    }

    [Fact]
    public void The_window_option_rides_in_args()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.WorkspaceTheme, Target = "active",
                Args = new ControlArgs { Name = "Nord", Window = "w1" },
            },
            Request("workspace", "theme", "Nord", "--window", "w1"));
    }
}
