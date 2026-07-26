namespace AgTerm.Core.Keymap;

/// <summary>A physical Windows key press: the three real modifier keys plus the base key, already
/// normalized to keymap key names (a lowercased single character, or one of tab/space/return/
/// delete/left/right/up/down).</summary>
public readonly record struct PhysicalChord(bool Ctrl, bool Alt, bool Shift, string Key);

/// <summary>
/// The Windows modifier mapping for the kitty-flavored keymap (spec §9 leaves "cmd → Win/Ctrl" to
/// the platform; PLATFORM-NOTES phase 4 documents this choice): keymap `cmd` AND `ctrl` both land
/// on the physical Ctrl key, `opt` on Alt, `shift` on Shift. Because two keymap modifiers share
/// one physical key, a press decodes into CANDIDATE chords in priority order — cmd first (most
/// upstream defaults are cmd chords), then ctrl, then the ctrl+cmd fold — and the first candidate
/// the KeybindMatcher recognizes wins. Consequence: where two active binds collapse onto the same
/// physical chord (⌘F toggle_search vs ⌃⌘F toggle_fullscreen → both Ctrl+F), the cmd
/// interpretation shadows the other; the table in PLATFORM-NOTES/README lists the collisions and
/// keymap.conf remaps around them.
/// </summary>
public static class WindowsChordDecoder
{
    /// <summary>The keymap chords a physical press may mean, most-preferred first. A press with
    /// neither Ctrl nor Alt is not a hotkey (bare/shifted keys belong to the terminal and text
    /// fields) and yields no candidates.</summary>
    public static IReadOnlyList<Chord> Candidates(PhysicalChord physical)
    {
        if (!physical.Ctrl && !physical.Alt) return Array.Empty<Chord>();

        var shift = physical.Shift ? Modifiers.Shift : Modifiers.None;
        var opt = physical.Alt ? Modifiers.Option : Modifiers.None;
        var candidates = new List<Chord>();
        if (physical.Ctrl)
        {
            candidates.Add(new Chord(Modifiers.Command | opt | shift, physical.Key));
            candidates.Add(new Chord(Modifiers.Control | opt | shift, physical.Key));
            candidates.Add(new Chord(Modifiers.Control | Modifiers.Command | opt | shift, physical.Key));
        }
        else
        {
            candidates.Add(new Chord(opt | shift, physical.Key));
        }
        return candidates;
    }

    /// <summary>The physical Windows chord a keymap chord lands on (the reverse direction, for the
    /// hotkey table, menu hints, and the in-terminal interception list). cmd and ctrl both fold
    /// onto Ctrl — equal results here ARE the documented collision surface.</summary>
    public static PhysicalChord Effective(Chord chord) => new(
        Ctrl: chord.Mods.HasFlag(Modifiers.Command) || chord.Mods.HasFlag(Modifiers.Control),
        Alt: chord.Mods.HasFlag(Modifiers.Option),
        Shift: chord.Mods.HasFlag(Modifiers.Shift),
        Key: chord.Key);
}
