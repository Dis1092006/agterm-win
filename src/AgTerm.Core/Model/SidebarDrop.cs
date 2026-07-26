namespace AgTerm.Core.Model;

/// <summary>
/// The pure index arithmetic for sidebar drag-and-drop reorder (port of agtermCore
/// SidebarDrop.swift), so the trickiest part of the drop handling — post-removal off-by-one,
/// drop-on-row redirect, cross-workspace vs same-parent index spaces, no-op detection — is
/// host-free and table-testable. The WinUI/store glue resolves the dragged/target ids, feeds
/// indices in, and applies the returned destination via AppStore.MoveSession(s)/MoveWorkspace.
/// </summary>
public static class SidebarDrop
{
    /// <summary>Mirrors AppKit's NSOutlineViewDropOnItemIndex: a drop landing ON a row rather than
    /// between rows.</summary>
    public const int OnItemIndex = -1;

    /// <summary>Prevents an accidental Explorer multi-selection from spawning an unbounded number
    /// of shells.</summary>
    public const int MaximumDirectoryImportCount = 20;

    /// <summary>Resolves the workspace for an Explorer directory drop. Folder import is a tree-only
    /// affordance; empty-space drops prefer the focused workspace so adding a session cannot
    /// silently leave focus mode.</summary>
    public static Guid? ResolveDirectoryWorkspace(SidebarMode sidebarMode, Guid? rowWorkspaceId,
                                                  Guid? focusedWorkspaceId, Guid? currentWorkspaceId)
    {
        if (sidebarMode != SidebarMode.Tree) return null;
        return rowWorkspaceId ?? focusedWorkspaceId ?? currentWorkspaceId;
    }

    /// <summary>The destination of a dragged session (Destination is the POST-removal index
    /// AppStore.MoveSession expects; DropChildIndex is the PRE-removal slot to highlight, or
    /// OnItemIndex to append).</summary>
    public readonly record struct SessionResolution(Guid Workspace, int DropChildIndex, int Destination);

    /// <summary>A dragged session's original location before a batch move removes anything.</summary>
    public readonly record struct SessionSource(Guid Workspace, int Index);

    /// <summary>The row a session was dropped onto: a workspace row uses the child index directly;
    /// a session row redirects to its owner workspace, landing just after it when dropped ON the
    /// row.</summary>
    public abstract record SessionDropTarget
    {
        public sealed record WorkspaceRow(Guid Id, int SessionCount) : SessionDropTarget;

        public sealed record SessionRow(Guid Workspace, int SessionIndex, int SessionCount) : SessionDropTarget;
    }

    /// <summary>Resolves a session drop into the move it would perform, or null for a no-op.
    /// childIndex is the proposed child index (OnItemIndex for a drop ON the target). For a
    /// same-workspace DOWNWARD move the slot shifts up by one after the removal, so 1 is
    /// subtracted; cross-workspace and upward moves pass through. A same-workspace move that lands
    /// the session back in its current slot (including appending an already-last session) is a
    /// no-op.</summary>
    public static SessionResolution? ResolveSession(Guid sourceWorkspace, int sourceIndex,
                                                    SessionDropTarget target, int childIndex)
    {
        var (workspace, targetCount, dropChildIndex) = Unpack(target, childIndex);

        var sameWorkspace = sourceWorkspace == workspace;
        var destination = dropChildIndex;
        if (dropChildIndex == OnItemIndex)
        {
            destination = targetCount;
        }
        else if (sameWorkspace && sourceIndex < dropChildIndex)
        {
            destination = dropChildIndex - 1;
        }

        if (sameWorkspace)
        {
            // MoveSession removes the source first (shrinking the list to targetCount - 1), then
            // clamps, so the landed slot is the clamped destination; equal to the source slot
            // means no change.
            var landed = Math.Max(0, Math.Min(destination, targetCount - 1));
            if (landed == sourceIndex) return null;
        }
        return new SessionResolution(workspace, dropChildIndex, destination);
    }

    /// <summary>Resolves a batch session drop into one remove-all/insert-block move. sources must
    /// be in the dragged block's intended order. The returned Destination is the target index
    /// AFTER all dragged sessions have been removed from their original workspaces.</summary>
    public static SessionResolution? ResolveSessions(IReadOnlyList<SessionSource> sources,
                                                     SessionDropTarget target, int childIndex)
    {
        if (sources.Count == 0) return null;
        var (workspace, targetCount, dropChildIndex) = Unpack(target, childIndex);

        var rawDestination = dropChildIndex == OnItemIndex ? targetCount : dropChildIndex;
        var sourceIndicesInTarget = sources.Where(s => s.Workspace == workspace)
            .Select(s => s.Index).OrderBy(i => i).ToList();
        var removedBeforeDestination = sourceIndicesInTarget.Count(i => i < rawDestination);
        var destination = rawDestination - removedBeforeDestination;

        if (sourceIndicesInTarget.Count > 0 &&
            sourceIndicesInTarget.Count == sources.Count &&
            sourceIndicesInTarget.SequenceEqual(
                Enumerable.Range(sourceIndicesInTarget[0], sourceIndicesInTarget.Count)) &&
            destination == sourceIndicesInTarget[0])
        {
            return null;
        }

        return new SessionResolution(workspace, dropChildIndex, destination);
    }

    /// <summary>Resolves an anchor-relative session placement (control --after/--before) into the
    /// same move ResolveSession produces for a drag. placeAfter lands just after the anchor row
    /// (via OnItemIndex → anchorIndex + 1); otherwise it lands at the anchor's slot. Reuses the
    /// drop math verbatim, so the post-removal off-by-one, cross-workspace index space, and
    /// anchor==source no-op all fall out unchanged.</summary>
    public static SessionResolution? ResolveRelative((Guid Workspace, int Index) source,
                                                     (Guid Workspace, int Index, int Count) anchor,
                                                     bool placeAfter)
    {
        var target = new SessionDropTarget.SessionRow(anchor.Workspace, anchor.Index, anchor.Count);
        var childIndex = placeAfter ? OnItemIndex : anchor.Index;
        return ResolveSession(source.Workspace, source.Index, target, childIndex);
    }

    /// <summary>The destination of a dragged workspace (Destination is the POST-removal index
    /// AppStore.MoveWorkspace expects), or null for a no-op. Same downward childIndex - 1
    /// adjustment as sessions.</summary>
    public readonly record struct WorkspaceResolution(int DropChildIndex, int Destination);

    /// <summary>Resolves a top-level workspace reorder (validity — a between-rows root drop — is
    /// the caller's to enforce). count is the pre-removal workspace count.</summary>
    public static WorkspaceResolution? ResolveWorkspace(int sourceIndex, int count, int childIndex)
    {
        var dropChildIndex = childIndex == OnItemIndex ? count : childIndex;
        var destination = dropChildIndex;
        if (sourceIndex < dropChildIndex) destination = dropChildIndex - 1;
        if (sourceIndex == destination) return null;
        return new WorkspaceResolution(dropChildIndex, destination);
    }

    private static (Guid Workspace, int TargetCount, int DropChildIndex) Unpack(
        SessionDropTarget target, int childIndex) => target switch
    {
        SessionDropTarget.WorkspaceRow(var id, var count) => (id, count, childIndex),
        SessionDropTarget.SessionRow(var owner, var index, var count) =>
            (owner, count, childIndex == OnItemIndex ? index + 1 : childIndex),
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };
}
