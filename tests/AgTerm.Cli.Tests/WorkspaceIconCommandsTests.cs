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
    public void A_relative_image_path_is_sent_as_an_absolute_one()
    {
        var expected = Path.GetFullPath(@"public\favicon.ico");

        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.WorkspaceIcon, Target = "9f3c",
                Args = new ControlArgs { Name = expected },
            },
            Request("workspace", "icon", @"public\favicon.ico", "--target", "9f3c"));
    }

    [Fact]
    public void An_absolute_image_path_is_sent_as_it_is()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.WorkspaceIcon, Target = "9f3c",
                Args = new ControlArgs { Name = @"C:\site\favicon.ico" },
            },
            Request("workspace", "icon", @"C:\site\favicon.ico", "--target", "9f3c"));
    }

    // the server trims the name, so a blank one clears the icon and a padded one is a name
    [Theory]
    [InlineData(" ")]
    [InlineData(" MapPin ")]
    public void A_blank_or_padded_name_is_sent_as_it_is_for_the_server_to_trim(string name)
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.WorkspaceIcon, Target = "9f3c",
                Args = new ControlArgs { Name = name },
            },
            Request("workspace", "icon", name, "--target", "9f3c"));
    }

    [Fact]
    public void A_padded_relative_image_path_is_trimmed_and_made_absolute()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.WorkspaceIcon, Target = "9f3c",
                Args = new ControlArgs { Name = Path.GetFullPath("favicon.ico") },
            },
            Request("workspace", "icon", " favicon.ico ", "--target", "9f3c"));
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
