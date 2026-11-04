using AgTerm.Cli;
using AgTerm.Cli.Tests.TestSupport;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests;

// version, sidebar width, workspace go and window go: the commands taken from upstream after the pin
// (parity/adopted.json). The expectations follow upstream CommandsTests.swift, WindowCommandsTests.swift
// and SocketClientTests.swift at v0.40.0.
public class VersionWidthGoCommandsTests
{
    private static ControlRequest Request(params string[] argv) =>
        Agtermctl.ParseAsRoot(argv).MakeRequest();

    private static string? ValidationMessage(params string[] argv)
    {
        try
        {
            Agtermctl.ParseAsRoot(argv);
            return null;
        }
        catch (Exception e)
        {
            return Agtermctl.Message(e);
        }
    }

    private static string Format(ControlResult result) =>
        ClientOutput.FormatResponse(new ControlResponse { Ok = true, Result = result }, json: false);

    [Fact]
    public void Version()
    {
        CanonicalJson.AssertEqual(new ControlRequest { Cmd = WireCommand.Version }, Request("version"));
    }

    [Fact]
    public void SidebarWidth()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.SidebarWidth, Args = new ControlArgs { SidebarWidth = 271.3 } },
            Request("sidebar", "width", "271.3"));
    }

    [Fact]
    public void SidebarWidthWithWindow()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.SidebarWidth,
                Args = new ControlArgs { Window = "abc", SidebarWidth = 300 },
            },
            Request("sidebar", "width", "300", "--window", "abc"));
    }

    [Fact]
    public void SidebarWidthRejectsNonNumericPoints()
    {
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "sidebar", "width", "wide" }));
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "sidebar", "width" }));
    }

    [Theory]
    [InlineData("nan")]
    [InlineData("inf")]
    [InlineData("-inf")]
    [InlineData("1e999")]
    public void SidebarWidthRejectsNonFinitePoints(string points)
    {
        Assert.Equal("points must be a number", ValidationMessage("sidebar", "width", points));
    }

    [Fact]
    public void WorkspaceGo()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WorkspaceGo, Args = new ControlArgs { To = "next" } },
            Request("workspace", "go", "--to", "next"));
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.WorkspaceGo,
                Args = new ControlArgs { Window = "w1", To = "prev" },
            },
            Request("workspace", "go", "--to", "prev", "--window", "w1"));
    }

    [Fact]
    public void WorkspaceGoTakesNoTarget()
    {
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(
            new[] { "workspace", "go", "--to", "next", "--target", "ab" }));
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "workspace", "go" }));
    }

    [Fact]
    public void WindowGo()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WindowGo, Args = new ControlArgs { To = "next" } },
            Request("window", "go", "--to", "next"));
        CanonicalJson.AssertEqual(
            new ControlRequest { Cmd = WireCommand.WindowGo, Args = new ControlArgs { To = "prev" } },
            Request("window", "go", "--to", "prev"));
    }

    [Fact]
    public void WindowGoTakesNoIdAndRequiresADirection()
    {
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "window", "go", "active" }));
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "window", "go" }));
    }

    // The caller compares its request against the echo to detect a clamp, so the echo keeps the
    // double's own digits and an integral width keeps its ".0", as upstream prints it.
    [Theory]
    [InlineData(271.3, "271.3")]
    [InlineData(271.34, "271.34")]
    [InlineData(300.0, "300.0")]
    public void FormatResponseSidebarWidth(double stored, string rendered)
    {
        Assert.Equal(rendered, Format(new ControlResult { SidebarWidth = stored }));
    }

    [Fact]
    public void FormatResponseVersionOnlyShowsTheInstalledIdentityWhenItDiffers()
    {
        var app = new ControlAppIdentity { Version = "0.34.0", Commit = "a1b2c3d" };

        Assert.Equal("0.34.0 (a1b2c3d)", Format(new ControlResult { App = app }));
        Assert.Equal("0.34.0 (a1b2c3d)", Format(new ControlResult { App = app, Installed = app }));
        Assert.Equal("0.34.0 (a1b2c3d)\ninstalled: 0.35.1 (def5678)", Format(new ControlResult
        {
            App = app, Installed = new ControlAppIdentity { Version = "0.35.1", Commit = "def5678" },
        }));
        Assert.Equal("0.34.0 (a1b2c3d)\ninstalled: 0.34.0 (def5678)", Format(new ControlResult
        {
            App = app, Installed = new ControlAppIdentity { Version = "0.34.0", Commit = "def5678" },
        }));
        Assert.Equal("0.34.0 (a1b2c3d)\ninstalled: 0.35.1", Format(new ControlResult
        {
            App = app, Installed = new ControlAppIdentity { Version = "0.35.1" },
        }));
    }

    [Fact]
    public void FormatResponseVersionWithoutACommitPrintsTheBareVersion()
    {
        Assert.Equal("0.34.0", Format(new ControlResult { App = new ControlAppIdentity { Version = "0.34.0" } }));
        Assert.Equal("0.34.0", Format(new ControlResult
        {
            App = new ControlAppIdentity { Version = "0.34.0", Commit = "" },
        }));
    }

    [Fact]
    public void VersionPrintsTheClientPathBesideTheAnswerButNotUnderJson()
    {
        var response = new ControlResponse
        {
            Ok = true,
            Result = new ControlResult { App = new ControlAppIdentity { Version = "0.34.0", Commit = "a1b2c3d" } },
        };
        var text = new StringWriter { NewLine = "\n" };
        var json = new StringWriter { NewLine = "\n" };
        CliEnvironment Env(StringWriter output) => new()
        {
            Out = output,
            Send = (_, _) => response,
            ClientPath = () => @"C:\tools\agtermctl.exe",
        };

        Assert.Equal(0, Agtermctl.ParseAsRoot(new[] { "version" }).Execute(Env(text)));
        Assert.Equal(0, Agtermctl.ParseAsRoot(new[] { "version", "--json" }).Execute(Env(json)));

        Assert.Equal("0.34.0 (a1b2c3d)\nclient: C:\\tools\\agtermctl.exe\n", text.ToString());
        Assert.DoesNotContain("client:", json.ToString());
    }

    [Fact]
    public void VersionOmitsTheClientLineWhenThePathIsUnknown()
    {
        var output = new StringWriter { NewLine = "\n" };
        var env = new CliEnvironment
        {
            Out = output,
            Send = (_, _) => new ControlResponse
            {
                Ok = true,
                Result = new ControlResult { App = new ControlAppIdentity { Version = "0.34.0" } },
            },
            ClientPath = () => null,
        };

        Assert.Equal(0, Agtermctl.ParseAsRoot(new[] { "version" }).Execute(env));
        Assert.Equal("0.34.0\n", output.ToString());
    }
}
