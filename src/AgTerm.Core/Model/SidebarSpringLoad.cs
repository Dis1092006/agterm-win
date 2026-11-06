namespace AgTerm.Core.Model;

/// <summary>
/// The hover timer of spring-loaded expansion (upstream WorkspaceSidebar+DragDrop
/// scheduleSpringLoadedExpansion, cancelSpringLoadedExpansion and finishDraggingSequence). A drag that
/// rests on a collapsed workspace for Delay opens it; moving to another workspace, a drop target that is
/// not a workspace, leaving the list or cancelling the drag folds it back, and a successful drop keeps it
/// open. The open and the fold go to the store (AppStore.SpringOpenSidebarWorkspace and
/// EndSidebarSpringLoad), which does not save them. Host-free: the caller passes the timer.
/// </summary>
public sealed class SidebarSpringLoad
{
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(650);

    private readonly Func<TimeSpan, Action, Action> _schedule;
    private readonly Action<Guid> _open;
    private readonly Action<bool> _end;
    private Guid? _pending;
    private Action? _cancelPending;
    private Guid? _opened;

    /// <param name="schedule">Runs the action once after the delay; returns the cancel action.</param>
    /// <param name="open">Opens the workspace on screen.</param>
    /// <param name="end">Ends the drag's open workspace: true keeps it open, false folds it back.</param>
    public SidebarSpringLoad(Func<TimeSpan, Action, Action> schedule, Action<Guid> open, Action<bool> end)
    {
        _schedule = schedule;
        _open = open;
        _end = end;
    }

    /// <summary>The drag moved. workspace is the one a drop here would land in, or null when the drag has
    /// no workspace target; isOpen is whether the rows show it open.</summary>
    public void Hover(Guid? workspace, bool isOpen)
    {
        if (workspace is not { } id)
        {
            Cancel();
            return;
        }
        // the rows may not show the open workspace yet, so the drag's own open counts as open
        if (_opened == id) return;
        if (isOpen)
        {
            Cancel();
            return;
        }
        if (_pending == id) return;
        Cancel();
        _pending = id;
        _cancelPending = _schedule(Delay, () =>
        {
            if (_pending != id) return;
            _pending = null;
            _cancelPending = null;
            _opened = id;
            _open(id);
        });
    }

    /// <summary>Leave or cancel: folds the workspace the drag opened.</summary>
    public void Cancel() => Finish(keepOpen: false);

    /// <summary>The end of the drag: a successful drop keeps the opened workspace open.</summary>
    public void Finish(bool keepOpen)
    {
        _cancelPending?.Invoke();
        _cancelPending = null;
        _pending = null;
        if (_opened is null) return;
        _opened = null;
        _end(keepOpen);
    }
}
