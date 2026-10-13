using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

// The HUD slot (port of the HUD half of upstream AppStore+Panes.swift). A HUD shares the session-wide
// slot rule with a program overlay but runs no process, so it never sets OverlayActive: the live host
// reserves a terminal surface for that flag.

public sealed partial class AppStore
{
    /// <summary>Raised under the host's model lock after a HUD opens, changes size or closes, so the
    /// window redraws it.</summary>
    public event Action? HudChanged;

    /// <summary>Opens a HUD over the session. A live HUD is replaced; a program overlay refuses. False
    /// for an unknown session.</summary>
    public bool OpenHud(Guid sessionId, HudSpec spec, Guid? paneIdentity)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (SessionWithId(sessionId) is not { } session || session.OverlayActive) return false;
        // upstream opens a HUD through the overlay slot, which drops the last program's exit code, so
        // overlay.result after this HUD closes reports no result rather than a stale one
        session.OverlayExitCode = null;
        session.Hud = spec;
        session.HudPaneIdentity = paneIdentity;
        session.HudFontSize = spec.FontSize ?? session.FontSize;
        session.HudForcedWidthPercent = null;
        session.HudPanelSize = MeasureHud(session);
        session.HudRevision++;
        HudChanged?.Invoke();
        return true;
    }

    /// <summary>Replaces a live HUD in place. The background color and the font size stay the open's;
    /// every other field, the pane scope included, is the update's. False with no HUD up.</summary>
    public bool UpdateHud(Guid sessionId, HudSpec spec, Guid? paneIdentity)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (SessionWithId(sessionId) is not { } session || session.Hud is not { } live) return false;
        session.Hud = spec.HoldingCreationFields(live);
        session.HudPaneIdentity = paneIdentity;
        session.HudForcedWidthPercent = null;
        session.HudPanelSize = MeasureHud(session);
        session.HudRevision++;
        HudChanged?.Invoke();
        return true;
    }

    /// <summary>False with no HUD up, which leaves a program overlay in the slot alone.</summary>
    public bool CloseHud(Guid sessionId)
    {
        if (SessionWithId(sessionId) is not { } session || !session.HudActive) return false;
        CloseHudOf(session);
        return true;
    }

    /// <summary>session.overlay.resize against a HUD: the width becomes a forced width that holds
    /// through re-measures until the next open or update. The height is measured again; the revision
    /// stays.</summary>
    public bool ResizeHud(Guid sessionId, int sizePercent)
    {
        if (SessionWithId(sessionId) is not { } session || session.Hud is not { } spec) return false;
        session.HudForcedWidthPercent = HudLayout.ClampSizePercent(sizePercent, spec);
        session.HudPanelSize = MeasureHud(session);
        HudChanged?.Invoke();
        return true;
    }

    /// <summary>Window close and application exit.</summary>
    public void CloseAllHuds()
    {
        foreach (var session in Workspaces.SelectMany(workspace => workspace.Sessions).ToArray())
            CloseHudOf(session);
    }

    /// <summary>Takes the object, not the id: the pending-close paths reach sessions that are no longer
    /// in the tree.</summary>
    internal void CloseHudOf(Session session)
    {
        if (!session.HudActive) return;
        session.Hud = null;
        session.HudPaneIdentity = null;
        session.HudPanelSize = null;
        session.HudForcedWidthPercent = null;
        session.HudFontSize = null;
        session.HudRevision++;
        HudChanged?.Invoke();
    }

    /// <summary>A soft close keeps the session object for undo, but an expiry cannot find a session
    /// outside the tree, so a HUD counting itself out would come back on undo after its time was up.
    /// A HUD with no auto-hide stays exactly as it was, which is what undo restores.</summary>
    private void CloseTimedHud(Session session)
    {
        if (session.Hud is { EffectiveHideAfter: > 0.0 }) CloseHudOf(session);
    }

    private static HudPanelSize MeasureHud(Session session)
    {
        var spec = session.Hud!;
        var sized = session.HudForcedWidthPercent is { } forced ? spec with { SizePercent = forced } : spec;
        return HudLayout.PanelSize(sized, HudPaneMetrics(session, spec));
    }

    private static PaneMetrics HudPaneMetrics(Session session, HudSpec spec) => PaneMetrics.Unmeasured;

    private static ControlHudNode? HudNode(Session session)
    {
        if (session.Hud is not { } spec || session.HudPanelSize is not { } size) return null;
        return new ControlHudNode
        {
            Message = spec.Message,
            Detail = spec.Detail,
            Spinner = spec.Spinner is { } spinner ? HudSpinners.Wire(spinner) : HudSpinners.NoneName,
            BackgroundColor = spec.BackgroundColor,
            TextColor = spec.TextColor,
            SizePercent = session.HudForcedWidthPercent ?? size.WidthPercent,
            HeightPercent = size.HeightPercent,
            Position = HudPositions.Wire(spec.Position),
            Pane = session.HudTargetPane is { } pane ? OverlayPanes.Wire(pane) : null,
            HideAfter = spec.EffectiveHideAfter,
            Markdown = spec.Markdown,
            FontSize = spec.FontSize,
            Sticky = spec.Sticky,
            Frame = spec.Frame,
        };
    }
}
