using System.Diagnostics;

namespace AgTerm.Terminal.Tests;

/// <summary>
/// Shell-integration injection policy (PLATFORM-NOTES: WebView2/xterm.js has no analogue of
/// libghostty's automatic shell-integration injection, which is where upstream's OSC 7 comes from).
///
/// A spawned shell reports its cwd only if it is asked to: no Windows shell emits OSC 7 / OSC 9;9
/// on its own, so Session.CurrentCwd stays null and EffectiveCwd falls back to InitialCwd forever.
/// These tests pin WHICH shells get an integration script.
/// </summary>
public class ShellIntegrationTests
{
    [Theory]
    [InlineData("pwsh.exe", ShellKind.PowerShellCore)]
    [InlineData("powershell.exe", ShellKind.WindowsPowerShell)]
    [InlineData("cmd.exe", ShellKind.Cmd)]
    [InlineData("bash.exe", ShellKind.Bash)]
    public void Identifies_the_supported_shells_by_executable_name(string exe, ShellKind expected)
    {
        Assert.Equal(expected, ShellIntegration.Identify(exe));
    }

    [Theory]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe", ShellKind.PowerShellCore)]
    [InlineData(@"C:\Windows\System32\cmd.exe", ShellKind.Cmd)]
    [InlineData(@"C:\Program Files\Git\bin\bash.exe", ShellKind.Bash)]
    public void Identifies_a_shell_behind_a_full_path(string path, ShellKind expected)
    {
        Assert.Equal(expected, ShellIntegration.Identify(path));
    }

    [Theory]
    [InlineData("PWSH.EXE", ShellKind.PowerShellCore)]
    [InlineData("Cmd.Exe", ShellKind.Cmd)]
    public void Shell_identification_ignores_case(string exe, ShellKind expected)
    {
        // CreateProcessW is case-insensitive, so the same shell must not read as unsupported
        // merely because a config or %COMSPEC% spelled it differently.
        Assert.Equal(expected, ShellIntegration.Identify(exe));
    }

    [Theory]
    [InlineData("wsl.exe")] // another OS: a C:\ script path does not exist inside it
    [InlineData("nu.exe")]
    [InlineData("fish.exe")]
    [InlineData("")]
    public void An_unsupported_shell_is_identified_as_such(string exe)
    {
        // Unsupported must be a value, never an exception: an unknown shell has to keep working
        // exactly as before (just without cwd tracking), per the parity policy on adaptations.
        Assert.Equal(ShellKind.Unsupported, ShellIntegration.Identify(exe));
    }

    // --- argv injection -------------------------------------------------------------------

    private const string ScriptDir = @"C:\app\integration";

    [Fact]
    public void Powershell_argv_dot_sources_the_script_text_and_keeps_the_session_interactive()
    {
        var argv = ShellIntegration.Inject([@"C:\pwsh.exe"], ScriptDir);

        // -NoExit is what keeps this a usable shell: without it pwsh runs the -Command and quits.
        // The script's text is dot-sourced, not the file: execution policy checks script files
        // only, and Windows PowerShell's default (Restricted) refuses `. 'integration.ps1'`.
        Assert.Equal(
            [@"C:\pwsh.exe", "-NoExit", "-Command",
                @". ([scriptblock]::Create([IO.File]::ReadAllText('C:\app\integration\integration.ps1')))"],
            argv);
    }

    [Fact]
    public void Powershell_script_path_is_single_quoted_against_spaces()
    {
        // 'Program Files' would otherwise split into two -Command tokens.
        var argv = ShellIntegration.Inject(["pwsh.exe"], @"C:\Program Files\agterm");

        Assert.Equal(
            @". ([scriptblock]::Create([IO.File]::ReadAllText('C:\Program Files\agterm\integration.ps1')))",
            argv[^1]);
    }

    [Fact]
    public void Windows_powershell_gets_the_same_script_as_powershell_core()
    {
        var argv = ShellIntegration.Inject(["powershell.exe"], ScriptDir);

        Assert.Contains("-NoExit", argv);
        Assert.EndsWith(@"integration.ps1')))", argv[^1]);
    }

    [Fact]
    public void Cmd_argv_calls_the_batch_script_with_slash_k()
    {
        var argv = ShellIntegration.Inject(["cmd.exe"], ScriptDir);

        // /k runs the script and stays; /c would exit. call and the path are separate arguments:
        // the spawn quotes a path with spaces itself, and quotes embedded in one argument would
        // reach cmd as \" (see the test below).
        Assert.Equal(["cmd.exe", "/k", "call", @"C:\app\integration\integration.cmd"], argv);
    }

    [Fact]
    public void Cmd_runs_the_injected_script_from_a_path_with_spaces_and_a_hash()
    {
        // cmd does not parse its command line with the MSVCRT rules WindowsCommandLine.FromArgv
        // inverts, so the argv shape is pinned against a real cmd.exe, not only as a list.
        var root = Path.Combine(Path.GetTempPath(), "agterm-int " + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "C# dir");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "integration.cmd"), "@echo INTEGRATION-RAN\r\n");
            var line = WindowsCommandLine.FromArgv(ShellIntegration.Inject(["cmd.exe"], dir));
            var start = new ProcessStartInfo("cmd.exe", line[(line.IndexOf(' ') + 1)..])
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(start)!;
            process.StandardInput.Close(); // /k keeps cmd interactive; end of input ends it
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(10_000), "cmd did not exit");

            Assert.DoesNotContain("is not recognized", output, StringComparison.Ordinal);
            Assert.Contains("INTEGRATION-RAN", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Bash_argv_uses_an_rcfile_so_the_users_bashrc_still_loads()
    {
        var argv = ShellIntegration.Inject(["bash.exe"], ScriptDir);

        // --rcfile replaces ~/.bashrc, so our script is responsible for sourcing it; -i keeps
        // the shell interactive, which is what makes bash read an rcfile at all.
        Assert.Equal(["bash.exe", "--rcfile", @"C:\app\integration\integration.bash", "-i"], argv);
    }

    [Fact]
    public void An_unsupported_shell_argv_is_returned_untouched()
    {
        string[] original = ["wsl.exe"];

        var argv = ShellIntegration.Inject(original, ScriptDir);

        Assert.Equal(original, argv);
    }

    [Fact]
    public void A_command_argv_is_never_rewritten()
    {
        // THE regression guard for restore/park-and-resume. LiveControlHost puts a captured
        // foreground command (session.restore, --command, ForegroundCommand) in argv INSTEAD of
        // the shell; injecting -NoExit -Command there would replace the user's program with a
        // shell and silently break restore. Only a bare, single-element shell argv is rewritten.
        string[] command = ["npm", "run", "dev"];

        Assert.Equal(command, ShellIntegration.Inject(command, ScriptDir));
    }

    [Fact]
    public void A_shell_that_already_carries_arguments_is_left_alone()
    {
        // A configured shell with its own switches is the user's explicit intent; appending
        // -NoExit -Command could contradict it (and pwsh rejects -Command twice).
        string[] configured = ["pwsh.exe", "-NoLogo"];

        Assert.Equal(configured, ShellIntegration.Inject(configured, ScriptDir));
    }

    [Fact]
    public void Injection_is_skipped_when_no_script_directory_is_available()
    {
        // A published layout missing its integration resources must still spawn a working shell.
        string[] shell = ["pwsh.exe"];

        Assert.Equal(shell, ShellIntegration.Inject(shell, scriptDirectory: null));
        Assert.Equal(shell, ShellIntegration.Inject(shell, scriptDirectory: "   "));
    }

    // --- readline marker ------------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe")]
    [InlineData("powershell.exe")]
    [InlineData(@"C:\Program Files\agterm")]
    public void An_injected_powershell_argv_reports_the_readline_marker(string shellOrDir)
    {
        // The page listens for the integration's readline marker only in a pane the app itself
        // started PowerShell with integration.ps1; other panes treat it as ordinary output.
        var argv = shellOrDir.EndsWith(".exe", StringComparison.Ordinal)
            ? ShellIntegration.Inject([shellOrDir], ScriptDir)
            : ShellIntegration.Inject(["pwsh.exe"], shellOrDir);

        Assert.True(ShellIntegration.ReportsReadline(argv));
    }

    public static TheoryData<string[]> ArgvWithoutTheReadlineMarker() => new()
    {
        ShellIntegration.Inject(["cmd.exe"], ScriptDir).ToArray(),
        ShellIntegration.Inject(["bash.exe"], ScriptDir).ToArray(),
        new[] { "pwsh.exe" },
        new[] { "pwsh.exe", "-NoLogo" },
        new[] { "pwsh.exe", "-NoExit", "-Command", @". 'C:\other\profile.ps1'" },
        new[] { "pwsh.exe", "-NoExit", "-Command", @". 'C:\app\integration\integration.ps1'" },
        new[] { "pwsh.exe", "-NoExit", "-Command",
            @". ([scriptblock]::Create([IO.File]::ReadAllText('C:\other\profile.ps1')))" },
        new[] { "pwsh.exe", "-NoExit", "-Command",
            @". ([scriptblock]::Create([IO.File]::ReadAllText('C:\app\integration\integration.ps1'))); vim" },
        new[] { "pwsh.exe", "-Command",
            @". ([scriptblock]::Create([IO.File]::ReadAllText('C:\app\integration\integration.ps1')))" },
        new[] { "cmd.exe", "-NoExit", "-Command", @". 'C:\app\integration\integration.ps1'" },
        new[] { "npm", "run", "dev" },
        Array.Empty<string>(),
    };

    [Theory]
    [MemberData(nameof(ArgvWithoutTheReadlineMarker))]
    public void Any_other_argv_does_not_report_the_readline_marker(string[] argv)
    {
        Assert.False(ShellIntegration.ReportsReadline(argv));
    }

    // --- script directory resolution ------------------------------------------------------

    [Fact]
    public void The_script_directory_is_found_beside_a_given_root()
    {
        var root = Directory.CreateTempSubdirectory("agterm-scriptdir-").FullName;
        try
        {
            Assert.Null(ShellIntegration.ResolveScriptDirectory(root));

            var expected = Directory.CreateDirectory(
                Path.Combine(root, ShellIntegration.DirectoryName)).FullName;

            Assert.Equal(expected, ShellIntegration.ResolveScriptDirectory(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_missing_script_directory_resolves_to_null_rather_than_throwing()
    {
        // The caller treats null as "spawn a plain shell", so a layout without the scripts
        // must degrade instead of failing the spawn.
        Assert.Null(ShellIntegration.ResolveScriptDirectory(
            Path.Combine(Path.GetTempPath(), "agterm-does-not-exist-" + Guid.NewGuid().ToString("N"))));
        Assert.Null(ShellIntegration.ResolveScriptDirectory("   "));
    }
}
