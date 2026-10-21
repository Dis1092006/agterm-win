using AgTerm.Cli;
using AgTerm.ControlServer;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests;

/// <summary>Own framework tests on top of the ported agtermctlKit suites: the ParseAsRoot seam,
/// pipe-name resolution, and the injectable run environment.</summary>
public class CliFrameworkTests
{
    internal static ControlRequest Request(params string[] argv) =>
        Agtermctl.ParseAsRoot(argv).MakeRequest();

    [Fact]
    public void WindowsCommandDescriptionsReflectTheLiveHost()
    {
        static System.CommandLine.Command Child(System.CommandLine.Command parent, string name) =>
            Assert.Single(parent.Subcommands, command => command.Name == name);

        var bindings = new CommandBindings();
        var session = SessionCommands.Build(bindings);
        var config = ConfigCommands.Build(bindings);
        var theme = ThemeCommands.Build(bindings);

        Assert.Contains("File Explorer", Child(session, "reveal").Description);
        Assert.Contains("Ctrl+V", Child(session, "paste").Description);
        Assert.DoesNotContain("⌘", Child(session, "paste").Description);
        Assert.Contains("no default shortcut", Child(session, "select-all").Description);
        Assert.DoesNotContain("⌘", Child(session, "select-all").Description);
        Assert.Contains("settings.json", Child(config, "reload").Description);
        Assert.DoesNotContain("ghostty.conf", Child(config, "reload").Description);
        Assert.Contains("built-in terminal colors", Child(theme, "set").Arguments.Single().Description);
        Assert.Contains("control pipe", Child(config, "reload").Options
            .Single(option => option.Name == "--socket").Description);
    }

    [Fact]
    public void TreeParsesToABareTreeRequest()
    {
        var request = Request("tree");
        Assert.Equal(WireCommand.Tree, request.Cmd);
        Assert.Null(request.Target);
        Assert.Null(request.Args);
    }

    [Fact]
    public void WorkspaceNewBindsNameCollapsedAndWindow()
    {
        var request = Request("workspace", "new", "Work", "--collapsed", "--window", "win");
        Assert.Equal(WireCommand.WorkspaceNew, request.Cmd);
        Assert.Equal("Work", request.Args?.Name);
        Assert.Equal(true, request.Args?.Collapsed);
        Assert.Equal("win", request.Args?.Window);
    }

    [Fact]
    public void WorkspaceFocusRejectsABadModeAtParseTime()
    {
        var error = Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(
            new[] { "workspace", "focus", "sideways" }));
        Assert.Equal("mode must be on, off, or toggle", Agtermctl.Message(error));
    }

    [Fact]
    public void UnknownSubcommandFailsToParse()
    {
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "nope" }));
    }

    [Fact]
    public void RunSendsOverTheInjectedTransportAndPrintsHumanOutput()
    {
        var output = new StringWriter();
        var seen = new List<(string Pipe, ControlRequest Request)>();
        var env = new CliEnvironment
        {
            GetEnv = key => key == "AGTERM_CONTROL_SOCKET" ? "test-pipe" : null,
            Out = output,
            Send = (pipe, request) =>
            {
                seen.Add((pipe, request));
                return new ControlResponse { Ok = true };
            },
        };

        var exit = Agtermctl.Run(new[] { "workspace", "delete", "--target", "9f3c" }, env);

        Assert.Equal(0, exit);
        Assert.Equal("ok", output.ToString().TrimEnd());
        var (pipe, request) = Assert.Single(seen);
        Assert.Equal("test-pipe", pipe);
        Assert.Equal(WireCommand.WorkspaceDelete, request.Cmd);
        Assert.Equal("9f3c", request.Target);
    }

    [Fact]
    public void RunEchoesResultIdOnlyForCreateCommands()
    {
        var output = new StringWriter();
        var env = new CliEnvironment
        {
            GetEnv = _ => "test-pipe",
            Out = output,
            Send = (_, _) => new ControlResponse
            {
                Ok = true, Result = new ControlResult { Id = "NEW-ID" },
            },
        };

        Assert.Equal(0, Agtermctl.Run(new[] { "workspace", "new", "Work" }, env));
        Assert.Equal(0, Agtermctl.Run(new[] { "workspace", "rename", "X" }, env));

        Assert.Equal(new[] { "NEW-ID", "ok" },
            output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd()).ToArray());
    }

    [Fact]
    public void FailedResponseGoesToStderrWithExitOne()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var env = new CliEnvironment
        {
            GetEnv = _ => "test-pipe",
            Out = output,
            Error = error,
            Send = (_, _) => new ControlResponse { Ok = false, Error = "no such workspace: x" },
        };

        var exit = Agtermctl.Run(new[] { "workspace", "delete", "--target", "x" }, env);

        Assert.Equal(1, exit);
        Assert.Equal("", output.ToString());
        Assert.Equal("error: no such workspace: x", error.ToString().TrimEnd());
    }

    [Fact]
    public void ExplicitSocketDerivesThePipeNameAndBeatsTheEnvOverride()
    {
        var command = Agtermctl.ParseAsRoot(new[] { "tree", "--socket", @"C:\tmp\explicit.sock" });
        var pipe = command.Options.PipeName(key => key == "AGTERM_CONTROL_SOCKET" ? "env-pipe" : null);
        Assert.Equal(ControlPipeName.Derive(@"C:\tmp\explicit.sock"), pipe);
    }

    [Fact]
    public void EnvPipeOverrideBeatsTheDerivedStateDirName()
    {
        var command = Agtermctl.ParseAsRoot(new[] { "tree" });
        var pipe = command.Options.PipeName(key => key switch
        {
            "AGTERM_CONTROL_SOCKET" => "env-pipe",
            "AGTERM_STATE_DIR" => @"C:\tmp\state",
            _ => null,
        });
        Assert.Equal("env-pipe", pipe);
    }

    [Fact]
    public void StateDirDrivesTheSocketPathResolution()
    {
        var command = Agtermctl.ParseAsRoot(new[] { "tree" });
        var path = command.Options.SocketPath(key => key == "AGTERM_STATE_DIR" ? @"C:\tmp\state" : null);
        Assert.Equal(Path.Combine(@"C:\tmp\state", "agterm.sock"), path);
    }
}
