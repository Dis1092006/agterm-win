using System.Diagnostics;
using AgTerm.Core.Install;
using AgTerm.Core.Model;
using AgTerm.Terminal;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// A custom command starts outside ConPTY and inherits the app environment. It gets the AGT_* set
/// and never an AGTERM_* value inherited from the app process.
/// </summary>
[Collection("process-environment")]
public class CustomCommandEnvironmentTests
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? @"C:\Windows\System32\cmd.exe";

    private static ProcessStartInfo CmdStartInfo(string shellLine) =>
        CustomCommandProcessRunner.CreateStartInfo(
            new CustomCommandInvocation(Guid.NewGuid(), "Mark", shellLine, "",
                new Dictionary<string, string> { ["AGT_SOCKET"] = "pipe" }),
            which: _ => null,
            env: key => key == "COMSPEC" ? Cmd : null);

    private static void WithForeignAgtermValues(Action body)
    {
        var saved = SurfaceEnvironment.OwnedVariables.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var name in SurfaceEnvironment.OwnedVariables)
                Environment.SetEnvironmentVariable(name, "foreign");
            body();
        }
        finally
        {
            foreach (var (name, value) in saved)
                Environment.SetEnvironmentVariable(name, value);
        }
    }

    [Fact]
    public void Start_info_drops_every_inherited_agterm_variable() => WithForeignAgtermValues(() =>
    {
        var startInfo = CmdStartInfo("echo ok");

        foreach (var name in SurfaceEnvironment.OwnedVariables)
            Assert.False(startInfo.Environment.ContainsKey(name), name);
        Assert.Equal("pipe", startInfo.Environment["AGT_SOCKET"]);
    });

    [Fact]
    public void The_started_command_does_not_see_inherited_agterm_variables() => WithForeignAgtermValues(() =>
    {
        var startInfo = CmdStartInfo("echo [%AGTERM_SOCKET%][%AGTERM_SESSION_ID%][%AGT_SOCKET%]");
        startInfo.RedirectStandardOutput = true;

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        Assert.True(process.WaitForExit(10_000));

        // cmd leaves an undefined %NAME% as is.
        Assert.Equal("[%AGTERM_SOCKET%][%AGTERM_SESSION_ID%][pipe]", output);
    });

    [Fact]
    public void A_bare_bundled_cli_is_found_without_installing_it_on_the_user_path()
    {
        var directory = Path.Join(Path.GetTempPath(), $"agterm-custom-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var workingDirectory = Path.Join(directory, "working");
            Directory.CreateDirectory(workingDirectory);
            File.WriteAllText(Path.Join(directory, CliInstall.ToolFileName), "");
            var startInfo = CustomCommandProcessRunner.CreateStartInfo(
                new CustomCommandInvocation(Guid.NewGuid(), "Run CLI", "where agtermctl", workingDirectory,
                    new Dictionary<string, string>()),
                which: _ => null,
                env: key => key == "COMSPEC" ? Cmd : null,
                processPath: Path.Join(directory, "agterm-win.exe"));
            startInfo.RedirectStandardOutput = true;

            Assert.Equal(directory, startInfo.Environment["PATH"]!.Split(';')[0]);
            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEnd().Trim();
            Assert.True(process.WaitForExit(10_000));
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(Path.Join(directory, CliInstall.ToolFileName), output.Split('\n')[0].Trim());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_missing_bundled_cli_does_not_add_the_app_directory()
    {
        var directory = Path.Join(Path.GetTempPath(), $"agterm-missing-cli-{Guid.NewGuid():N}");
        var startInfo = CustomCommandProcessRunner.CreateStartInfo(
            new CustomCommandInvocation(Guid.NewGuid(), "Run CLI", "echo ok", "",
                new Dictionary<string, string>()),
            which: _ => null,
            env: key => key == "COMSPEC" ? Cmd : null,
            processPath: Path.Join(directory, "agterm-win.exe"));

        Assert.DoesNotContain(directory, startInfo.Environment["PATH"]!, StringComparison.OrdinalIgnoreCase);
    }

    private static CustomCommandProcessRunner CmdRunner() =>
        new(which: _ => null, env: key => key == "COMSPEC" ? Cmd : null);

    private static CustomCommandInvocation Run(string shellLine, bool errorHud) =>
        new(Guid.NewGuid(), "Build", shellLine, "", new Dictionary<string, string>(),
            Guid.NewGuid(), errorHud);

    [Fact]
    public async Task An_opted_in_command_reports_its_exit_code_and_last_stderr_line()
    {
        var handle = CmdRunner().Start(Run("echo first 1>&2 & echo build broke 1>&2 & exit /b 3", errorHud: true));

        var exit = await handle.Finished.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(3, exit.ExitCode);
        Assert.Equal("build broke", exit.Detail);
        await handle.Exited.WaitAsync(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task An_opted_in_command_still_runs_without_capture_when_the_stderr_file_cannot_be_created()
    {
        var original = StderrCapturingProcess.StderrDirectory;
        StderrCapturingProcess.StderrDirectory = () => Path.Join(Path.GetTempPath(), $"agterm-no-dir-{Guid.NewGuid():N}");
        try
        {
            var handle = CmdRunner().Start(Run("echo oops 1>&2 & exit /b 5", errorHud: true));

            var exit = await handle.Finished.WaitAsync(TimeSpan.FromSeconds(60));

            Assert.Equal(5, exit.ExitCode);
            Assert.Null(exit.Detail);
        }
        finally
        {
            StderrCapturingProcess.StderrDirectory = original;
        }
    }

    [Fact]
    public async Task A_plain_command_reports_its_exit_code_without_detail()
    {
        var handle = CmdRunner().Start(Run("echo oops 1>&2 & exit /b 4", errorHud: false));

        var exit = await handle.Finished.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(4, exit.ExitCode);
        Assert.Null(exit.Detail);
        await handle.Exited.WaitAsync(TimeSpan.FromSeconds(60));
    }
}
