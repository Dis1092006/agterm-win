namespace AgTerm.Core.Model;

/// <summary>
/// A bounded most-recent-first list of unique ids. Push moves an id to the front (no duplicates);
/// the front is the most recently used. Backs the Ctrl-Tab session switcher's order — Items[0] is
/// the current session, Items[1] the previous one.
/// </summary>
public sealed class RecencyStack<TId> : IEquatable<RecencyStack<TId>> where TId : notnull
{
    private readonly List<TId> _items;

    public int Limit { get; }

    /// <summary>Seeds the list, keeping the first occurrence of each id (the list is unique by
    /// contract, but a persisted/hand-edited seed may not be) and truncating to the limit.</summary>
    public RecencyStack(int limit = 100, IEnumerable<TId>? items = null)
    {
        Limit = limit;
        var seen = new HashSet<TId>();
        _items = (items ?? Enumerable.Empty<TId>()).Where(seen.Add).Take(limit).ToList();
    }

    public IReadOnlyList<TId> Items => _items;

    /// <summary>Move id to the front. No-op if it's already there.</summary>
    public void Push(TId id)
    {
        if (_items.Count > 0 && EqualityComparer<TId>.Default.Equals(_items[0], id)) return;
        _items.RemoveAll(x => EqualityComparer<TId>.Default.Equals(x, id));
        _items.Insert(0, id);
        if (_items.Count > Limit) _items.RemoveRange(Limit, _items.Count - Limit);
    }

    public void Remove(TId id) => _items.RemoveAll(x => EqualityComparer<TId>.Default.Equals(x, id));

    /// <summary>Up to n most-recent ids that are still in valid, most-recent first. Stale ids (not
    /// in valid) are skipped, so a closed session never appears even before it's removed.</summary>
    public List<TId> Top(int n, IReadOnlySet<TId> valid)
    {
        var result = new List<TId>();
        foreach (var id in _items)
        {
            if (!valid.Contains(id)) continue;
            result.Add(id);
            if (result.Count == n) break;
        }
        return result;
    }

    public bool IsEmpty => _items.Count == 0;

    public bool Equals(RecencyStack<TId>? other) =>
        other is not null && Limit == other.Limit && _items.SequenceEqual(other._items);

    public override bool Equals(object? obj) => Equals(obj as RecencyStack<TId>);

    public override int GetHashCode() => HashCode.Combine(Limit, _items.Count);
}
