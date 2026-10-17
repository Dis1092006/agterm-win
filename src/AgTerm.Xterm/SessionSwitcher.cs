namespace AgTerm.Xterm;

/// <summary>The keys the Ctrl-Tab switcher listens to: Ctrl+Tab, Ctrl+Shift+Tab, Esc, and Ctrl's release.</summary>
public enum SessionSwitcherKey
{
    Tab,
    ReverseTab,
    Escape,
    ControlReleased,
}

/// <summary>
/// The Ctrl-Tab "previously visited session" switcher, upstream's SessionSwitcher. Hold Ctrl and press
/// Tab to walk a frozen most-recently-used list (Shift+Tab reverses); releasing Ctrl commits the
/// highlighted session, Esc abandons the switch. The list is snapshotted when the switch begins, so
/// cycling never reorders it; only the commit does, through the selection. The window feeds it key
/// signals from the terminal page and from its own key events; this class holds only the state.
/// </summary>
public sealed class SessionSwitcher
{
    public bool IsActive { get; private set; }

    public IReadOnlyList<TitleBarRecentSession> Candidates { get; private set; } = [];

    public int Index { get; private set; }

    /// <summary>Raised after every change of IsActive, Candidates or Index.</summary>
    public event Action? Changed;

    /// <summary>Ctrl+Tab (reverse: Ctrl+Shift+Tab). The first press snapshots the candidates and
    /// highlights the previous session; later presses move the highlight. A window that cannot switch
    /// now resets instead. The press is always the switcher's, never the terminal's.</summary>
    public void Tab(bool reverse, bool canSwitch, Func<IReadOnlyList<TitleBarRecentSession>> candidates)
    {
        if (!canSwitch)
        {
            Reset();
            return;
        }
        if (IsActive)
        {
            var count = Candidates.Count;
            Index = ((Index + (reverse ? -1 : 1)) % count + count) % count;
            Changed?.Invoke();
            return;
        }
        var order = candidates();
        if (order.Count < 2)
            return;
        Candidates = order;
        Index = 1;
        IsActive = true;
        Changed?.Invoke();
    }

    /// <summary>Esc: abandons an active switch. Returns whether the key was the switcher's.</summary>
    public bool Escape()
    {
        if (!IsActive)
            return false;
        Reset();
        return true;
    }

    /// <summary>Ctrl released: the session to select, or null when no switch was in progress (or the
    /// window cannot switch now). Either way the switcher is idle afterwards.</summary>
    public Guid? ControlReleased(bool canSwitch)
    {
        if (!IsActive)
            return null;
        var selected = canSwitch ? Candidates[Index].Id : (Guid?)null;
        Reset();
        return selected;
    }

    /// <summary>Abandons a switch without selecting, as when the window loses focus or a modal opens.</summary>
    public void Cancel()
    {
        if (IsActive)
            Reset();
    }

    private void Reset()
    {
        var wasActive = IsActive;
        IsActive = false;
        Candidates = [];
        Index = 0;
        if (wasActive)
            Changed?.Invoke();
    }
}
