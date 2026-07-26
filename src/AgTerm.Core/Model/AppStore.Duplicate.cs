namespace AgTerm.Core.Model;

// Port of AppStore+Duplicate.swift

public sealed partial class AppStore
{
    /// <summary>Duplicates a session: a fresh shell in the SAME workspace, inserted directly after
    /// the source, rooted at the source's focused-pane cwd (FocusedCwd — the directory the sidebar
    /// row shows). ONLY the directory carries over: the duplicate is a plain new session — auto
    /// basename, no split, no scratch, no status, unflagged, no InitialCommand. Returns null if no
    /// session matches. Backs the sidebar row's "Duplicate" and the session.duplicate command.</summary>
    public Session? DuplicateSession(Guid id)
    {
        if (SessionWithId(id) is not { } session || SessionLocation(id) is not { } location) return null;
        return AddSession(location.Workspace, session.FocusedCwd, at: location.Index + 1);
    }
}
