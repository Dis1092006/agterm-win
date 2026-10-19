using AgTerm.Core.Install;

namespace AgTerm.Core.Tests.Install;

public sealed class CliInstallTests
{
    private const string Dir = @"C:\Program Files\agterm-win";

    private sealed class FakeEnvironment : IUserPathEnvironment
    {
        public UserPathValue? UserPath { get; set; }
        public string? ProcessPath { get; set; } = @"C:\Windows\system32;C:\Windows";
        public Exception? WriteError { get; set; }
        public List<string> Log { get; } = new();

        public UserPathValue? ReadUserPath() => UserPath;

        public void WriteUserPath(UserPathValue value)
        {
            if (WriteError is not null)
                throw WriteError;
            Log.Add($"write-user {value.Value} expandable={value.Expandable}");
            UserPath = value;
        }

        public void BroadcastChange() => Log.Add("broadcast");

        public string? ReadProcessPath() => ProcessPath;

        public void WriteProcessPath(string value)
        {
            Log.Add($"write-process {value}");
            ProcessPath = value;
        }

        public string ExpandVariables(string value) =>
            value.Replace("%LOCALAPPDATA%", @"C:\Users\me\AppData\Local", StringComparison.OrdinalIgnoreCase);
    }

    private static string Added(string dir) =>
        $"agtermctl was added to your user Path: {dir}. Terminals that are already open, including open agterm-win panes, do not see the change. Open a new terminal and run “agtermctl --help”.";

    [Fact]
    public void MissingToolIsNotBundled()
    {
        var env = new FakeEnvironment();
        var outcome = CliInstall.Install(Dir, toolExists: false, env);
        Assert.Equal(new InstallOutcome("Install Failed", "agtermctl is not bundled in this build.", IsWarning: true), outcome);
        Assert.Empty(env.Log);
    }

    [Fact]
    public void AbsentUserPathIsCreatedExpandableThenBroadcastThenProcessPath()
    {
        var env = new FakeEnvironment();

        var outcome = CliInstall.Install(Dir, toolExists: true, env);

        Assert.Equal(new InstallOutcome("Command Line Tool Installed", Added(Dir), IsWarning: false), outcome);
        Assert.Equal(
            new[]
            {
                $"write-user {Dir} expandable=True",
                "broadcast",
                $@"write-process C:\Windows\system32;C:\Windows;{Dir}",
            },
            env.Log);
    }

    [Fact]
    public void ExpandableValueKeepsItsKindAndVariables()
    {
        var env = new FakeEnvironment { UserPath = new UserPathValue(@"%USERPROFILE%\bin;C:\x", Expandable: true) };
        CliInstall.Install(Dir, toolExists: true, env);
        Assert.Equal(new UserPathValue($@"%USERPROFILE%\bin;C:\x;{Dir}", Expandable: true), env.UserPath);
    }

    [Fact]
    public void PlainValueStaysPlain()
    {
        var env = new FakeEnvironment { UserPath = new UserPathValue(@"C:\x;", Expandable: false) };
        CliInstall.Install(Dir, toolExists: true, env);
        Assert.Equal(new UserPathValue($@"C:\x;{Dir}", Expandable: false), env.UserPath);
    }

    [Fact]
    public void DirectoryAlreadyOnUserPathIsNotWrittenAgain()
    {
        var env = new FakeEnvironment { UserPath = new UserPathValue(@"C:\x;c:\program files\AGTERM-WIN\", Expandable: true) };

        var outcome = CliInstall.Install(Dir, toolExists: true, env);

        Assert.Equal(
            new InstallOutcome(
                "Command Line Tool Installed",
                $"agtermctl is already on your user Path: {Dir}. Open a new terminal and run “agtermctl --help”.",
                IsWarning: false),
            outcome);
        Assert.Equal(new[] { $@"write-process C:\Windows\system32;C:\Windows;{Dir}" }, env.Log);
    }

    [Fact]
    public void DirectoryOnUserPathThroughAVariableIsNotWrittenAgain()
    {
        const string dir = @"C:\Users\me\AppData\Local\agterm-win";
        var env = new FakeEnvironment
        {
            UserPath = new UserPathValue(@"C:\x;%LOCALAPPDATA%\agterm-win", Expandable: true),
            ProcessPath = $@"C:\Windows;{dir}",
        };

        var outcome = CliInstall.Install(dir, toolExists: true, env);

        Assert.StartsWith("agtermctl is already on your user Path", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(env.Log);
        Assert.Equal(new UserPathValue(@"C:\x;%LOCALAPPDATA%\agterm-win", Expandable: true), env.UserPath);
    }

    [Fact]
    public void ProcessPathThatHasTheDirectoryIsLeftAlone()
    {
        var env = new FakeEnvironment { ProcessPath = $@"C:\Windows;{Dir}\" };
        CliInstall.Install(Dir, toolExists: true, env);
        Assert.Equal(new[] { $"write-user {Dir} expandable=True", "broadcast" }, env.Log);
    }

    [Fact]
    public void EmptyProcessPathGetsTheDirectory()
    {
        var env = new FakeEnvironment { ProcessPath = null };
        CliInstall.Install(Dir, toolExists: true, env);
        Assert.Equal(Dir, env.ProcessPath);
    }

    [Fact]
    public void WriteFailureReportsTheMessageAndLeavesTheProcessPathAlone()
    {
        var env = new FakeEnvironment { WriteError = new UnauthorizedAccessException("Access is denied.") };

        var outcome = CliInstall.Install(Dir, toolExists: true, env);

        Assert.Equal(new InstallOutcome("Install Failed", "Access is denied.", IsWarning: true), outcome);
        Assert.Empty(env.Log);
        Assert.Equal(@"C:\Windows\system32;C:\Windows", env.ProcessPath);
    }

    [Fact]
    public void ToolFileNameIsTheExe()
    {
        Assert.Equal("agtermctl", CliInstall.ToolName);
        Assert.Equal("agtermctl.exe", CliInstall.ToolFileName);
    }
}
