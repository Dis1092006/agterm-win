using System.Text;
using System.Text.RegularExpressions;

namespace AgTerm.Terminal.Tests;

/// <summary>
/// integration.ps1's readline marker against a REAL PowerShell over a pseudoconsole. The page
/// sends a paste key to PSReadLine only between "agterm-readline=1" and "agterm-readline=0", so
/// these pin when the shell sends them and which paste key it names.
///
/// Each shell starts with -NoProfile and saves no history, so nothing of the user's is read or
/// written. Every wait has a deadline, and Dispose kills the shell, also on failure. The shell
/// ends itself with [Environment]::Exit(0): `exit` under -NoExit with a dot-sourced script hangs
/// Windows PowerShell 5.1.
/// </summary>
public class PowerShellReadlineIntegrationTests : IDisposable
{
    private readonly StringBuilder _output = new();
    private ConPtySession? _pty;

    private static readonly TimeSpan ChildBudget = TimeSpan.FromSeconds(60);

    /// <summary>Windows PowerShell is on every Windows machine; pwsh only where it is installed
    /// (xunit 2 has no runtime skip, so a missing pwsh is left out of the list).</summary>
    public static IEnumerable<object[]> Shells()
    {
        yield return [Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe")];
        var pwsh = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir.Trim(), "pwsh.exe"))
            .FirstOrDefault(File.Exists);
        if (pwsh is not null) yield return [pwsh];
    }

    private static string ScriptPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var path = Path.Combine(dir!, "src", "AgTerm.Xterm", "Integration", "integration.ps1");
        Assert.True(File.Exists(path), $"integration script missing: {path}");
        return path;
    }

    /// <summary>Starts the shell with the integration loaded after <paramref name="setup"/>, by
    /// the same command the app's -NoExit -Command uses. setup must not contain a double quote:
    /// Windows PowerShell 5.1 parses them out of -Command.</summary>
    private ConPtySession StartShell(string shell, string setup = "")
    {
        _pty = ConPtySession.Start(new ConPtyStartInfo
        {
            Argv =
            [
                shell, "-NoProfile", "-NoLogo", "-NoExit", "-Command",
                $"Set-PSReadLineOption -HistorySaveStyle SaveNothing; {setup}{ShellIntegration.LoadScriptCommand(ScriptPath())}",
            ],
            Cols = 120,
            Rows = 30,
            ExtraEnvironment = new Dictionary<string, string> { ["AGTERM_ENABLED"] = "1" },
            OnOutput = chunk =>
            {
                lock (_output) _output.Append(Encoding.UTF8.GetString(chunk));
            },
        });
        return _pty;
    }

    private string Snapshot()
    {
        lock (_output) return _output.ToString();
    }

    /// <summary>Waits for <paramref name="pattern"/> at or after <paramref name="from"/> and
    /// returns the index just past the match.</summary>
    private int WaitFor(string pattern, int from = 0)
    {
        var regex = new Regex(pattern);
        var deadline = Environment.TickCount64 + (long)ChildBudget.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            var text = Snapshot();
            var m = regex.Match(text, Math.Min(from, text.Length));
            if (m.Success) return m.Index + m.Length;
            Thread.Sleep(25);
        }
        Assert.Fail($"output never matched /{pattern}/ after {from}; got:\n{Snapshot()}");
        return 0; // unreachable — Assert.Fail throws
    }

    // ConPTY may end an OSC with ST instead of BEL
    private static string Marker(string payload) =>
        $@"\x1b\]7777;{Regex.Escape(payload)}(?:\x07|\x1b\\)";

    public void Dispose() => _pty?.Dispose();

    [Theory]
    [MemberData(nameof(Shells))]
    public async Task The_shell_marks_when_readline_reads_and_which_key_pastes(string shell)
    {
        var pty = StartShell(shell);

        var at = WaitFor(Marker("agterm-readline=1;paste=shift-insert"));
        pty.WriteText("'mark-'+'nineteen'\r");
        // the marker is cleared when the line is accepted, before the command writes anything
        at = WaitFor(Marker("agterm-readline=0"), at);
        at = WaitFor("mark-nineteen", at);

        // Vi insert mode binds Ctrl+v to Paste, and Shift+Insert to nothing
        pty.WriteText("Set-PSReadLineOption -EditMode Vi\r");
        at = WaitFor(Marker("agterm-readline=1;paste=ctrl-v"), at);

        // Emacs binds no paste key to Paste
        pty.WriteText("Set-PSReadLineOption -EditMode Emacs\r");
        at = WaitFor(Marker("agterm-readline=0"), at);
        WaitFor(Marker("agterm-readline=1"), at);

        pty.WriteText("[Environment]::Exit(0)\r");
        Assert.Equal(0, await pty.Exited.WaitAsync(ChildBudget));
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public async Task The_ctrl_z_record_with_the_latin_letter_runs_undo(string shell)
    {
        // The record the page sends for Ctrl+Z on a non-Latin layout. PSReadLine has to name it
        // Ctrl+z from the character, without the layout, or it would not undo there.
        var pty = StartShell(shell);
        var at = WaitFor(Marker("agterm-readline=1;paste=shift-insert"));

        pty.WriteText("'undo-'+12345");
        pty.WriteText("\u001b[90;44;122;1;8;1_\u001b[90;44;122;0;8;1_");
        pty.WriteText(";'end-'+'of-test'\r");

        // The typed line shows 'undo-'+1234, only its result shows undo-1234. ConPTY can render
        // the output after the next prompt's marker, so the second output line ends the wait.
        at = WaitFor(Marker("agterm-readline=0"), at);
        var end = WaitFor("end-of-test", at);
        var result = Snapshot()[at..end];
        Assert.Contains("undo-1234", result);
        Assert.DoesNotContain("undo-12345", result);

        pty.WriteText("[Environment]::Exit(0)\r");
        Assert.Equal(0, await pty.Exited.WaitAsync(ChildBudget));
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public async Task The_wrapper_passes_the_last_status_to_the_original_readline(string shell)
    {
        // A stub stands in for PSReadLine's PSConsoleHostReadLine: it reports the $? it sees and
        // returns the next line of a fixed list. No keys are sent.
        var pty = StartShell(shell,
            "$global:agtLines = @(" +
            "'Get-Item C:\\agterm-missing-readline-test -ErrorAction SilentlyContinue', " +
            "'1', " +
            "'[Console]::Write(''errors='' + $Error.Count + '';''); [Environment]::Exit(0)'); " +
            "$global:agtNext = 0; " +
            "function global:PSConsoleHostReadLine { $s = $?; " +
            "[Console]::Write('status=' + $s + ';'); " +
            "$global:agtLines[$global:agtNext++] }; ");

        Assert.Equal(0, await pty.Exited.WaitAsync(ChildBudget));
        var text = Snapshot();

        // the stub ran inside the wrapper
        Assert.Matches(@"\x1b\]7777;agterm-readline=1", text);
        // False after the failing command, True after `1`
        Assert.Matches(@"status=False;[\s\S]*status=True;", text);
        // only Get-Item's error: the wrapper's own Write-Error is not recorded
        Assert.Contains("errors=1;", text);
    }
}
