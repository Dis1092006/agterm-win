namespace AgTerm.Core.Model;

/// <summary>
/// A chrome element the user can hide through the hiddenInterfaceElements setting — the port of
/// agtermCore's InterfaceElement. The persisted form is the raw value (the upstream case name), in
/// upstream's declaration order.
/// </summary>
public enum InterfaceElement
{
    // title bar
    SidebarToggle,
    SessionName,
    WindowName,
    RecentSessions,
    Scratch,
    Split,
    Dashboard,
    QuickTerminal,
    // sidebar
    NewWorkspace,
    NewSession,
    FlaggedView,
    WorkspaceAddSession,
}

public static class InterfaceElementExtensions
{
    /// <summary>The raw value stored in settings.json: the upstream Swift case name.</summary>
    public static string RawValue(this InterfaceElement element)
    {
        var name = element.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
