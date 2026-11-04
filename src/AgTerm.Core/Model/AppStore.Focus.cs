namespace AgTerm.Core.Model;

// Port of AppStore+Focus.swift (upstream 6722755a: focus as a set of workspaces)

public sealed partial class AppStore
{
    /// <summary>The workspaces the sidebar tree renders: the marked set while the filter is enabled,
    /// else all. The empty-result fallback guards an invariant violation only (the mutators keep
    /// enabled-with-an-empty-set out of reach); rendering the full tree beats stranding the user with
    /// no rows.</summary>
    public List<Workspace> VisibleWorkspaces
    {
        get
        {
            if (!FocusEnabled) return Workspaces;
            var visible = Workspaces.Where(w => FocusedWorkspaceIds.Contains(w.Id)).ToList();
            return visible.Count == 0 ? Workspaces : visible;
        }
    }

    /// <summary>The marked workspace when the filter is on and exactly one workspace is marked, else
    /// null. The single-focus view the old one-workspace focus showed.</summary>
    public Guid? SoleFocusedWorkspaceId =>
        FocusEnabled && FocusedWorkspaceIds.Count == 1 ? FocusedWorkspaceIds.First() : null;

    /// <summary>Whether id is the only marked workspace while the filter is on.</summary>
    public bool IsSoleFocus(Guid id) => SoleFocusedWorkspaceId == id;

    public bool IsCurrentWorkspaceSoleFocus => CurrentWorkspaceId is { } id && IsSoleFocus(id);

    public bool IsCurrentWorkspaceFocusMember => CurrentWorkspaceId is { } id && FocusedWorkspaceIds.Contains(id);

    /// <summary>Focuses a single workspace: the set becomes just it and the filter turns on. No-op
    /// for an unknown workspace.</summary>
    public void SetFocusedWorkspace(Guid id)
    {
        if (!Workspaces.Any(w => w.Id == id)) return;
        CommitFocus(new HashSet<Guid> { id }, true);
    }

    /// <summary>Empties the set and turns the filter off.</summary>
    public void ClearFocus() => CommitFocus(new HashSet<Guid>(), false);

    /// <summary>Clears the focus when id is the sole focus, else focuses it alone.</summary>
    public void ToggleFocusedWorkspace(Guid id)
    {
        if (IsSoleFocus(id)) ClearFocus();
        else SetFocusedWorkspace(id);
    }

    /// <summary>The workspace.focus modes: on focuses it alone, off un-marks it, add marks it
    /// without enabling the filter, toggle flips the sole focus.</summary>
    public void ApplyFocusMode(Protocol.ControlWorkspaceFocusMode mode, Guid id)
    {
        switch (mode)
        {
            case Protocol.ControlWorkspaceFocusMode.On: SetFocusedWorkspace(id); break;
            case Protocol.ControlWorkspaceFocusMode.Off: SetFocusMembership(id, false); break;
            case Protocol.ControlWorkspaceFocusMode.Add: SetFocusMembership(id, true); break;
            case Protocol.ControlWorkspaceFocusMode.Toggle: ToggleFocusedWorkspace(id); break;
        }
    }

    /// <summary>The workspace.filter modes, resolved against the current filter state.</summary>
    public void ApplyWorkspaceFilter(Protocol.ControlToggleMode mode) =>
        SetFocusEnabled(Protocol.ControlToggleModes.DesiredValue(mode, FocusEnabled));

    /// <summary>Marks or un-marks a workspace without touching the filter flag. Marking an unknown
    /// workspace is a no-op; un-marking is not gated, so a stale id can always be removed.</summary>
    public void SetFocusMembership(Guid id, bool member)
    {
        if (member && !Workspaces.Any(w => w.Id == id)) return;
        var next = new HashSet<Guid>(FocusedWorkspaceIds);
        if (member) next.Add(id); else next.Remove(id);
        CommitFocus(next, FocusEnabled);
    }

    /// <summary>Turns the filter on or off over the current set. On with an empty set stays off.</summary>
    public void SetFocusEnabled(bool on) => CommitFocus(new HashSet<Guid>(FocusedWorkspaceIds), on);

    /// <summary>The single write point of the two focus fields: clamps enabled to a non-empty set,
    /// does nothing when neither field changes, and otherwise repairs the state the narrowing or
    /// widening leaves behind (fresh target, sidebar selection, a hidden active session) and saves.</summary>
    private void CommitFocus(HashSet<Guid> ids, bool enabled)
    {
        var wantEnabled = enabled && ids.Count > 0;
        if (FocusedWorkspaceIds.SetEquals(ids) && FocusEnabled == wantEnabled) return;
        FocusedWorkspaceIds = ids;
        FocusEnabled = wantEnabled;
        ForgetHiddenFreshWorkspace();
        PruneSidebarSelection();
        ReselectIfSelectionHidden();
        Save();
    }

    private void ForgetHiddenFreshWorkspace()
    {
        if (_freshWorkspaceId is { } fresh && !VisibleWorkspaces.Any(w => w.Id == fresh))
            _freshWorkspaceId = null;
    }

    /// <summary>Removes a workspace from the set; the filter goes off with the last member. Does not
    /// save: the caller is mid-mutation and saves once.</summary>
    internal void DropFocusMember(Guid id)
    {
        if (!FocusedWorkspaceIds.Remove(id)) return;
        if (FocusedWorkspaceIds.Count == 0) FocusEnabled = false;
    }

    /// <summary>Adds a new workspace to the set only while the filter is on, so it is not created
    /// hidden. Does not save.</summary>
    internal void RevealNewFocusMember(Guid id)
    {
        if (FocusEnabled) FocusedWorkspaceIds.Add(id);
    }

    /// <summary>Re-marks a workspace that was a member when it was closed. Insert-only; does not
    /// save.</summary>
    internal void MarkFocusMember(Guid id) => FocusedWorkspaceIds.Add(id);

    /// <summary>Switches the filter off, keeping the set, when the newly selected session lives
    /// outside it, so an explicit cross-set select reveals its target. No-op when the filter is off,
    /// in flagged mode (the filter does not apply there), or with no selection.</summary>
    internal void DisableFocusIfSelectionOutsideSet(Guid? sessionId)
    {
        if (!FocusEnabled || SidebarMode == SidebarMode.Flagged || sessionId is not { } id) return;
        if (WorkspaceForSession(id) is { } owner && FocusedWorkspaceIds.Contains(owner.Id)) return;
        FocusEnabled = false;
    }

    /// <summary>Loads the persisted focus after the tree is rebuilt: the set loses ids whose
    /// workspace is gone, and the filter stays on only for a non-empty set. Writes the fields
    /// directly: the mutators would save what was just read.</summary>
    internal void RestoreFocus(Snapshot snapshot)
    {
        var present = Workspaces.Select(w => w.Id).ToHashSet();
        FocusedWorkspaceIds = (snapshot.FocusedWorkspaceIds ?? Array.Empty<Guid>()).Where(present.Contains).ToHashSet();
        FocusEnabled = (snapshot.FocusEnabled ?? false) && FocusedWorkspaceIds.Count > 0;
    }

    /// <summary>Moves the selection back inside the visible set whenever the active session is
    /// outside it: a narrowing that hid it, or a widening that filled an empty set. Targets the most
    /// recent visible session, else the first. No-op on a nil selection or an empty visible set.</summary>
    internal void ReselectIfSelectionHidden()
    {
        if (SelectedSessionId is not { } selected) return;
        var visible = NavigableSessions;
        if (visible.Count == 0 || visible.Any(s => s.Id == selected)) return;
        SelectSession(NavigableRecentSessions(1).Cast<Guid?>().FirstOrDefault() ?? visible[0].Id);
    }

    /// <summary>The selection after the workspace holding the active session is removed: the most
    /// recent visible session, else the first visible one. The positional walk at index applies only
    /// when nothing is visible.</summary>
    internal Guid? WorkspaceRemovalTarget(int index)
    {
        var visible = NavigableSessions;
        if (visible.Count > 0)
            return NavigableRecentSessions(1).Cast<Guid?>().FirstOrDefault() ?? visible[0].Id;
        if (Workspaces.Count == 0) return null;
        var fallbackIndex = Math.Min(index, Workspaces.Count - 1);
        return Workspaces[fallbackIndex].Sessions.FirstOrDefault()?.Id
            ?? Workspaces.FirstOrDefault(w => w.Sessions.Count > 0)?.Sessions[0].Id;
    }
}
