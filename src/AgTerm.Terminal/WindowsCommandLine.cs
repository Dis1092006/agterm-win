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
