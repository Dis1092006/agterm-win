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

    /// <summary>Which of the two separators in the title bar's trailing button cluster to draw, given
    /// how many visible buttons each group has (A = recent sessions + attention, B = scratch + split,
    /// C = dashboard + quick terminal). A separator sits only where two groups that each still show 2+
    /// buttons meet: AfterA between A and B, AfterB between B and C, or, when B is empty, directly
    /// between a full A and a full C. Upstream InterfaceElement.titlebarGroupDividers.</summary>
    public static (bool AfterA, bool AfterB) TitlebarGroupDividers(int countA, int countB, int countC)
    {
        var afterA = countA >= 2 && countB >= 2;
        var afterB = (countB >= 2 && countC >= 2) || (countA >= 2 && countC >= 2 && countB == 0);
        return (afterA, afterB);
    }
}
