using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

// Agent status & attention (port of AppStore+Status.swift)

public sealed partial class AppStore
{
    /// <summary>
    /// The control channel's session.status write. Refuses a non-blocked write from a pane other than the
    /// one that owns a blocked status, so a background pane cannot wipe a prompt another pane is waiting
    /// on; a blocked write from any pane replaces. An untagged write reads as the left pane, and a Right
    /// tag reads as Left when the session has no split. Internal clears go through SetAgentIndicator.
    /// </summary>
    public StatusWriteResult ApplyControlStatus(AgentIndicator indicator, Guid forSession)
    {
        if (SessionWithId(forSession) is not { } session) return StatusWriteResult.Applied;
        var current = session.AgentIndicator;
        if (current.Status == AgentStatus.Blocked && indicator.Status != AgentStatus.Blocked)
        {
            var owner = current.NormalizedPane(session.HasSplit) ?? StatusPane.Left;
            var writer = indicator.NormalizedPane(session.HasSplit) ?? StatusPane.Left;
            if (owner != writer) return StatusWriteResult.Refused(owner);
        }
        SetAgentIndicator(indicator, forSession);
        return StatusWriteResult.Applied;
    }

    /// <summary>
    /// Sets a session's agent status indicator (the sidebar status glyph). The single mutation
    /// point for the control channel's session.status. Stamps StatusChangedAt with the current time
    /// on every accepted write, idle and repeated values included (the stamp is never cleared; the
    /// attention list only sorts non-idle sessions, so an idle stamp is harmless).
    /// Clears the session's AutoFollowConsumed on a transition INTO blocked, re-arming idle
    /// auto-follow for the fresh episode. No-op for an unknown id. Not persisted (the indicator is
    /// ephemeral), so it never triggers a Save.
    /// </summary>
    public void SetAgentIndicator(AgentIndicator indicator, Guid forSession)
    {
        if (SessionWithId(forSession) is not { } session) return;
        var previous = session.AgentIndicator;
        var wasBlocked = session.AgentIndicator.Status == AgentStatus.Blocked;
        // normalize a Right tag to Left when the session has NO split. A promoted survivor's shell
        // keeps its baked AGTERM_PANE=right, so the agent-status hook re-emits --pane right after
        // promotion even though there is no right pane — left unnormalized that re-creates the
        // split:false + statusPane:"right" contradiction, and the sole pane could never
        // keystroke-clear it. A hidden-but-LIVE split keeps HasSplit, so Right stays valid there.
        // Gated on HasSplit, NOT SplitSurface == null: a scripted session.split + immediate
        // session.status --pane right lands in the realization window where Right is the correct
        // forward tag and must NOT be rewritten.
        indicator = indicator with { StatusPane = indicator.NormalizedPane(session.HasSplit) };
        session.AgentIndicator = indicator;
        session.StatusChangedAt = DateTimeOffset.UtcNow;
        // a fresh block episode (entering blocked from a non-blocked status) re-arms idle
        // auto-follow for this session; a re-asserted blocked-over-blocked is not a new episode.
        if (!wasBlocked && indicator.Status == AgentStatus.Blocked) session.AutoFollowConsumed = false;
        NotifySidebar(); // ephemeral (never saved), so the glyph refresh needs its own invalidation
        if (previous == indicator)
        {
            RearmAutoFollowOnAttentionChange();
            return;
        }
        EmitControlEvent(
            ControlEventKind.Status,
            WorkspaceForSession(forSession)?.Id,
            forSession,
            new ControlEventPayload
            {
                Name = session.DisplayName,
                Status = StatusWire(indicator.Status),
                Pane = indicator.StatusPane is { } pane ? PaneWire(pane) : null,
                Blink = indicator.Blink,
                Color = indicator.Color,
                Shape = indicator.Shape is { } shape ? ShapeWire(shape) : null,
            });
        RearmAutoFollowOnAttentionChange();
    }

    /// <summary>
    /// The sidebar row action "Mark for Attention": gives each idle session a plain completed status
    /// with no auto-reset, so it stays on the attention list and navigation stops on it until the usual
    /// completed clear (keystroke, Clear Status, hook). A session that already has a status is skipped,
    /// so a live blocked, active or completed status is never overwritten. Equals session.status
    /// completed on idle targets; there is no control command for it.
    /// </summary>
    public void MarkForAttention(IEnumerable<Guid> ids)
    {
        foreach (var id in ids)
        {
            if (SessionWithId(id) is { } session && session.AgentIndicator.Status == AgentStatus.Idle)
                SetAgentIndicator(new AgentIndicator(AgentStatus.Completed), id);
        }
    }

    /// <summary>
    /// The window-wide non-idle sessions, the single source of truth for the attention icon and the
    /// attention palette. Spans ALL workspaces and deliberately IGNORES the focus/flagged sidebar
    /// filter (unlike NavigableSessions) — the point is window-wide visibility even when the sidebar
    /// is hidden. Sorted by attention rank ascending (blocked → active → completed) then
    /// StatusChangedAt DESCENDING (newest change first; a null stamp sorts last within its rank group).
    /// </summary>
    public List<Session> AttentionSessions =>
        Workspaces.SelectMany(w => w.Sessions)
            .Where(s => s.AgentIndicator.Status != AgentStatus.Idle)
            .OrderBy(s => s.AgentIndicator.Status.AttentionRank())
            .ThenByDescending(s => s.StatusChangedAt ?? DateTimeOffset.MinValue)
            .ToList();
}
