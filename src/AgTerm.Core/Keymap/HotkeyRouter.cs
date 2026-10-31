namespace AgTerm.Core.Keymap;

public sealed record CustomHotkeyBinding(
    Guid CommandId,
    IReadOnlyList<PhysicalChord> Chords);

public sealed record HotkeyManifest(
    long Generation,
    int LeaderTimeoutMs,
    IReadOnlyList<PhysicalChord> BuiltinSingles,
    IReadOnlyList<CustomHotkeyBinding> CustomBindings);

public sealed record CustomHotkeyEvent(
    string PaneId,
    Guid CommandId,
    long Generation,
    PhysicalChord Chord,
    bool Dashboard = false);

public sealed record BuiltinHotkeyEvent(
    string PaneId,
    long Generation,
    PhysicalChord Chord);

/// <summary>
/// Routes physical Windows presses to BuiltinAction fires: the active keymap's builtin binds
/// (override else default — always SINGLE chords; §9 rejects leaders on map lines) matched against
/// the press's candidate interpretations in WindowsChordDecoder priority order. It also projects
/// parsed custom-command bindings into an immutable page manifest; execution remains host-owned.
/// Host-free; the window's KeyDown and the terminal page's interception both feed this.
/// </summary>
public sealed class HotkeyRouter
{
    private readonly Dictionary<Chord, BuiltinAction> _binds = new();
    private readonly IReadOnlyList<CustomCommand> _commands;

    public HotkeyRouter(Keymap keymap)
    {
        ArgumentNullException.ThrowIfNull(keymap);
        _commands = keymap.Commands;
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
        _binds.Keys.SelectMany(WindowsChordDecoder.Presses).Distinct().ToList();

    public HotkeyManifest CreateManifest(
        long generation,
        Func<BuiltinAction, bool> canExecute,
        int leaderTimeoutMs = 1500)
    {
        ArgumentNullException.ThrowIfNull(canExecute);
        if (generation < 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        if (leaderTimeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(leaderTimeoutMs));

        var executable = _binds.Where(pair => canExecute(pair.Value)).Select(pair => pair.Key).ToArray();
        var builtinSet = executable.Select(WindowsChordDecoder.Effective).ToHashSet();
        var customBindings = new List<CustomHotkeyBinding>();

        foreach (var command in _commands)
        {
            if (string.IsNullOrWhiteSpace(command.Shortcut) ||
                Keybinds.Parse(command.Shortcut) is not { } parsed)
            {
                continue;
            }

            var physical = parsed.Select(WindowsChordDecoder.Effective).ToArray();
            if (builtinSet.Contains(physical[0]) ||
                customBindings.Any(binding => PrefixesOverlap(binding.Chords, physical)))
            {
                continue;
            }

            customBindings.Add(new CustomHotkeyBinding(command.Id, physical));
        }

        // a press a chord only stands for (plus on the equals key) never takes one a command names
        var customFirsts = customBindings.Select(binding => binding.Chords[0]).ToHashSet();
        var builtinSingles = builtinSet
            .Concat(executable
                .SelectMany(WindowsChordDecoder.Presses)
                .Where(press => !customFirsts.Contains(press)))
            .Distinct()
            .ToArray();

        return new HotkeyManifest(
            generation,
            leaderTimeoutMs,
            builtinSingles,
            customBindings.ToArray());
    }

    private static bool PrefixesOverlap(
        IReadOnlyList<PhysicalChord> first,
        IReadOnlyList<PhysicalChord> second)
    {
        var commonLength = Math.Min(first.Count, second.Count);
        for (var index = 0; index < commonLength; index++)
        {
            if (first[index] != second[index])
                return false;
        }

        return true;
    }
}
