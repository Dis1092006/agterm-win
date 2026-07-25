namespace AgTerm.Core.Keymap;

/// <summary>
/// The parsed keymap: the built-in actions the user rebound (single-chord overrides), plus the
/// custom commands defined in keymap.conf (each with a shortcut that may be empty — palette-only —
/// or a leader sequence).
/// </summary>
public sealed class Keymap
{
    public Keymap(IReadOnlyDictionary<BuiltinAction, Chord> builtinOverrides, IReadOnlyList<CustomCommand> commands)
    {
        BuiltinOverrides = builtinOverrides;
        Commands = commands;
    }

    public IReadOnlyDictionary<BuiltinAction, Chord> BuiltinOverrides { get; }

    public IReadOnlyList<CustomCommand> Commands { get; }

    public static Keymap Empty { get; } =
        new(new Dictionary<BuiltinAction, Chord>(), Array.Empty<CustomCommand>());

    /// <summary>The active chord for a built-in action: the user override when one is present, else
    /// the action's shipped default (null only for the keyless actions).</summary>
    public Chord? EquivalentFor(BuiltinAction action) =>
        BuiltinOverrides.TryGetValue(action, out var chord) ? chord : action.DefaultChord();

    /// <summary>The action's current shortcut as a menu glyph string, or null when the action has no
    /// shortcut at all.</summary>
    public string? GlyphHint(BuiltinAction action) => EquivalentFor(action)?.GlyphString;
}

/// <summary>A single problem found while parsing keymap.conf. Line is 1-based; 0 is reserved for a
/// whole-file or cross-section diagnostic that doesn't belong to a single line.</summary>
public readonly record struct KeymapDiagnostic(int Line, string Message);

/// <summary>
/// Host-free loader for the user keymap file. A missing file recovers as an empty keymap with no
/// diagnostics; an existing unreadable file recovers with a single line-0 diagnostic.
/// </summary>
public sealed class KeymapStore
{
    private readonly string _configDirectory;

    public KeymapStore(string configDirectory)
    {
        _configDirectory = configDirectory;
    }

    public string Path => System.IO.Path.Combine(_configDirectory, "keymap.conf");

    public (Keymap Keymap, List<KeymapDiagnostic> Diagnostics) Load()
    {
        try
        {
            return KeymapParser.Parse(File.ReadAllText(Path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!File.Exists(Path))
                return (Keymap.Empty, new List<KeymapDiagnostic>());
            return (Keymap.Empty, new List<KeymapDiagnostic>
            {
                new(0, $"could not read keymap.conf: {e.Message}"),
            });
        }
    }
}
