namespace AgTerm.Core.Protocol;

/// <summary>An event before the ring assigns its app-run sequence and timestamp.</summary>
public sealed record ControlEventDraft
{
    public required ControlEventKind Kind { get; init; }
    public string? Window { get; init; }
    public string? Workspace { get; init; }
    public string? Session { get; init; }
    public ControlEventPayload Payload { get; init; } = new();
}

/// <summary>An independent consumer's position within one app run.</summary>
public readonly record struct ControlEventCursor(Guid Run, ulong After);

/// <summary>
/// A successful page (Error is null) or a loud cursor failure carrying the ring's current anchor
/// in Batch — present even on failure so the caller can decide whether to rebaseline.
/// </summary>
public sealed record ControlEventReadResult
{
    public required ControlEventBatch Batch { get; init; }
    public ControlEventReadError? Error { get; init; }
}

/// <summary>
/// Bounded, non-destructive event history for one app process: an app-run-scoped ring the control
/// server reads with cursor + kind filter. Append and read are internally serialized because the
/// live application shares one ring across independently gated windows; single-window hosts retain
/// the same sequence and cursor behavior.
/// </summary>
public sealed class ControlEventRing
{
    public const int DefaultCapacity = 4_096;

    private readonly int _capacity;
    private readonly Guid _runId;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private readonly List<ControlEvent> _entries = new();
    private ulong _currentSequence;

    public ControlEventRing(int capacity = DefaultCapacity, Guid? runId = null, Func<DateTimeOffset>? now = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "event ring capacity must be positive");
        _capacity = capacity;
        _runId = runId ?? Guid.NewGuid();
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Append one event, assigning the next sequence and current Unix timestamp.</summary>
    public ControlEvent Append(ControlEventDraft draft)
    {
        lock (_gate)
        {
            _currentSequence += 1;
            var evt = new ControlEvent
            {
                Seq = _currentSequence,
                Ts = _now().ToUnixTimeMilliseconds() / 1000.0,
                Kind = draft.Kind,
                Window = draft.Window,
                Workspace = draft.Workspace,
                Session = draft.Session,
                Payload = draft.Payload,
            };
            _entries.Add(evt);
            if (_entries.Count > _capacity)
                _entries.RemoveRange(0, _entries.Count - _capacity);
            return evt;
        }
    }

    /// <summary>
    /// Read retained entries after the cursor, optionally filtering by kind. A null cursor is the
    /// subscribe-from-now bootstrap: it anchors at the tail and intentionally returns no history.
    /// </summary>
    public ControlEventReadResult Read(ControlEventCursor? cursor,
        IReadOnlySet<ControlEventKind>? kinds = null, int limit = 100)
    {
        lock (_gate)
        {
            var anchor = new ControlEventBatch { Run = _runId, Next = _currentSequence, Items = Array.Empty<ControlEvent>() };
            if (cursor is not { } c)
                return new ControlEventReadResult { Batch = anchor };
            if (c.Run != _runId)
                return new ControlEventReadResult { Batch = anchor, Error = ControlEventReadError.RunChanged };
            if (c.After > _currentSequence)
                return new ControlEventReadResult { Batch = anchor, Error = ControlEventReadError.CursorAhead };
            if (_entries.Count > 0 && c.After < _entries[0].Seq - 1)
                return new ControlEventReadResult { Batch = anchor, Error = ControlEventReadError.CursorExpired };

            var items = new List<ControlEvent>();
            foreach (var evt in _entries)
            {
                if (evt.Seq <= c.After) continue;
                if (kinds is not null && !kinds.Contains(evt.Kind)) continue;
                items.Add(evt);
                if (items.Count == limit)
                    return new ControlEventReadResult
                    {
                        Batch = new ControlEventBatch { Run = _runId, Next = evt.Seq, Items = items.ToArray() },
                    };
            }
            return new ControlEventReadResult
            {
                Batch = new ControlEventBatch { Run = _runId, Next = _currentSequence, Items = items.ToArray() },
            };
        }
    }
}
