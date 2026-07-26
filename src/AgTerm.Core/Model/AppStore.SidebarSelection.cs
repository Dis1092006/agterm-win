namespace AgTerm.Core.Model;

// Port of AppStore+SidebarSelection.swift

public sealed partial class AppStore
{
    /// <summary>Replaces the transient sidebar selection with ids from the current visible sidebar
    /// projection.</summary>
    public void SetSidebarSelection(IReadOnlyList<Guid> ids)
    {
        SidebarSelectionRaw = ids.ToList();
    }

    /// <summary>Replaces the transient sidebar selection with a single active row.</summary>
    public void ReplaceSidebarSelection(Guid? with)
    {
        SidebarSelectionRaw = with is { } id ? new List<Guid> { id } : new List<Guid>();
    }

    /// <summary>Context-menu target resolution for sidebar session commands.</summary>
    public List<Guid> SidebarSelectionTargets(Guid? forContextSession)
    {
        var selectionIds = SidebarSelectionIds;
        if (forContextSession is { } clicked && selectionIds.Contains(clicked)) return selectionIds;
        if (forContextSession is { } clickedVisible && VisibleSidebarSessionIds.Contains(clickedVisible))
            return new List<Guid> { clickedVisible };
        if (selectionIds.Count > 0) return selectionIds;
        if (SelectedSessionId is not { } selected || !VisibleSidebarSessionIds.Contains(selected))
            return new List<Guid>();
        return new List<Guid> { selected };
    }

    /// <summary>The selected session ids that are still visible in the current sidebar mode/focus.</summary>
    public List<Guid> SidebarSelectionIds
    {
        get
        {
            var requested = new HashSet<Guid>(SidebarSelectionRaw);
            var normalized = VisibleSidebarSessionIds.Where(requested.Contains).ToList();
            if (SelectedSessionId is not { } selected) return new List<Guid>();
            return normalized.Contains(selected) ? normalized : new List<Guid>();
        }
    }

    internal void PruneSidebarSelection()
    {
        SidebarSelectionRaw = SidebarSelectionIds;
    }

    private List<Guid> VisibleSidebarSessionIds => NavigableSessions.Select(s => s.Id).ToList();
}
