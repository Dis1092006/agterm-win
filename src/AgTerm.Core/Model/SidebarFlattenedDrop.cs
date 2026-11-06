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

    /// <summary>Where a pointer at y drops among the rendered rows. bounds holds the realized rows only, in
    /// list order. Over a row, its top quarter is the gap above it, its bottom quarter the gap below it, and
    /// the middle half a drop ON it (AppKit's outline draws the same split). Outside every row the gap
    /// follows the row midpoints above the pointer.</summary>
    public static SidebarDropPoint Locate(IReadOnlyList<SidebarRowBounds> bounds, double y)
    {
        var gap = 0;
        foreach (var row in bounds)
        {
            if (y >= row.Top && y < row.Top + row.Height)
            {
                var fraction = (y - row.Top) / row.Height;
                if (fraction < 0.25) return new SidebarDropPoint.Gap(row.Index);
                if (fraction >= 0.75) return new SidebarDropPoint.Gap(row.Index + 1);
                return new SidebarDropPoint.OnRow(row.Index);
            }
            if (y > row.Top + row.Height / 2) gap = row.Index + 1;
        }
        return new SidebarDropPoint.Gap(gap);
    }

    /// <summary>The session drop a drop point means. With workspace rows (the tree and the flagged tree) a
    /// gap is a slot among its workspace's rendered session rows; without them (the flat and plain flagged
    /// list) it is a slot in the list. Null when the tree has no workspace to drop into.</summary>
    public static SidebarSessionDrop? SessionDrop(IReadOnlyList<SidebarRow> rows, bool rendersWorkspaceRows,
                                                  SidebarDropPoint point)
    {
        switch (point)
        {
            case SidebarDropPoint.OnRow(var index) when index >= 0 && index < rows.Count:
                return rows[index].Kind == SidebarRowKind.Workspace
                    ? new SidebarSessionDrop.OnWorkspace(rows[index].Id)
                    : new SidebarSessionDrop.OnSession(rows[index].Id);
            case SidebarDropPoint.Gap(var index) when !rendersWorkspaceRows:
                return new SidebarSessionDrop.ListGap(index);
            case SidebarDropPoint.Gap(var index):
                return SessionSlot(rows, index) is { } slot
                    ? new SidebarSessionDrop.Gap(slot.Workspace, slot.ChildIndex)
                    : null;
            default:
                return null;
        }
    }
}

/// <summary>A realized sidebar row's vertical extent in list coordinates.</summary>
public readonly record struct SidebarRowBounds(int Index, double Top, double Height);

/// <summary>Where a drag points in the flattened sidebar list.</summary>
public abstract record SidebarDropPoint
{
    /// <summary>Between rows: Index is the flat insertion index.</summary>
    public sealed record Gap(int Index) : SidebarDropPoint;

    /// <summary>On the row at Index.</summary>
    public sealed record OnRow(int Index) : SidebarDropPoint;
}
