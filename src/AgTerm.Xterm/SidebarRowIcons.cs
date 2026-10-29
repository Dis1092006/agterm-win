using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// Gives each sidebar row the icon name it shows. A Windows-only extension (PLATFORM-NOTES
/// "Windows-only extensions"). It carries the workspace icon palette's preview for one workspace,
/// and a pick the palette just saved: the save answer can reach the window before the sidebar
/// snapshot that has the new icon, so the pick is held until a snapshot requested after the save
/// arrives. Used on the UI thread only.
/// </summary>
public sealed class SidebarRowIcons
{
    private long _observedGeneration;
    private Override? _override;

    public string? IconNameFor(SidebarRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return _override is { } shown && shown.WorkspaceId == row.WorkspaceId
            ? shown.IconName
            : row.IconName;
    }

    public void Preview(Guid workspaceId, string? iconName) =>
        _override = new Override(workspaceId, iconName, HeldUntil: null);

    public void EndPreview() => _override = null;

    /// <summary>Holds the saved pick until the snapshot of <paramref name="untilGeneration"/> or a
    /// later one, which the caller requested after the save finished. Snapshot names alone cannot
    /// tell a stale snapshot from a later change back to the old icon.</summary>
    public void Saved(Guid workspaceId, string? iconName, long untilGeneration) =>
        _override = _observedGeneration >= untilGeneration
            ? null
            : new Override(workspaceId, iconName, HeldUntil: untilGeneration);

    /// <summary>Called with the generation of every snapshot the view applies.</summary>
    public void Observe(long generation)
    {
        _observedGeneration = Math.Max(_observedGeneration, generation);
        if (_override is { HeldUntil: { } until } && _observedGeneration >= until)
            _override = null;
    }

    private sealed record Override(Guid WorkspaceId, string? IconName, long? HeldUntil);
}
