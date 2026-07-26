using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

// Agent status & attention (port of AppStore+Status.swift)

public sealed partial class AppStore
{
    /// <summary>
    /// Sets a session's agent status indicator (the sidebar status glyph). The single mutation
    /// point for the control channel's session.status. Stamps StatusChangedAt with the current time
    /// on any non-idle status (the attention list's newest-first sort key) and clears it on idle.
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
        if (indicator.StatusPane == StatusPane.Right && !session.HasSplit)
        {
            indicator = indicator with { StatusPane = StatusPane.Left };
        }
        session.AgentIndicator = indicator;
        session.StatusChangedAt = indicator.Status == AgentStatus.Idle ? null : DateTimeOffset.UtcNow;
        // a fresh block episode (entering blocked from a non-blocked status) re-arms idle
        // auto-follow for this session; a re-asserted blocked-over-blocked is not a new episode.
        if (!wasBlocked && indicator.Status == AgentStatus.Blocked) session.AutoFollowConsumed = false;
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
