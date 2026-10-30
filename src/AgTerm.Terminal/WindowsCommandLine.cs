using System.Text;

namespace AgTerm.Terminal;

/// <summary>
/// Builds a CreateProcessW command line from argv, quoting as the exact inverse of the
/// CommandLineToArgvW / MSVCRT parsing rules, so the §6 --command argv contract (exec argv[0]
/// directly, no shell interpretation) survives Windows' single-string process spawn.
/// </summary>
public static class WindowsCommandLine
{
    public static string FromArgv(IReadOnlyList<string> argv)
    {
        if (argv.Count == 0)
            throw new ArgumentException("argv must contain at least the program to run", nameof(argv));
        var line = new StringBuilder();
        for (var i = 0; i < argv.Count; i++)
        {
            if (i > 0) line.Append(' ');
            AppendQuoted(line, argv[i]);
        }
        return line.ToString();
    }

    private const string InertPunctuation = " _-./\\:+~";

    /// <summary>The CreateProcessW line that runs <paramref name="command"/> in cmd.exe unchanged.
    /// cmd takes everything after /c as raw text and does not read \" as a quote, so FromArgv cannot
    /// carry a command with quotes. /s makes cmd strip exactly the outer pair added here.</summary>
    public static string ForCmdShell(string comspec, string command) =>
        FromArgv([comspec]) + " /s /c \"" + command + "\"";

    /// <summary>The child that <see cref="ForCmdShell"/> starts, as argv after cmd's own parsing.</summary>
    public static IReadOnlyList<string> CmdShellArgv(string comspec, string command) =>
        [comspec, "/s", "/c", command];

    /// <summary>FromArgv quotes for CreateProcess only. A line typed into pwsh or cmd is parsed by that
    /// shell as well, and pwsh treats more than the ASCII punctuation as syntax (curly quotes close a
    /// string), so an allowlist is the only safe form: letters, digits and a few path characters.</summary>
    public static bool IsInertForShells(IReadOnlyList<string> argv) =>
        argv.All(arg => arg.All(c => char.IsLetterOrDigit(c) || InertPunctuation.Contains(c)));

    private static void AppendQuoted(StringBuilder line, string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
        {
            line.Append(arg);
            return;
        }
        line.Append('"');
        var backslashes = 0;
        foreach (var ch in arg)
        {
            if (ch == '\\')
            {
                backslashes++;
                continue;
            }
            if (ch == '"')
            {
                // backslashes before a quote are doubled, plus one to escape the quote itself
                line.Append('\\', backslashes * 2 + 1);
                backslashes = 0;
                line.Append('"');
                continue;
            }
            line.Append('\\', backslashes);
            backslashes = 0;
            line.Append(ch);
        }
        // backslashes before the closing quote double so the quote stays a delimiter
        line.Append('\\', backslashes * 2);
        line.Append('"');
    }
}
