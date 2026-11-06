namespace AgTerm.Core.Model;

// Port of AppStore+FlaggedOrder.swift (upstream 8088e146)

/// <summary>Which side of the anchor a flagged-order move puts the moved block.</summary>
public enum FlaggedOrderPlacement
{
    Before,
    After,
}

public sealed partial class AppStore
{
    /// <summary>The flagged working set's saved order: the flat and plain layouts' row order.
    /// Persisted. A session parked in a pending close keeps its slot so an undo returns it in place.</summary>
    public IReadOnlyList<Guid> FlaggedOrder => _flaggedOrder;

    private List<Guid> _flaggedOrder = [];

    /// <summary>Every flagged session in tree order: the flagged tree's rows and what membership
    /// readers count.</summary>
    public List<Session> FlaggedSessions =>
        Workspaces.SelectMany(w => w.Sessions).Where(s => s.Flagged).ToList();

    /// <summary>The flat and plain layouts' rows: saved ids still flagged and in the tree, then any
    /// flagged session the order lacks, in tree order.</summary>
    public List<Session> OrderedFlaggedSessions
    {
        get
        {
            var flagged = FlaggedSessions;
            var unplaced = flagged.ToDictionary(s => s.Id);
            var saved = new List<Session>();
            foreach (var id in _flaggedOrder)
            {
                if (unplaced.Remove(id, out var session)) saved.Add(session);
            }
            saved.AddRange(flagged.Where(s => unplaced.ContainsKey(s.Id)));
            return saved;
        }
    }

    /// <summary>Puts ids, kept in their current order, just before or after anchor. Every id and the
    /// anchor must be flagged sessions of this store and the anchor not among the ids; an invalid or
    /// unchanged move returns false. Ids parked in a pending close keep their slots. Saves and signals
    /// a tree change only on a real move.</summary>
    public bool MoveFlagged(IReadOnlyList<Guid> ids, FlaggedOrderPlacement placement, Guid anchor)
    {
        var live = OrderedFlaggedSessions.Select(s => s.Id).ToList();
        var moving = ids.ToHashSet();
        if (moving.Count == 0 || moving.Contains(anchor) || !moving.IsSubsetOf(live)) return false;
        if (SidebarDrop.ReorderedFlagged(live, moving, placement, anchor) is not { } reordered) return false;
        _flaggedOrder = WritingLiveOrder(reordered);
        ScheduleTreeChanged();
        Save();
        return true;
    }

    internal string[]? FlaggedOrderReadback
    {
        get
        {
            var ids = OrderedFlaggedSessions.Select(s => s.Id.ToString("D").ToUpperInvariant()).ToArray();
            return ids.Length == 0 ? null : ids;
        }
    }

    // recorded when the session arrives rather than derived from the tree later, so a session placed
    // earlier in the tree by a later arrival cannot jump ahead of one that arrived before it
    internal void AppendFlaggedArrivals()
    {
        var known = _flaggedOrder.ToHashSet();
        _flaggedOrder.AddRange(FlaggedSessions.Select(s => s.Id).Where(id => !known.Contains(id)));
    }

    internal void DropFromFlaggedOrder(IEnumerable<Guid> ids)
    {
        var gone = ids.ToHashSet();
        _flaggedOrder.RemoveAll(gone.Contains);
    }

    // a snapshot written before the order existed carries none and restores in tree order
    private void RestoreFlaggedOrder(IEnumerable<Guid>? saved)
    {
        var flagged = FlaggedSessions.Select(s => s.Id).ToHashSet();
        var seen = new HashSet<Guid>();
        _flaggedOrder = (saved ?? []).Where(id => flagged.Contains(id) && seen.Add(id)).ToList();
        AppendFlaggedArrivals();
    }

    // parked ids keep their slots so a pending-close undo returns them in place
    private List<Guid> WritingLiveOrder(List<Guid> liveOrder)
    {
        var live = liveOrder.ToHashSet();
        var next = 0;
        var written = new List<Guid>();
        foreach (var id in _flaggedOrder)
        {
            written.Add(live.Contains(id) ? liveOrder[next++] : id);
        }
        written.AddRange(liveOrder.Skip(next));
        return written;
    }
}
