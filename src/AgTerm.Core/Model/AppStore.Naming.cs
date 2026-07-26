namespace AgTerm.Core.Model;

// Naming (port of AppStore+Naming.swift)

public sealed partial class AppStore
{
    /// <summary>Sets a session's custom name, with a blank value restoring its automatic display name.</summary>
    public void RenameSession(Guid sessionId, string to)
    {
        if (SessionWithId(sessionId) is not { } session) return;
        var renamed = Session.TrimmedOrNull(to);
        if (session.CustomName == renamed) return;
        session.CustomName = renamed;
        ScheduleTreeChanged();
        Save();
    }

    /// <summary>Renames a workspace. Blank and same-value names are structural no-ops.</summary>
    public void RenameWorkspace(Guid workspaceId, string to)
    {
        if (Session.TrimmedOrNull(to) is not { } trimmed) return;
        var index = Workspaces.FindIndex(w => w.Id == workspaceId);
        if (index < 0 || Workspaces[index].Name == trimmed) return;
        Workspaces[index].Name = trimmed;
        ScheduleTreeChanged();
        Save();
    }
}
