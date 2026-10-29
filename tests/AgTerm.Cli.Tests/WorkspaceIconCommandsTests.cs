using AgTerm.Cli.Tests.TestSupport;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests;

// workspace icon and icon list are Windows-only (parity/windows-only.json), so they have no ported CLI test.
public class WorkspaceIconCommandsTests
{
    private static ControlRequest Request(params string[] argv) => CliFrameworkTests.Request(argv);

    [Fact]
    public void A_name_sets_the_icon_of_the_target()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.WorkspaceIcon, Target = "9f3c",
                Args = new ControlArgs { Name = "MapPin" },
            },
            Request("workspace", "icon", "MapPin", "--target", "9f3c"));
    }

    [Fact]
    public void No_name_clears_the_icon_of_the_target()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WorkspaceIcon, Target = "9f3c", Args = new ControlArgs() },
            Request("workspace", "icon", "--target", "9f3c"));
    }

    [Fact]
    public void The_window_option_rides_in_args()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.WorkspaceIcon, Target = "9f3c",
                Args = new ControlArgs { Name = "MapPin", Window = "w" },
            },
            Request("workspace", "icon", "MapPin", "--target", "9f3c", "--window", "w"));
    }

    [Fact]
    public void Icon_list_sends_icon_list()
    {
        CanonicalJson.AssertEqual(new ControlRequest { Cmd = WireCommand.IconList }, Request("icon", "list"));
    }

    [Fact]
    public void Icons_print_one_per_line()
    {
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Icons = new[] { "A", "B" } } };

        Assert.Equal("A\nB", ClientOutput.FormatResponse(response, json: false));
    }
}
