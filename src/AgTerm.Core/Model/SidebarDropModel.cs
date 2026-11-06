namespace AgTerm.Core.Model;

/// <summary>What a dragged session block was dropped on in the sidebar.</summary>
public abstract record SidebarSessionDrop
{
    /// <summary>A gap in the tree or the flagged tree: Slot counts the workspace's rendered session rows
    /// above the gap.</summary>
    public sealed record Gap(Guid Workspace, int Slot) : SidebarSessionDrop;

    /// <summary>A gap in the flat or plain flagged list: Slot is the list index.</summary>
    public sealed record ListGap(int Slot) : SidebarSessionDrop;

    public sealed record OnWorkspace(Guid Workspace) : SidebarSessionDrop;

    public sealed record OnSession(Guid Session) : SidebarSessionDrop;
}

/// <summary>The store move a sidebar session drop performs.</summary>
public abstract record SidebarSessionMove
{
    /// <summary>AppStore.MoveFlagged arguments: the flat and plain flagged list.</summary>
    public sealed record Reorder(IReadOnlyList<Guid> Ids, FlaggedOrderPlacement Placement, Guid Anchor) : SidebarSessionMove
    {
        public bool Equals(Reorder? other) =>
            other is not null && Ids.SequenceEqual(other.Ids) && Placement == other.Placement && Anchor == other.Anchor;

        public override int GetHashCode() => HashCode.Combine(Placement, Anchor);
    }

    /// <summary>AppStore.MoveSessions arguments for the dragged ids: the tree and the flagged tree.
    /// Destination is the index after the dragged sessions are removed.</summary>
    public sealed record Sessions(Guid Workspace, int Destination) : SidebarSessionMove;
}

/// <summary>
/// The part of the store a sidebar session drop reads, captured in one read so the view can check a drop
/// while the pointer moves and the controller can apply it under the host gate with the same rules
/// (upstream 8088e146 WorkspaceSidebar+DragDrop validateDrop/acceptDrop). Workspaces hold every session,
/// including those the rows hide.
/// </summary>
public sealed record SidebarDropModel(
    SidebarMode Mode,
    FlaggedViewLayout Layout,
    IReadOnlyList<(Guid Id, IReadOnlyList<Guid> Sessions)> Workspaces,
    IReadOnlySet<Guid> Flagged,
    IReadOnlyList<Guid> FlaggedOrder)
{
    public static SidebarDropModel From(AppStore store) => new(
        store.SidebarMode,
        store.FlaggedLayout,
        store.Workspaces.Select(w => (w.Id, (IReadOnlyList<Guid>)w.Sessions.Select(s => s.Id).ToList())).ToList(),
        store.Workspaces.SelectMany(w => w.Sessions).Where(s => s.Flagged).Select(s => s.Id).ToHashSet(),
        store.OrderedFlaggedSessions.Select(s => s.Id).ToList());

    private bool IsFlaggedList => Mode == SidebarMode.Flagged && Layout != FlaggedViewLayout.Tree;

    /// <summary>The move that dropping dragged (in sidebar order) on drop performs, or null when it changes
    /// nothing on screen or does not fit the layout.</summary>
    public SidebarSessionMove? Resolve(IReadOnlyList<Guid> dragged, SidebarSessionDrop drop)
    {
        if (IsFlaggedList)
        {
            var reorder = drop switch
            {
                SidebarSessionDrop.ListGap(var slot) => SidebarDrop.ResolveFlaggedReorder(FlaggedOrder, dragged, null, slot),
                SidebarSessionDrop.OnSession(var row) =>
                    SidebarDrop.ResolveFlaggedReorder(FlaggedOrder, dragged, row, SidebarDrop.OnItemIndex),
                _ => null,
            };
            return reorder is { } move ? new SidebarSessionMove.Reorder(move.Ids, move.Placement, move.Anchor) : null;
        }

        var sources = new List<SidebarDrop.SessionSource>(dragged.Count);
        foreach (var id in dragged)
        {
            if (Locate(id) is not { } source) return null;
            sources.Add(new SidebarDrop.SessionSource(source.Workspace, source.Index));
        }
        if (Target(drop) is not { } target) return null;
        if (SidebarDrop.ResolveSessions(sources, target.Target, target.ChildIndex) is not { } resolution) return null;
        if (Mode == SidebarMode.Flagged && !new SidebarDrop.FlaggedTree(Workspaces, Flagged).ChangesView(dragged, resolution))
            return null;
        return new SidebarSessionMove.Sessions(resolution.Workspace, resolution.Destination);
    }

    private (SidebarDrop.SessionDropTarget Target, int ChildIndex)? Target(SidebarSessionDrop drop)
    {
        switch (drop)
        {
            case SidebarSessionDrop.Gap(var id, var slot) when Workspace(id) is { } sessions:
                var childIndex = Mode == SidebarMode.Flagged
                    ? SidebarDrop.FlaggedTreeChildIndex(
                        sessions.Select((s, i) => (s, i)).Where(p => Flagged.Contains(p.s)).Select(p => p.i).ToList(), slot)
                    : slot;
                return (new SidebarDrop.SessionDropTarget.WorkspaceRow(id, sessions.Count), childIndex);
            case SidebarSessionDrop.OnWorkspace(var id) when Workspace(id) is { } sessions:
                return (new SidebarDrop.SessionDropTarget.WorkspaceRow(id, sessions.Count), SidebarDrop.OnItemIndex);
            case SidebarSessionDrop.OnSession(var id) when Locate(id) is { } row:
                return (new SidebarDrop.SessionDropTarget.SessionRow(row.Workspace, row.Index, Workspace(row.Workspace)!.Count),
                        SidebarDrop.OnItemIndex);
            default:
                return null;
        }
    }

    private IReadOnlyList<Guid>? Workspace(Guid id) =>
        Workspaces.Where(w => w.Id == id).Select(w => w.Sessions).FirstOrDefault();

    private (Guid Workspace, int Index)? Locate(Guid session)
    {
        foreach (var (id, sessions) in Workspaces)
        {
            for (var index = 0; index < sessions.Count; index++)
                if (sessions[index] == session) return (id, index);
        }
        return null;
    }
}
