using AgTerm.Core.Keymap;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Hooks;

/// <summary>What makes two hooks.conf lines the same hook: the event kind and the shell line, outer
/// whitespace trimmed. The line number is not part of it, so inserting a comment above a hook keeps
/// its running child and queue across a reload.</summary>
public readonly record struct HookIdentity(ControlEventKind Kind, string Command);

/// <summary>One <c>on &lt;kind&gt; &lt;shell...&gt;</c> line. Line is display data for diagnostics and
/// hooks.list.</summary>
public readonly record struct HookEntry(HookIdentity Identity, int Line)
{
    public ControlEventKind Kind => Identity.Kind;

    public string Command => Identity.Command;
}

/// <summary>The parsed hooks.conf, entries in file order.</summary>
public sealed record HooksConfig(IReadOnlyList<HookEntry> Entries)
{
    public static HooksConfig Empty { get; } = new(Array.Empty<HookEntry>());
}

/// <summary>Parses hooks.conf. Blank lines and whole-line <c>#</c> comments are skipped; everything
/// after the kind is the shell line verbatim, so an inline <c>#</c>, quotes, pipes and variables reach
/// the shell untouched. A malformed line or a duplicate kind+command is diagnosed and skipped without
/// stopping later lines. Never throws.</summary>
public static class HooksParser
{
    public static (HooksConfig Hooks, List<KeymapDiagnostic> Diagnostics) Parse(string text)
    {
        var entries = new List<HookEntry>();
        var seen = new HashSet<HookIdentity>();
        var diagnostics = new List<KeymapDiagnostic>();

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index].Trim(' ', '\t');
            if (line.Length == 0 || line[0] == '#') continue;

            var verb = LeadingToken(line);
            if (verb != "on")
            {
                diagnostics.Add(new KeymapDiagnostic(lineNumber, $"unknown verb '{verb}'"));
                continue;
            }
            var rest = line[verb.Length..].Trim(' ', '\t');
            var rawKind = LeadingToken(rest);
            if (rawKind.Length == 0)
            {
                diagnostics.Add(new KeymapDiagnostic(lineNumber, "on requires an event kind"));
                continue;
            }
            if (ControlEventKinds.FromWire(rawKind) is not { } kind)
            {
                diagnostics.Add(new KeymapDiagnostic(lineNumber, $"unknown event kind '{rawKind}'"));
                continue;
            }
            var command = rest[rawKind.Length..].Trim(' ', '\t');
            if (command.Length == 0)
            {
                diagnostics.Add(new KeymapDiagnostic(lineNumber, $"hook for '{rawKind}' has no shell line"));
                continue;
            }
            var identity = new HookIdentity(kind, command);
            if (!seen.Add(identity))
            {
                diagnostics.Add(new KeymapDiagnostic(lineNumber,
                    $"hook 'on {rawKind} {command}' is already defined; hook skipped"));
                continue;
            }
            entries.Add(new HookEntry(identity, lineNumber));
        }
        return (new HooksConfig(entries), diagnostics);
    }

    private static string LeadingToken(string text)
    {
        var end = 0;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        return text[..end];
    }
}

/// <summary>Host-free loader for hooks.conf, beside keymap.conf. A missing file is an empty config
/// with no diagnostics; an existing unreadable one is empty with a single line-0 diagnostic, so a
/// reload cannot silently retire every hook.</summary>
public sealed class HooksStore
{
    private readonly string _configDirectory;

    public HooksStore(string configDirectory)
    {
        _configDirectory = configDirectory;
    }

    public string Path => System.IO.Path.Combine(_configDirectory, "hooks.conf");

    public (HooksConfig Hooks, List<KeymapDiagnostic> Diagnostics) Load()
    {
        try
        {
            var strictUtf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);
            using var reader = new StreamReader(Path, strictUtf8, detectEncodingFromByteOrderMarks: false);
            return HooksParser.Parse(reader.ReadToEnd());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or System.Text.DecoderFallbackException)
        {
            if (!File.Exists(Path))
                return (HooksConfig.Empty, new List<KeymapDiagnostic>());
            return (HooksConfig.Empty, new List<KeymapDiagnostic>
            {
                new(0, $"could not read hooks.conf: {e.Message}"),
            });
        }
    }

    /// <summary>Writes the commented starter file (and its directory) when none exists. Returns true
    /// when it wrote. The Edit Hooks entry point: the file can be missing after a config-directory
    /// change or a manual delete.</summary>
    public bool EnsureStarter()
    {
        if (File.Exists(Path)) return false;
        try
        {
            Directory.CreateDirectory(_configDirectory);
            File.WriteAllText(Path, HooksStarter.Text(), new System.Text.UTF8Encoding(false));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>The commented starter hooks.conf. Every line is a comment, so a fresh file runs nothing.</summary>
public static class HooksStarter
{
    public static string Text()
    {
        var kinds = string.Join(", ", ControlEventKinds.All.Select(k => k.ToWire()));
        return $"""
            # agterm hooks - run a shell line when a control event fires. Edit this file and run
            # Reload Hooks from the command palette (or `agtermctl hooks reload`) to apply. Blank lines and
            # lines starting with `#` are ignored.
            #
            #   on <kind> <shell...>
            #
            # Kinds: {kinds}.
            # Several lines per kind are allowed and run independently; an identical kind+command line is
            # skipped with a diagnostic. Everything after the kind is the shell line, passed to
            # `{HookShell.DisplayName} {HookShell.CommandSwitch}` untouched, detached with no terminal, in the app's
            # working directory, one process per line at a time. Further events for a busy line queue in
            # order (256 pending, oldest dropped).
            #
            # The script gets the event as one JSON object on stdin, the shape `agtermctl events --json`
            # prints, and these variables: AGT_EVENT_KIND, AGT_EVENT_STATUS, AGT_EVENT_HOST, AGT_SESSION_ID,
            # AGT_WORKSPACE_ID, AGT_WINDOW_ID, AGT_SOCKET. Pass `--socket "%AGT_SOCKET%"` to any agtermctl
            # call. A hook whose command emits another event of its own kind triggers itself again; the
            # queue bounds concurrency, it does not detect loops.
            #
            # Examples:
            #
            #   on status           %USERPROFILE%\.config\agterm\hooks\status.cmd
            #   on session.closed   echo %TIME% closed %AGT_SESSION_ID% >> "%TEMP%\agterm-sessions.log"
            #   on pane.scratch     %USERPROFILE%\.config\agterm\hooks\scratch-layout.cmd
            #
            # Uncomment and edit a line below to start.
            # on status if "%AGT_EVENT_STATUS%"=="blocked" powershell -NoProfile -c "[console]::beep(880,300)"

            """;
    }
}

/// <summary>The shell a hook line runs in on Windows: cmd.exe, the same interpreter a console
/// "run this line" uses, so %VAR%, pipes and redirections behave as users of cmd expect.</summary>
public static class HookShell
{
    public const string DisplayName = "cmd.exe";

    public const string CommandSwitch = "/d /s /c";
}
