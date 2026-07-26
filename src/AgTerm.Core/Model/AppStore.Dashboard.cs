namespace AgTerm.Core.Model;

// Port of AppStore+Dashboard.swift (+ DashboardMember from DashboardController.swift)

/// <summary>One dashboard cell's identity: a session plus which of its panes the cell hosts. A
/// non-split session yields a single Primary member; a split session yields both Primary and Split,
/// so each pane gets its own cell. Surface is always Primary or Split for the dashboard.</summary>
public readonly record struct DashboardMember(Guid Session, TerminalZoomSurface Surface)
{
    /// <summary>The pane ref the control tree reports for this cell: &lt;UUID&gt;:left|right.</summary>
    public string ControlRef => $"{Session.ToString("D").ToUpperInvariant()}:{Surface.WireName()}";
}

public sealed partial class AppStore
{
    /// <summary>Expand session ids into dashboard pane cells IN ORDER — each resolved id yields its
    /// Primary cell, plus a Split cell when the session HasSplit (both shells alive, shown OR
    /// hidden) — so a split session becomes two cells. Ids that don't resolve to a session are
    /// skipped. UNCAPPED: the caller applies the cell cap and reports the dropped-pane count.</summary>
    public List<DashboardMember> DashboardPaneCells(IReadOnlyList<Guid> ids)
    {
        var members = new List<DashboardMember>();
        foreach (var id in ids)
        {
            if (SessionWithId(id) is not { } session) continue;
            members.Add(new DashboardMember(id, TerminalZoomSurface.Primary));
            if (session.HasSplit) members.Add(new DashboardMember(id, TerminalZoomSurface.Split));
        }
        return members;
    }

    /// <summary>Expand ids into dashboard pane cells and cap the result to limit cells, returning
    /// the capped cells plus the number of panes dropped past the cap. The single expansion + cap
    /// used by both the control dashboard command and the GUI toggle; Dropped feeds the control
    /// path's "dropped N pane(s) beyond the N-cell limit" note.</summary>
    public (List<DashboardMember> Members, int Dropped) DashboardMembers(IReadOnlyList<Guid> ids, int limit)
    {
        var expanded = DashboardPaneCells(ids);
        var capped = expanded.Take(limit).ToList();
        return (capped, expanded.Count - capped.Count);
    }

    /// <summary>The window's most-recently-used sessions expanded into capped dashboard pane cells —
    /// the GUI toggle's MRU open. limit bounds BOTH the session pull and the pane cap; empty when
    /// the window has no recent sessions.</summary>
    public List<DashboardMember> DashboardMruMembers(int limit) =>
        DashboardMembers(RecentSessions(limit), limit).Members;
}
