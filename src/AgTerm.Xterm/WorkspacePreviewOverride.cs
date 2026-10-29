namespace AgTerm.Xterm;

/// <summary>
/// One workspace's preview of a value (a theme name, an icon name) and a pick the palette just
/// saved. The save answer can reach the window before the sidebar snapshot that has the new value,
/// so the pick is held until a snapshot requested after the save arrives. A null value is an
/// override too: it means the default. Used on the UI thread only.
/// </summary>
public sealed class WorkspacePreviewOverride<T>
{
    private long _observedGeneration;
    private Override? _override;

    public bool TryGet(Guid workspaceId, out T value)
    {
        if (_override is { } shown && shown.WorkspaceId == workspaceId)
        {
            value = shown.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public void Preview(Guid workspaceId, T value) =>
        _override = new Override(workspaceId, value, HeldUntil: null);

    public void EndPreview() => _override = null;

    /// <summary>Holds the saved pick until the snapshot of <paramref name="untilGeneration"/> or a
    /// later one, which the caller requested after the save finished. Snapshot values alone cannot
    /// tell a stale snapshot from a later change back to the old value.</summary>
    public void Saved(Guid workspaceId, T value, long untilGeneration) =>
        _override = _observedGeneration >= untilGeneration
            ? null
            : new Override(workspaceId, value, HeldUntil: untilGeneration);

    /// <summary>Called with the generation of every snapshot the view applies.</summary>
    public void Observe(long generation)
    {
        _observedGeneration = Math.Max(_observedGeneration, generation);
        if (_override is { HeldUntil: { } until } && _observedGeneration >= until)
            _override = null;
    }

    private sealed record Override(Guid WorkspaceId, T Value, long? HeldUntil);
}
