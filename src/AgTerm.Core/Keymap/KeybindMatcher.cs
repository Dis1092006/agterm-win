namespace AgTerm.Core.Keymap;

/// <summary>The outcome kind of feeding one chord to a <see cref="KeybindMatcher"/>.</summary>
public enum MatchResultKind
{
    /// <summary>The pending prefix plus this chord exactly matches a bound keybind; the matcher has reset.</summary>
    Fired,
    /// <summary>The pending prefix plus this chord is a strict prefix of some longer bind; the
    /// matcher is now waiting for the next chord (the leader is armed).</summary>
    Armed,
    /// <summary>No bind starts with the pending prefix plus this chord; the matcher has reset.</summary>
    Unmatched,
}

/// <summary>The outcome of feeding one chord: the kind plus the fired bind's id (Fired only).</summary>
public readonly record struct MatchResult(MatchResultKind Kind, Guid Id = default)
{
    public static MatchResult Fired(Guid id) => new(MatchResultKind.Fired, id);
    public static readonly MatchResult Armed = new(MatchResultKind.Armed);
    public static readonly MatchResult Unmatched = new(MatchResultKind.Unmatched);
}

/// <summary>
/// The leader/sequence state machine that turns a stream of chords into command fires. Built from a
/// list of (keybind, id) pairs, it holds the chords typed so far as a pending prefix. Deadline-free:
/// the leader timeout that abandons a half-typed sequence is driven host-side via Reset() (the same
/// call Esc makes). No timers, no UI.
/// </summary>
public sealed class KeybindMatcher
{
    private readonly List<(Chord[] Keybind, Guid Id)> _binds;
    private List<Chord> _pending = new();

    public KeybindMatcher(IEnumerable<(Chord[] Keybind, Guid Id)> binds)
    {
        _binds = binds.ToList();
    }

    /// <summary>Whether a sequence is partway through (a leader is armed) — gates the host's timeout
    /// timer and on-screen hint.</summary>
    public bool IsArmed => _pending.Count > 0;

    /// <summary>
    /// Feed one chord. An exact match fires (and resets); a strict prefix arms (keeps the pending
    /// prefix); anything else is unmatched (and resets). When armed, a chord that completes no bind
    /// resets so the caller can pass it through to the terminal — UNLESS that chord is itself a
    /// fresh leader, in which case the matcher re-arms on it (re-pressing a leader mid-sequence
    /// restarts the sequence rather than abandoning it).
    /// </summary>
    public MatchResult Advance(Chord chord)
    {
        var candidate = _pending.Append(chord).ToArray();

        foreach (var bind in _binds)
        {
            if (bind.Keybind.SequenceEqual(candidate))
            {
                _pending = new List<Chord>();
                return MatchResult.Fired(bind.Id);
            }
        }

        if (_binds.Any(b => IsStrictPrefix(candidate, b.Keybind)))
        {
            _pending = candidate.ToList();
            return MatchResult.Armed;
        }

        // the extended prefix matches nothing; if the chord on its own is a fresh leader (or an
        // exact single-chord bind), restart from it instead of dropping the press.
        if (IsArmed)
        {
            _pending = new List<Chord>();
            return Advance(chord);
        }

        _pending = new List<Chord>();
        return MatchResult.Unmatched;
    }

    /// <summary>Clear the pending prefix (Esc or the host-side leader timeout).</summary>
    public void Reset() => _pending = new List<Chord>();

    private static bool IsStrictPrefix(Chord[] prefix, Chord[] keybind) =>
        prefix.Length < keybind.Length && keybind.Take(prefix.Length).SequenceEqual(prefix);
}
