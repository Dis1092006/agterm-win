namespace AgTerm.Core.Model;

// Split, overlay, and scratch panes (port of AppStore+Panes.swift)

public sealed partial class AppStore
{
    /// <summary>Toggles the one-level split for a session. The second pane's surface is created
    /// lazily by the host on first show and kept alive when hidden, so this only flips the flag.
    /// The flag is persisted, so the split is restored on relaunch.</summary>
    public void ToggleSplit(Guid sessionId)
    {
        if (SessionWithId(sessionId) is not { } session) return;
        session.IsSplit = !session.IsSplit;
        // opening a NEW split marks the session as having one and moves focus to the new (right)
        // pane; RE-showing a hidden split preserves whichever pane was focused before it was hidden.
        // hiding (toggling off) leaves HasSplit and SplitFocused set so the split indicators persist
        // and the focused pane is the one shown maximized. Only CloseSplit clears them.
        if (session.IsSplit)
        {
            var isNewSplit = !session.HasSplit;
            session.HasSplit = true;
            if (isNewSplit) session.SplitFocused = true;
        }
        Save();
    }

    /// <summary>Sets a session's split-divider left-pane fraction to ratio, clamped to the bounds,
    /// and persists. Returns the applied (clamped) fraction, or null when the id is unknown. Moving
    /// the LIVE divider is driven separately by the host.</summary>
    public double? ApplySplitRatio(double ratio, Guid forSession)
    {
        if (SessionWithId(forSession) is not { } session) return null;
        var applied = ClampSplitRatio(ratio);
        session.SplitRatio = applied;
        Save();
        return applied;
    }

    /// <summary>Clear the agent-status indicator when the pane that OWNED it is being torn down, so
    /// a pane-tagged block can't strand a glyph no surviving surface can keystroke-clear. owner is
    /// the pane whose surface is going away; a null tag counts as Left (main), matching the
    /// clear-decision default. Mirrors the ClearSearch reconcile on these same teardown paths.</summary>
    private void ClearIndicatorOwnedByPane(StatusPane owner, Session session)
    {
        if (session.AgentIndicator.Status == AgentStatus.Idle) return;
        if ((session.AgentIndicator.StatusPane ?? StatusPane.Left) != owner) return;
        SetAgentIndicator(new AgentIndicator(), session.Id);
    }

    /// <summary>Closes the split pane: hides it AND tears down its surface, so a subsequent split
    /// starts a fresh shell. Used when the split shell exits on its own; resets the focus flag so a
    /// stale SplitFocused doesn't point the collapsed view at the gone pane.</summary>
    public void CloseSplit(Guid sessionId)
    {
        if (SessionWithId(sessionId) is not { } session) return;
        session.IsSplit = false;
        session.HasSplit = false;
        session.SplitFocused = false;
        session.SplitSurface?.Teardown();
        session.SplitSurface = null;
        session.SplitCwd = null;
        session.SplitTitle = null;
        session.InitialSplitCwd = null;
        // the right pane is gone, so its restore-command override describes nothing: drop both the
        // persisted pin and any payload still armed for this launch (a fresh split must be a plain shell).
        session.SplitRestoreCommand = null;
        session.PendingSplitRestoreCommand = null;
        session.SplitRatio = null; // tearing down the split clears its geometry too, so a fresh split opens even
        // a search bar pinned to the torn-down split surface would otherwise stay stuck, so reset
        // search on the surviving session.
        session.ClearSearch();
        // the split (right) pane owned any Right-tagged block; with it gone the surviving main pane
        // can never keystroke-clear that tag, so clear it here (mirrors the search reset above).
        ClearIndicatorOwnedByPane(StatusPane.Right, session);
        Save();
    }

    /// <summary>
    /// The primary pane's shell exited. If a split pane is alive it is PROMOTED into the primary
    /// slot and the session survives as a single (non-split) pane; otherwise the session is closed.
    /// The survivor MOVES from SplitSurface into Surface (its surface, cwd, title, and foreground
    /// command migrate to the main fields, and its split-role reporting is turned off via
    /// PromoteToPrimaryPane), so the session becomes indistinguishable from a fresh single pane.
    /// Called by the primary surface's exit path.
    /// </summary>
    public void ClosePrimaryPane(Guid sessionId)
    {
        if (SessionWithId(sessionId) is not { } session) return;
        if (session.SplitSurface is not { } survivor)
        {
            CloseSession(sessionId);
            return;
        }
        var priorPrimary = session.Surface; // the exiting pane, torn down below; scopes the search reset
        priorPrimary?.Teardown();
        // promote the surviving split pane into the primary slot. PromoteToPrimaryPane flips the
        // surface's split-role flag so its future pwd/title reports write to the main fields.
        survivor.PromoteToPrimaryPane();
        session.Surface = survivor;
        session.SplitSurface = null;
        session.IsSplit = false;
        session.HasSplit = false;
        session.SplitFocused = false;
        session.SplitRatio = null; // promoted to a single pane; a later split should open even, not stale
        // the command pane is gone — the promoted survivor is a plain shell, so drop the creation
        // command (and its held-open flag) together.
        session.InitialCommand = null;
        session.CommandWait = false;
        // migrate the split pane's live/persisted metadata up to the session (main) fields, then
        // clear the now-meaningless split fields so nothing still describes a pane that no longer
        // exists. cwd prefers the split's live PWD, then its restore-seed, and only falls back to
        // the exited primary's cwd when the split has none at all. title is replaced OUTRIGHT from
        // the split's (null clears it) — likewise foregroundCommand.
        session.CurrentCwd = session.SplitCwd ?? session.InitialSplitCwd ?? session.CurrentCwd;
        session.OscTitle = session.SplitTitle;
        session.ForegroundCommand = session.SplitForegroundCommand;
        // the restore-command override follows the survivor into the main slot the same way, BOTH
        // halves: the persisted pin and any payload still armed for this launch.
        session.RestoreCommand = session.SplitRestoreCommand;
        session.PendingRestoreCommand = session.PendingSplitRestoreCommand;
        session.SplitCwd = null;
        session.SplitTitle = null;
        session.InitialSplitCwd = null;
        session.SplitForegroundCommand = null;
        session.SplitRestoreCommand = null;
        session.PendingSplitRestoreCommand = null;
        // reset search only if the torn-down primary owned the bar (or the weak ref already
        // dangled), so a search owned by the SURVIVING pane stays valid across promotion.
        if (session.SearchSurface is null || ReferenceEquals(session.SearchSurface, priorPrimary))
        {
            session.ClearSearch();
        }
        // migrate the agent-status identity like the cwd/title above: the exited primary owned any
        // Left/null-tagged block, which dies with it (clear); a Right-tagged block belonged to the
        // promoted survivor and FOLLOWS it into the main slot — re-tag to Left so the tree (which
        // now reports split:false) and the survivor's keystroke-clear agree. A Scratch block is untouched.
        if (session.AgentIndicator.Status != AgentStatus.Idle)
        {
            switch (session.AgentIndicator.StatusPane ?? StatusPane.Left)
            {
                case StatusPane.Left:
                    SetAgentIndicator(new AgentIndicator(), session.Id);
                    break;
                case StatusPane.Right:
                    SetAgentIndicator(session.AgentIndicator with { StatusPane = StatusPane.Left }, session.Id);
                    break;
                case StatusPane.Scratch:
                    break;
            }
        }
        Save();
    }

    /// <summary>The split pane's shell exited. It collapses to the primary (CloseSplit) ONLY when a
    /// genuine two-pane split is live — BOTH Surface and SplitSurface set. Otherwise this was the
    /// session's last pane and the session is closed: a promoted survivor keeps the split pane's
    /// exit routing, so its own exit still lands here, and closing (not collapsing a split that no
    /// longer exists) is what keeps it from leaving a zombie session.</summary>
    public void CloseSplitPane(Guid sessionId)
    {
        if (SessionWithId(sessionId) is not { } session) return;
        if (session.Surface is null || session.SplitSurface is null)
        {
            CloseSession(sessionId);
            return;
        }
        CloseSplit(sessionId);
    }

    /// <summary>Opens an ephemeral overlay terminal on a session running command (e.g. a TUI). The
    /// overlay surface is created lazily by the host and runs the command as its process; when the
    /// program exits, CloseOverlay tears it down. No-op (returns false) when the session is unknown
    /// or already has an overlay open. NOT persisted — the overlay never survives a relaunch.
    /// sizePercent (clamped to 1..100) requests a FLOATING overlay; null gives the default full-pane
    /// overlay that hides the session. backgroundColor (#rrggbb) gives the overlay pane its own
    /// solid background; null leaves the default theme background.</summary>
    public bool OpenOverlay(Guid sessionId, string command, string? cwd = null,
                            bool wait = false, int? sizePercent = null, string? backgroundColor = null)
    {
        if (SessionWithId(sessionId) is not { } session || session.OverlayActive) return false;
        session.OverlayCommand = command;
        session.OverlayCwd = cwd;
        session.OverlayWait = wait;
        session.OverlayExitCode = null;
        session.OverlaySizePercent = sizePercent is { } percent ? Math.Min(100, Math.Max(1, percent)) : null;
        session.OverlayBackgroundColor = backgroundColor;
        session.OverlayActive = true;
        NotifySidebar();
        return true;
    }

    /// <summary>Resizes an already-open overlay in place. sizePercent (clamped to 1..100) switches
    /// it to a FLOATING panel at that percent of the pane; null switches it to the full-pane
    /// overlay. The overlay surface stays mounted, so this only re-flows the layout — the program
    /// keeps running, never re-spawns. No-op (returns false) with no overlay open.</summary>
    public bool ResizeOverlay(Guid sessionId, int? sizePercent)
    {
        if (SessionWithId(sessionId) is not { } session || !session.OverlayActive) return false;
        session.OverlaySizePercent = sizePercent is { } percent ? Math.Min(100, Math.Max(1, percent)) : null;
        NotifySidebar();
        return true;
    }

    /// <summary>Records the overlay program's exit status so session.overlay.result can report it
    /// after the overlay closes. No-op for an unknown session.</summary>
    public void RecordOverlayExit(Guid sessionId, int code)
    {
        if (SessionWithId(sessionId) is { } session) session.OverlayExitCode = code;
    }

    /// <summary>Closes the overlay terminal: hides it AND tears down its surface (unlike the split,
    /// the overlay is never kept alive — it is ephemeral). Used both on explicit close and when the
    /// overlay's program exits on its own. No-op (returns false) when there is no overlay.</summary>
    public bool CloseOverlay(Guid sessionId)
    {
        if (SessionWithId(sessionId) is not { } session || !session.OverlayActive) return false;
        if (session.OverlayExitCode is null &&
            session.OverlaySurface is ICompletedExitCodeSurface { CompletedExitCode: { } exitCode })
        {
            session.OverlayExitCode = exitCode;
        }
        session.OverlayActive = false;
        session.OverlaySurface?.Teardown();
        session.OverlaySurface = null;
        session.OverlayCommand = null;
        session.OverlayCwd = null;
        session.OverlayWait = false;
        session.OverlaySizePercent = null;
        session.OverlayBackgroundColor = null;
        NotifySidebar();
        return true;
    }

    /// <summary>Toggles the scratch terminal for a session — a third, full-overlay login shell. The
    /// scratch surface is created lazily by the host on first show and, like the split, kept alive
    /// when hidden (this only flips ScratchActive), so a re-show reuses the same shell. Not
    /// persisted, so no Save. No-op for an unknown session.</summary>
    public void ToggleScratch(Guid sessionId)
    {
        if (SessionWithId(sessionId) is not { } session) return;
        session.ScratchActive = !session.ScratchActive;
        NotifySidebar();
    }

    /// <summary>Closes the scratch terminal: hides it AND tears down its surface (so a subsequent
    /// show starts a fresh shell). Used on the scratch shell's own exit and on
    /// session/workspace/window teardown. No-op (returns false) when there is no scratch surface.</summary>
    public bool CloseScratch(Guid sessionId)
    {
        if (SessionWithId(sessionId) is not { } session || session.ScratchSurface is not { } scratch)
            return false;
        session.ScratchActive = false;
        // if the open search bar is pinned to the scratch being torn down, reset search rather than
        // leave a stuck, no-op bar — mirrors the CloseSplit/ClosePrimaryPane handling. Guarded on
        // identity so a search owned by the main/split pane survives the scratch teardown.
        if (ReferenceEquals(session.SearchSurface, scratch)) session.ClearSearch();
        // a Scratch-tagged block loses its owning surface here; clear it so it can't strand a glyph
        // the surviving main/split panes can never keystroke-clear.
        ClearIndicatorOwnedByPane(StatusPane.Scratch, session);
        scratch.Teardown();
        session.ScratchSurface = null;
        NotifySidebar();
        return true;
    }
}
