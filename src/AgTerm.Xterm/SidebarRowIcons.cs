using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// Gives each sidebar row the icon name it shows. A Windows-only extension (PLATFORM-NOTES
/// "Windows-only extensions"). It carries the workspace icon palette's preview and saved pick
/// (see <see cref="WorkspacePreviewOverride{T}"/>). Used on the UI thread only.
/// </summary>
public sealed class SidebarRowIcons
{
    private readonly WorkspacePreviewOverride<string?> _override = new();

    public string? IconNameFor(SidebarRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return _override.TryGet(row.WorkspaceId, out var shown) ? shown : row.IconName;
    }

    public void Preview(Guid workspaceId, string? iconName) => _override.Preview(workspaceId, iconName);

    public void EndPreview() => _override.EndPreview();

    public void Saved(Guid workspaceId, string? iconName, long untilGeneration) =>
        _override.Saved(workspaceId, iconName, untilGeneration);

    public void Observe(long generation) => _override.Observe(generation);
}
