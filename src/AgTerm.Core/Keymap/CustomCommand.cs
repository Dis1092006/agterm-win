namespace AgTerm.Core.Keymap;

/// <summary>
/// A user-defined command: a shell line, optionally bound to a keyboard shortcut and always listed
/// in the action palette. The command body may contain {AGT_X} template tokens (see
/// <see cref="CommandContext"/>), expanded at fire time; the same values are also exported as
/// $AGT_X environment variables on the spawned process. An empty Shortcut means palette-only.
/// </summary>
public sealed class CustomCommand
{
    public CustomCommand(string name, string command, string shortcut, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Name = name;
        Command = command;
        Shortcut = shortcut;
    }

    public Guid Id { get; }

    /// <summary>Display name, shown in the palette and used in the failure banner.</summary>
    public string Name { get; set; }

    /// <summary>The shell line, with {AGT_X} tokens expanded at fire time.</summary>
    public string Command { get; set; }

    /// <summary>The keybind string (e.g. cmd+shift+e or ctrl+a&gt;b); empty means palette-only.</summary>
    public string Shortcut { get; set; }
}

/// <summary>
/// The already-resolved session context for a command fire: one field per {AGT_X} token. The host
/// builds this from the active session at fire time; the core only turns it into the token
/// substitutions (Expand) and the environment dictionary (Environment). Both derive from the single
/// token table, so the {AGT_X} set and the $AGT_X set can never drift.
/// </summary>
public sealed record CommandContext
{
    /// <summary>Which pane a command fired from; the wire strings are exactly the --pane argument
    /// values (left = main, right = split).</summary>
    public enum Pane { Left, Right, Scratch }

    public string SessionId { get; init; } = "";
    public string SessionName { get; init; } = "";
    public string SessionPwd { get; init; } = "";
    public string WorkspaceId { get; init; } = "";
    public string WorkspaceName { get; init; } = "";
    public string WindowId { get; init; } = "";
    public string WindowName { get; init; } = "";

    /// <summary>The pane that had focus at fire time; Left for any single-pane session, including a
    /// promoted split survivor.</summary>
    public Pane FocusedPane { get; init; } = Pane.Left;

    public string Selection { get; init; } = "";
    public string Socket { get; init; } = "";

    private static string PaneWire(Pane pane) => pane switch
    {
        Pane.Left => "left",
        Pane.Right => "right",
        _ => "scratch",
    };

    /// <summary>The single source of truth mapping each AGT_X token name to its resolved value —
    /// Expand and Environment both iterate it, so the token set and the env-var set stay symmetric.</summary>
    private (string Name, string Value)[] Tokens => new[]
    {
        ("AGT_SESSION_ID", SessionId),
        ("AGT_SESSION_NAME", SessionName),
        ("AGT_SESSION_PWD", SessionPwd),
        ("AGT_WORKSPACE_ID", WorkspaceId),
        ("AGT_WORKSPACE_NAME", WorkspaceName),
        ("AGT_WINDOW_ID", WindowId),
        ("AGT_WINDOW_NAME", WindowName),
        ("AGT_PANE", PaneWire(FocusedPane)),
        ("AGT_SELECTION", Selection),
        ("AGT_SOCKET", Socket),
    };

    /// <summary>The AGT_X token names available in a command body, in declaration order.</summary>
    public static IReadOnlyList<string> TokenNames { get; } =
        new CommandContext().Tokens.Select(t => t.Name).ToArray();

    /// <summary>
    /// The token base names whose value comes from an active session/workspace/selection. In a
    /// session-free context each expands EMPTY, which is dangerous — an empty {AGT_SESSION_PWD}
    /// turns "rm -rf …/*" into a root glob. AGT_SOCKET/AGT_WINDOW/AGT_PANE are excluded: they
    /// resolve with no session.
    /// </summary>
    public static readonly IReadOnlyList<string> SessionScopedTokenBases =
        new[] { "AGT_SESSION", "AGT_WORKSPACE", "AGT_SELECTION" };

    /// <summary>Whether the command body references any session-scoped token (in {AGT_X}, $AGT_X, or
    /// ${AGT_X} form — a plain substring match, the base names are specific enough).</summary>
    public static bool ReferencesSessionScopedContext(string commandBody) =>
        SessionScopedTokenBases.Any(commandBody.Contains);

    /// <summary>
    /// Substitutes each {AGT_X} occurrence in the template with its resolved value. A token whose
    /// value is empty becomes an empty string; an unknown {...} is left untouched. Single-pass: a
    /// replaced value that itself contains an {AGT_X} literal is NOT re-substituted.
    /// </summary>
    public string Expand(string template)
    {
        var table = Tokens.ToDictionary(t => t.Name, t => t.Value, StringComparer.Ordinal);
        var result = new System.Text.StringBuilder();
        var rest = template.AsSpan();
        while (true)
        {
            var open = rest.IndexOf('{');
            if (open < 0) break;
            result.Append(rest[..open]);
            var afterOpen = rest[(open + 1)..];
            var close = afterOpen.IndexOf('}');
            if (close < 0)
            {
                // no closing brace — copy the rest verbatim and stop.
                result.Append(rest[open..]);
                return result.ToString();
            }
            var name = afterOpen[..close].ToString();
            if (table.TryGetValue(name, out var value))
                result.Append(value);
            else
                result.Append(rest.Slice(open, close + 2)); // keep the {...} literal untouched
            rest = afterOpen[(close + 1)..];
        }
        result.Append(rest);
        return result.ToString();
    }

    /// <summary>The AGT_X → value dictionary exported as environment on the spawned process; keys
    /// mirror exactly the tokens Expand substitutes.</summary>
    public IReadOnlyDictionary<string, string> Environment() =>
        Tokens.ToDictionary(t => t.Name, t => t.Value, StringComparer.Ordinal);
}
