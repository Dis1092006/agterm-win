namespace AgTerm.Core.Model;

// Naming (port of AppStore+Naming.swift)

public sealed partial class AppStore
{
    /// <summary>Sets a session's custom name, with a blank value restoring its automatic display name.</summary>
    public void RenameSession(Guid sessionId, string to)
    {
        if (SessionWithId(sessionId) is not { } session) return;
        var renamed = Session.TrimmedOrNull(TerminalText.Sanitized(to));
        if (session.CustomName == renamed) return;
        session.CustomName = renamed;
        ScheduleTreeChanged();
        Save();
    }

    /// <summary>Sets or clears (null) what a session is about. Returns whether anything changed; an
    /// unknown session or an unchanged value is a no-op with no save and no event. The caller has
    /// already validated the text (Session.ValidateContext).</summary>
    public bool SetContext(string? context, Guid sessionId)
    {
        if (SessionWithId(sessionId) is not { } session || session.Context == context) return false;
        session.Context = context;
        ScheduleTreeChanged();
        Save();
        return true;
    }

    /// <summary>Renames a workspace. Blank and same-value names are structural no-ops.</summary>
    public void RenameWorkspace(Guid workspaceId, string to)
    {
        if (Session.TrimmedOrNull(TerminalText.Sanitized(to)) is not { } trimmed) return;
        var index = Workspaces.FindIndex(w => w.Id == workspaceId);
        if (index < 0 || Workspaces[index].Name == trimmed) return;
        Workspaces[index].Name = trimmed;
        ScheduleTreeChanged();
        Save();
    }
}
