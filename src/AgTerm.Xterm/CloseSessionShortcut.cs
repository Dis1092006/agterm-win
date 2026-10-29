using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm;

/// <summary>Window close shortcut: dismiss the topmost cover before closing the active session.
/// The control API's session.close intentionally remains a direct session close.</summary>
public sealed class CloseSessionShortcut
{
    private readonly ILiveStoreCoordinator _coordinator;
    private readonly HeadlessControlActions _actions;

    public CloseSessionShortcut(ILiveStoreCoordinator coordinator, HeadlessControlActions actions)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public async Task ExecuteAsync(
        Func<Guid, Task> closeSession,
        Func<bool>? escapeFocusedAsk = null,
        Action? focusActiveSession = null)
    {
        ArgumentNullException.ThrowIfNull(closeSession);

        var modalDismissed = false;
        await _coordinator.MutateAsync(_ =>
        {
            if (_actions.Pick.PendingAsk is { } ask)
            {
                if (!_actions.Pick.EscapeAsk(ask.Id))
                    throw new InvalidOperationException("Could not dismiss pending GUI question.");
                modalDismissed = true;
            }
            else if (_actions.Pick.Pending is { } pick)
            {
                if (!_actions.Pick.Cancel(pick.Id))
                    throw new InvalidOperationException("Could not dismiss pending picker.");
                modalDismissed = true;
            }
        });
        if (modalDismissed || escapeFocusedAsk?.Invoke() == true)
            return;

        var windowCoverDismissed = false;
        var dashboardDismissed = false;
        await _coordinator.MutateAsync(_ =>
        {
            ControlResponse? result = null;
            if (_actions.Quick.HoldsKey && _actions.Quick.IsZoomed)
                result = _actions.SetSurfaceZoom("quick", null, ControlToggleMode.Off);
            else if (_actions.Quick.HoldsKey)
                result = _actions.SetQuickTerminal("hide");
            else if (_actions.Zoom.Target is not null)
                result = _actions.SetSurfaceZoom("active", null, ControlToggleMode.Off);
            else if (_actions.Dashboard.IsOpen)
            {
                result = _actions.SetDashboard(Array.Empty<string>(), null, close: true,
                    new DashboardFontMode.Untouched(), mru: false);
                dashboardDismissed = true;
            }

            if (result is null) return;
            if (!result.Ok)
                throw new InvalidOperationException($"Could not dismiss terminal cover: {result.Error}");
            windowCoverDismissed = true;
        });
        if (dashboardDismissed)
            focusActiveSession?.Invoke();
        if (windowCoverDismissed)
            return;

        Guid? sessionToClose = null;
        await _coordinator.MutateAsync(store =>
        {
            if (store.ActiveSession is not { } session) return;
            if (session.HudActive)
                store.CloseHud(session.Id);
            else if (session.OverlayActive)
                store.CloseOverlay(session.Id);
            else if (session.ScratchActive)
                store.ToggleScratch(session.Id);
            else
                sessionToClose = session.Id;
        });
        if (sessionToClose is { } id)
            await closeSession(id);
    }
}
