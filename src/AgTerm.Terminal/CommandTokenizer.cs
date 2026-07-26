using System.Text;

namespace AgTerm.Terminal;

/// <summary>
/// Tokenizes a session.new --command string into argv (§6: quote-aware split, argv[0] execed
/// directly, shell operators NOT interpreted). Upstream defers this to libghostty's config parser;
/// the Windows engine owns its own rules (PLATFORM-NOTES): whitespace splits, single quotes group
/// literally, double quotes group with \" and \\ escapes, a backslash OUTSIDE quotes is a plain
/// character (bare C:\paths must survive), an unterminated quote takes the rest literally.
/// </summary>
public static class CommandTokenizer
{
    public static IReadOnlyList<string> Tokenize(string command)
    {
        var argv = new List<string>();
        var token = new StringBuilder();
        var hasToken = false;
        var i = 0;
        while (i < command.Length)
        {
            var ch = command[i];
            if (ch is ' ' or '\t')
            {
                if (hasToken)
                {
                    argv.Add(token.ToString());
                    token.Clear();
                    hasToken = false;
                }
                i++;
                continue;
            }
            hasToken = true;
            switch (ch)
            {
                case '\'':
                    i++;
                    while (i < command.Length && command[i] != '\'') token.Append(command[i++]);
                    i++; // closing quote (or end for an unterminated one)
                    break;
                case '"':
                    i++;
                    while (i < command.Length && command[i] != '"')
                    {
                        if (command[i] == '\\' && i + 1 < command.Length &&
                            (command[i + 1] == '"' || command[i + 1] == '\\'))
                        {
                            i++;
                        }
                        token.Append(command[i++]);
                    }
                    i++;
                    break;
                default:
                    token.Append(ch);
                    i++;
                    break;
            }
        }
        if (hasToken) argv.Add(token.ToString());
        return argv;
    }
}
