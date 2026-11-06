using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// Whether a session row draws the flag after its name (Windows-only). It shows in every sidebar mode,
/// so a flagged session stays marked in the flat and state lists too; the sessionFlags interface
/// element hides it.
/// </summary>
public static class SidebarSessionFlag
{
    public static bool IsShown(SidebarRowKind kind, bool flagged, AppSettings settings) =>
        kind == SidebarRowKind.Session && flagged && !settings.IsInterfaceElementHidden(InterfaceElement.SessionFlags);
}
