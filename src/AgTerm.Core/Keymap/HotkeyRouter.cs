namespace AgTerm.Core.Keymap;

/// <summary>
/// Routes physical Windows presses to BuiltinAction fires: the active keymap's builtin binds
/// (override else default — always SINGLE chords; §9 rejects leaders on map lines) matched against
/// the press's candidate interpretations in WindowsChordDecoder priority order. Custom-command
/// chords are a phase-5 concern (they exec shell lines) and are neither fired nor intercepted
/// here. Host-free; the window's KeyDown and the terminal page's interception both feed this.
/// </summary>
public sealed class HotkeyRouter
{
    private readonly Dictionary<Chord, BuiltinAction> _binds = new();

    public HotkeyRouter(Keymap keymap)
    {
        foreach (var action in BuiltinActions.All)
        {
            if (keymap.EquivalentFor(action) is { } chord) _binds[chord] = action;
        }
    }

    /// <summary>The action a press fires, or null when it belongs to the terminal/text field. The
    /// first candidate interpretation with a bind wins (the cmd-first collision rule).</summary>
    public BuiltinAction? Handle(PhysicalChord physical)
    {
        foreach (var candidate in WindowsChordDecoder.Candidates(physical))
        {
            if (_binds.TryGetValue(candidate, out var action)) return action;
        }
        return null;
    }

    /// <summary>The distinct physical chords the terminal page must intercept (keydown must NOT
    /// reach the shell, it must come back as a chord message). cmd/ctrl folds dedupe here.</summary>
    public IReadOnlyList<PhysicalChord> InterceptChords =>
        _binds.Keys.Select(WindowsChordDecoder.Effective).Distinct().ToList();
}
