using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

// The HUD slot (port of the HUD half of upstream AppStore+Panes.swift and of the auto-hide in
// ControlServer+Hud.swift). A HUD shares the session-wide slot rule with a program overlay but runs no
// process, so it never sets OverlayActive: the live host reserves a terminal surface for that flag.

public sealed partial class AppStore
{
    private readonly Dictionary<Guid, IDisposable> _hudAutoHides = new();

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
        session.HudFontSize = spec.FontSize ?? session.FontSize
            ?? HudSessionMetrics.UsableFontSize(session.HudMetrics?.BaseFontSize);
        session.HudForcedWidthPercent = null;
        session.HudPanelSize = MeasureHud(session);
        session.HudRevision++;
        ArmHudAutoHide(session);
        HudChanged?.Invoke();
        return true;
    }

    /// <summary>Replaces a live HUD in place. The background color and the font size stay the open's;
    /// every other field, the pane scope and the auto-hide included, is the update's. False with no
    /// HUD up.</summary>
    public bool UpdateHud(Guid sessionId, HudSpec spec, Guid? paneIdentity)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (SessionWithId(sessionId) is not { } session || session.Hud is not { } live) return false;
        session.Hud = spec.HoldingCreationFields(live);
        session.HudPaneIdentity = paneIdentity;
        session.HudForcedWidthPercent = null;
        session.HudPanelSize = MeasureHud(session);
        session.HudRevision++;
        ArmHudAutoHide(session);
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
    /// and the auto-hide stay.</summary>
    public bool ResizeHud(Guid sessionId, int sizePercent)
    {
        if (SessionWithId(sessionId) is not { } session || session.Hud is not { } spec) return false;
        session.HudForcedWidthPercent = HudLayout.ClampSizePercent(sizePercent, spec);
        session.HudPanelSize = MeasureHud(session);
        HudChanged?.Invoke();
        return true;
    }

    /// <summary>Stores what the page measured for the session and measures a live HUD again, so the HUD
    /// follows a window or divider resize with no command. The spec, the scope, the forced width and
    /// the auto-hide stay. True when the HUD's size changed; a markdown HUD whose grid changed is
    /// redrawn either way, so its rows are fitted to the new grid.</summary>
    public bool SetHudMetrics(Guid sessionId, HudSessionMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        if (SessionWithId(sessionId) is not { } session) return false;
        var gridBefore = MarkdownGrid(session);
        // a pane the page does not lay out now (a hidden split) keeps its last rect, so a HUD anchored
        // there keeps its size until the pane is shown again
        session.HudMetrics = session.HudMetrics is { } previous
            ? metrics with { Left = metrics.Left ?? previous.Left, Right = metrics.Right ?? previous.Right }
            : metrics;
        if (!session.HudActive) return false;
        // a HUD opened before any font was known takes the first base font the page reports, and keeps it
        session.HudFontSize ??= HudSessionMetrics.UsableFontSize(metrics.BaseFontSize);
        var size = MeasureHud(session);
        var sizeChanged = size != session.HudPanelSize;
        session.HudPanelSize = size;
        // the page never posts equal metrics twice, so a redraw for a new grid cannot loop
        if (sizeChanged || MarkdownGrid(session) != gridBefore)
            HudChanged?.Invoke();
        return sizeChanged;
    }

    /// <summary>A markdown body is fitted to the panel grid, which a resize can change while the percent
    /// sizes stay the same; upstream rewrites the body on every re-measure.</summary>
    private static (int Columns, int Rows)? MarkdownGrid(Session session)
    {
        if (session.Hud is not { Markdown: true } hud || session.HudPanelSize is not { } size)
            return null;
        var effective = session.HudForcedWidthPercent is { } forced ? size with { WidthPercent = forced } : size;
        return HudLayout.PaintGrid(hud, effective, HudPaneMetrics(session));
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
        CancelHudAutoHide(session.Id);
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

    private void ArmHudAutoHide(Session session)
    {
        CancelHudAutoHide(session.Id);
        var seconds = Math.Min(session.Hud?.EffectiveHideAfter ?? 0, HudSpec.MaxHideAfter);
        // written as a negation so a NaN that slipped past the dispatcher arms nothing
        if (!(seconds > 0)) return;
        var sessionId = session.Id;
        var revision = session.HudRevision;
        var schedule = HudAutoHideScheduler ?? ScheduleOnHostLock;
        _hudAutoHides[sessionId] = schedule(TimeSpan.FromSeconds(seconds), () => ExpireHud(sessionId, revision));
    }

    private void CancelHudAutoHide(Guid sessionId)
    {
        if (_hudAutoHides.Remove(sessionId, out var timer)) timer.Dispose();
    }

    private void ExpireHud(Guid sessionId, int revision)
    {
        // an expiry that was already on its way when an update, a replace or a close moved the
        // revision belongs to a HUD that is gone
        if (SessionWithId(sessionId) is not { } session || session.HudRevision != revision) return;
        CloseHudOf(session);
    }

    private IDisposable ScheduleOnHostLock(TimeSpan delay, Action action)
    {
        var cancellation = new CancellationTokenSource();
        _ = Task.Delay(delay, cancellation.Token).ContinueWith(
            t =>
            {
                if (t.IsCanceled) return;
                if (PendingCloseFinalizationScheduler is { } schedule)
                    schedule(action);
                else
                    action();
            },
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        return new CancelOnDispose(cancellation);
    }

    private sealed class CancelOnDispose(CancellationTokenSource cancellation) : IDisposable
    {
        public void Dispose()
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    private static HudPanelSize MeasureHud(Session session)
    {
        var spec = session.Hud!;
        var sized = session.HudForcedWidthPercent is { } forced ? spec with { SizePercent = forced } : spec;
        return HudLayout.PanelSize(sized, HudPaneMetrics(session));
    }

    private static PaneMetrics HudPaneMetrics(Session session) =>
        session.HudMetrics is { } metrics
            ? metrics.For(session.HudTargetPane, session.HudFontSize ?? metrics.FontSize)
            : PaneMetrics.Unmeasured;

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
