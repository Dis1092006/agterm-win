using System.Diagnostics;
using AgTerm.Core.Model;

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
}
