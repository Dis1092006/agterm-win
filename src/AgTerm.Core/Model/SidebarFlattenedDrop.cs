namespace AgTerm.Core.Model;

/// <summary>
/// Maps a flat insertion index over the projected row list (the WinUI sidebar is ONE flattened
/// ListView, unlike AppKit's outline) back into the (workspace, child index) space
/// <see cref="SidebarDrop"/> works in. Host-free so the fiddly walk is table-tested.
/// </summary>
public static class SidebarFlattenedDrop
{
    /// <summary>The workspace and pre-removal child index a flat insertion slot means, or null
    /// when the list has no workspace context (empty sidebar). A slot above the first workspace
    /// header targets the first workspace at 0; a slot at/below a header boundary belongs to the
    /// workspace above it (its append space).</summary>
    public static (Guid Workspace, int ChildIndex)? SessionSlot(
        IReadOnlyList<SidebarRow> rows, int flatIndex)
    {
        if (rows.Count == 0) return null;

        Guid? workspace = null;
        var childIndex = 0;
        var bounded = Math.Clamp(flatIndex, 0, rows.Count);
        for (var i = 0; i < bounded; i++)
        {
            if (rows[i].Kind == SidebarRowKind.Workspace)
            {
                workspace = rows[i].Id;
                childIndex = 0;
            }
            else
            {
                childIndex++;
            }
        }
        if (workspace is { } found) return (found, childIndex);

        // the slot sits above every header — target the first workspace's start
        var first = rows.FirstOrDefault(r => r.Kind == SidebarRowKind.Workspace);
        return first is null ? null : (first.Id, 0);
    }
}
