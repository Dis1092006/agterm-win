namespace AgTerm.Core.Protocol;

/// <summary>
/// The outcome of resolving a control target string against a candidate id set. Value-equatable,
/// including the ambiguous hit list, so tests and callers can compare outcomes directly.
/// </summary>
public sealed class TargetResolution : IEquatable<TargetResolution>
{
    public enum Outcome { Resolved, NotFound, Ambiguous }

    private static readonly TargetResolution NotFoundInstance = new(Outcome.NotFound, default, Array.Empty<Guid>());

    public Outcome Kind { get; }

    /// <summary>The resolved id; only meaningful when Kind is Resolved.</summary>
    public Guid Id { get; }

    /// <summary>The matching ids in candidate order; only meaningful when Kind is Ambiguous.</summary>
    public IReadOnlyList<Guid> Hits { get; }

    private TargetResolution(Outcome kind, Guid id, IReadOnlyList<Guid> hits)
    {
        Kind = kind;
        Id = id;
        Hits = hits;
    }

    public static TargetResolution Resolved(Guid id) => new(Outcome.Resolved, id, Array.Empty<Guid>());

    public static TargetResolution NotFound() => NotFoundInstance;

    public static TargetResolution Ambiguous(IReadOnlyList<Guid> hits) => new(Outcome.Ambiguous, default, hits);

    public bool Equals(TargetResolution? other) =>
        other is not null && Kind == other.Kind && Id == other.Id && Hits.SequenceEqual(other.Hits);

    public override bool Equals(object? obj) => Equals(obj as TargetResolution);

    public override int GetHashCode() => HashCode.Combine(Kind, Id, Hits.Count);

    public override string ToString() => Kind switch
    {
        Outcome.Resolved => $"Resolved({Id})",
        Outcome.Ambiguous => $"Ambiguous({string.Join(", ", Hits)})",
        _ => "NotFound",
    };
}

/// <summary>
/// Pure resolvers shared by the control server and the CLI so the wire contract cannot drift:
/// turning a target string into an id, and deriving the control-channel path from the state directory.
/// </summary>
public static class ControlResolve
{
    /// <summary>
    /// Resolve a target string against a candidate id set. Candidates are session ids (for session
    /// commands) or workspace ids (for workspace commands); active is the currently selected session /
    /// current workspace. Matching order:
    /// empty string → NotFound (an empty prefix would otherwise match everything);
    /// "active" → the active id, or NotFound when null;
    /// exact uuid string (case-insensitive) → Resolved;
    /// otherwise a prefix match on the lowercased uuid string: 1 hit → Resolved, 0 → NotFound,
    /// ≥2 → Ambiguous(hits).
    /// </summary>
    public static TargetResolution Resolve(string target, IReadOnlyList<Guid> candidates, Guid? active)
    {
        if (target.Length == 0) return TargetResolution.NotFound();

        if (target == "active")
            return active is { } a ? TargetResolution.Resolved(a) : TargetResolution.NotFound();

        var needle = target.ToLowerInvariant();
        foreach (var candidate in candidates)
        {
            if (UuidString(candidate).ToLowerInvariant() == needle)
                return TargetResolution.Resolved(candidate);
        }

        var hits = candidates.Where(c => UuidString(c).ToLowerInvariant().StartsWith(needle, StringComparison.Ordinal)).ToArray();
        return hits.Length switch
        {
            0 => TargetResolution.NotFound(),
            1 => TargetResolution.Resolved(hits[0]),
            _ => TargetResolution.Ambiguous(hits),
        };
    }

    /// <summary>The canonical not-found control error for a target resolution miss.</summary>
    public static string NotFoundMessage(string noun, string target) => $"no such {noun}: {target}";

    /// <summary>The canonical ambiguous-prefix control error, listing matching ids by their first
    /// 8 characters (of the canonical uppercase uuid string).</summary>
    public static string AmbiguousMessage(string noun, string target, IReadOnlyList<Guid> hits)
    {
        var listed = string.Join(", ", hits.Select(h => UuidString(h)[..8]));
        return $"ambiguous {noun} prefix '{target}' → {listed}";
    }

    /// <summary>
    /// The canonical control error for an unresolved target. Resolved maps to not-found so callers
    /// that resolve an id but cannot find its owner keep the same wire contract as a normal miss.
    /// </summary>
    public static string ErrorMessage(string noun, string target, TargetResolution resolution) =>
        resolution.Kind == TargetResolution.Outcome.Ambiguous
            ? AmbiguousMessage(noun, target, resolution.Hits)
            : NotFoundMessage(noun, target);

    /// <summary>
    /// Derive the control channel path: (stateDir ?? appSupport) + "agterm.sock". The app and the
    /// CLI both call this with the same inputs, so they always rendezvous on the same path. The
    /// Windows transport is a named pipe, but the on-disk rendezvous derivation is kept 1:1.
    /// </summary>
    public static string SocketPath(string? stateDir, string appSupport) =>
        Path.Combine(stateDir ?? appSupport, "agterm.sock");

    /// <summary>The canonical uppercase UUID string, matching Swift Foundation's uuidString.</summary>
    private static string UuidString(Guid id) => id.ToString("D").ToUpperInvariant();
}
