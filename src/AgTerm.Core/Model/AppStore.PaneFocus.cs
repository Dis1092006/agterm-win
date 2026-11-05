using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

// pane.focused (upstream e958477b): the selected terminal of a split session settled on another one.

public sealed partial class AppStore
{
    private const double PaneFocusDebounceSeconds = 0.1;

    /// <summary>Coalesces focus flips: a move and back inside the window reports nothing.</summary>
    internal readonly Debouncer PaneFocusDebouncer = new();

    /// <summary>The selected terminal's identity per session as of the last check. Identities, not the
    /// left/right flag, so a swap (which moves both terminals and the flag together) stays silent.</summary>
    private Dictionary<Guid, Guid> _paneFocusBaseline = new();

    /// <summary>Hooks a session the store now owns: a focus flip schedules a check, and the session's
    /// current selection becomes its baseline so its first real change is reported.</summary>
    internal void WatchPaneFocus(Session session)
    {
        session.PaneFocusChanged = SchedulePaneFocusCheck;
        _paneFocusBaseline[session.Id] = session.SelectedPaneIdentity;
    }

    private void SchedulePaneFocusCheck() =>
        PaneFocusDebouncer.Schedule(PaneFocusDebounceSeconds, () =>
        {
            // the timer fires off the model context; the host's scheduler brings the read under its lock
            if (PendingCloseFinalizationScheduler is { } schedule)
                schedule(EmitPaneFocusChanges);
            else
                EmitPaneFocusChanges();
        });

    internal void EmitPaneFocusChanges()
    {
        var current = new Dictionary<Guid, Guid>();
        foreach (var workspace in Workspaces.ToArray())
        {
            foreach (var session in workspace.Sessions.ToArray())
            {
                var selected = session.SelectedPaneIdentity;
                current[session.Id] = selected;
                if (!_paneFocusBaseline.TryGetValue(session.Id, out var before) || before == selected) continue;
                var pane = session.SplitFocused && session.HasSplit ? StatusPane.Right : StatusPane.Left;
                var token = session.PaneToken(pane);
                EmitControlEvent(ControlEventKind.PaneFocused, workspace.Id, session.Id, new ControlEventPayload
                {
                    Name = session.DisplayName,
                    Pane = PaneWire(pane),
                    PaneId = token.Length > 0 ? token : null,
                });
            }
        }
        _paneFocusBaseline = current;
    }

    private void ResetPaneFocusBaseline()
    {
        PaneFocusDebouncer.Cancel();
        _paneFocusBaseline = new Dictionary<Guid, Guid>();
        foreach (var session in Workspaces.SelectMany(workspace => workspace.Sessions))
            WatchPaneFocus(session);
    }
}
