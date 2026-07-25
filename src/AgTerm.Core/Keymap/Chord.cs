namespace AgTerm.Core.Keymap;

/// <summary>The keyboard modifier flags a chord may require, host-free. The parser recognizes the
/// modifier words ctrl/control, cmd/command, opt/option/alt, and shift.</summary>
[Flags]
public enum Modifiers
{
    None = 0,
    Control = 1 << 0,
    Command = 1 << 1,
    Option = 1 << 2,
    Shift = 1 << 3,
}

/// <summary>
/// A single key press: zero or more modifiers plus a base key (a lowercased single character or one
/// of the bindable named keys). Chords compose into a keybind sequence.
/// </summary>
public readonly record struct Chord(Modifiers Mods, string Key)
{
    /// <summary>The chord rendered back to kitty syntax (e.g. cmd+shift+e), the same form the user
    /// writes in keymap.conf. Modifiers are emitted in a fixed ctrl+cmd+opt+shift order so the
    /// round-trip is stable.</summary>
    public string DisplayString
    {
        get
        {
            var parts = new List<string>();
            if (Mods.HasFlag(Modifiers.Control)) parts.Add("ctrl");
            if (Mods.HasFlag(Modifiers.Command)) parts.Add("cmd");
            if (Mods.HasFlag(Modifiers.Option)) parts.Add("opt");
            if (Mods.HasFlag(Modifiers.Shift)) parts.Add("shift");
            parts.Add(Key);
            return string.Join("+", parts);
        }
    }

    /// <summary>The chord rendered as macOS menu glyphs (e.g. ⌘N, ⌃P): modifiers in the macOS order
    /// ⌃⌥⇧⌘, then the key (single letters uppercased, the named keys as their symbols). Kept for
    /// display parity with upstream palette hints.</summary>
    public string GlyphString
    {
        get
        {
            var s = "";
            if (Mods.HasFlag(Modifiers.Control)) s += "⌃";
            if (Mods.HasFlag(Modifiers.Option)) s += "⌥";
            if (Mods.HasFlag(Modifiers.Shift)) s += "⇧";
            if (Mods.HasFlag(Modifiers.Command)) s += "⌘";
            s += Key switch
            {
                "tab" => "⇥",
                "space" => "␣",
                "return" => "↩",
                "delete" => "⌫",
                "left" => "←",
                "right" => "→",
                "up" => "↑",
                "down" => "↓",
                _ => Key.Length == 1 ? Key.ToUpperInvariant() : Key,
            };
            return s;
        }
    }
}

/// <summary>
/// A pair of custom commands whose shortcuts conflict — either identical or one a sequence-prefix
/// of the other (the wait-or-fire ambiguity). Carries both ids so the caller can point at the offenders.
/// </summary>
public readonly record struct KeybindConflict(Guid First, Guid Second);

/// <summary>Keybind parsing and conflict analysis (a keybind is an ordered chord sequence: length 1
/// is a simple chord, longer is a leader sequence like ctrl+a &gt; b).</summary>
public static class Keybinds
{
    /// <summary>The four arrow keys — a subset of the named keys with one extra rule: a built-in map
    /// may not bind a modifier-less arrow (an always-on key-equivalent on a bare arrow would swallow
    /// the key in the terminal, the palettes, and every text field at once). Declared before
    /// BindableNamedKeys, whose initializer folds it in.</summary>
    public static readonly IReadOnlySet<string> BindableArrowKeys =
        new HashSet<string> { "left", "right", "up", "down" };

    /// <summary>The base keys a chord may name beyond a single printable character — exactly the
    /// named keys the app-side runner can produce, so the parser rejects any other name. esc is
    /// reserved as the leader abort and is intentionally NOT bindable.</summary>
    public static readonly IReadOnlySet<string> BindableNamedKeys =
        new HashSet<string>(new[] { "tab", "space", "return", "delete" }.Concat(BindableArrowKeys));

    /// <summary>
    /// Whether a chord is owned by the app's always-on key monitors (not a menu equivalent), so a
    /// keybind that starts with it would dead-race the monitor and must be rejected. Mirrors the
    /// monitors' real predicates: the MRU switcher consumes Tab whenever Control is held with ANY
    /// other modifiers; the pane shortcuts consume 1/2 only when Control is the SOLE modifier.
    /// </summary>
    public static bool IsReservedMonitorChord(Chord chord)
    {
        if (chord.Mods.HasFlag(Modifiers.Control) && chord.Key == "tab") return true;
        if (chord.Mods == Modifiers.Control && chord.Key is "1" or "2") return true;
        return false;
    }

    /// <summary>
    /// Parse a keybind string, or null when it is empty, malformed, or names an unknown modifier.
    /// The grammar is chords separated by '&gt;', each chord a '+'-joined list of modifier words and a
    /// final base key, case-insensitive. The base key is a single printable character or one of the
    /// bindable named keys; any other multi-char word (esc, f1) is rejected.
    /// </summary>
    public static Chord[]? Parse(string s)
    {
        var chordStrings = s.Split('>');
        var keybind = new Chord[chordStrings.Length];
        for (var i = 0; i < chordStrings.Length; i++)
        {
            if (ParseChord(chordStrings[i]) is not { } chord) return null;
            keybind[i] = chord;
        }
        return keybind;
    }

    private static Chord? ParseChord(string s)
    {
        var tokens = s.Split('+').Select(t => t.Trim().ToLowerInvariant()).ToArray();
        if (tokens.Length == 0 || tokens.Any(t => t.Length == 0)) return null;

        var mods = Modifiers.None;
        string? key = null;
        foreach (var token in tokens)
        {
            if (ModifierFor(token) is { } mod)
            {
                mods |= mod;
                continue;
            }
            // a non-modifier token is the base key; only one is allowed per chord.
            if (key is not null) return null;
            key = token;
        }

        // the base key must be a single character or a named key the runner can produce.
        if (key is null || (key.Length != 1 && !BindableNamedKeys.Contains(key))) return null;
        return new Chord(mods, key);
    }

    private static Modifiers? ModifierFor(string token) => token switch
    {
        "ctrl" or "control" => Modifiers.Control,
        "cmd" or "command" => Modifiers.Command,
        "opt" or "option" or "alt" => Modifiers.Option,
        "shift" => Modifiers.Shift,
        _ => null,
    };

    /// <summary>
    /// Find shortcut conflicts across a list of custom commands. Two parseable, non-empty shortcuts
    /// conflict when one keybind is a prefix of the other (a duplicate is the degenerate equal-length
    /// case): the shorter bind would fire — or be forced to wait — ambiguously while the longer one is
    /// still being typed. Commands with an empty or unparseable shortcut are skipped.
    /// </summary>
    public static IReadOnlyList<KeybindConflict> Conflicts(IReadOnlyList<CustomCommand> commands)
    {
        var parsed = new List<(Guid Id, Chord[] Keybind)>();
        foreach (var command in commands)
        {
            if (command.Shortcut.Length == 0 || Parse(command.Shortcut) is not { } keybind) continue;
            parsed.Add((command.Id, keybind));
        }

        var conflicts = new List<KeybindConflict>();
        for (var i = 0; i < parsed.Count; i++)
        for (var j = i + 1; j < parsed.Count; j++)
        {
            if (IsPrefix(parsed[i].Keybind, parsed[j].Keybind))
                conflicts.Add(new KeybindConflict(parsed[i].Id, parsed[j].Id));
        }
        return conflicts;
    }

    /// <summary>Whether one keybind is a prefix of another (equal length counts). Order-independent:
    /// sorts the two by length itself.</summary>
    private static bool IsPrefix(Chord[] a, Chord[] b)
    {
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        return longer.Take(shorter.Length).SequenceEqual(shorter);
    }
}
