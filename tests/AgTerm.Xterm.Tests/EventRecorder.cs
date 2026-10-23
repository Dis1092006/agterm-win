using System.Collections;

namespace AgTerm.Xterm.Tests;

internal sealed class EventRecorder<T> : IReadOnlyList<T>
{
    private readonly List<T> _items = [];
    private readonly Lock _gate = new();

    public int Count { get { lock (_gate) return _items.Count; } }
    public T this[int index] { get { lock (_gate) return _items[index]; } }
    public void Add(T item) { lock (_gate) _items.Add(item); }
    public void Clear() { lock (_gate) _items.Clear(); }
    public IEnumerator<T> GetEnumerator()
    {
        T[] snapshot;
        lock (_gate) snapshot = _items.ToArray();
        return ((IEnumerable<T>)snapshot).GetEnumerator();
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
