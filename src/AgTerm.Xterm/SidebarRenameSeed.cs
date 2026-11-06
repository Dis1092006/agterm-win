using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// The text an inline rename starts from. The flat flagged list labels a session "name : workspace",
/// so the rename seeds the bare name and the tail never gets committed into the custom name
/// (upstream pin). Every other view shows the name alone, and a name that contains " : " stays whole.
/// </summary>
public static class SidebarRenameSeed
{
    public static string Of(string label, SidebarMode mode, FlaggedViewLayout layout) =>
        mode == SidebarMode.Flagged && layout == FlaggedViewLayout.Flat
            && label.LastIndexOf(" : ", StringComparison.Ordinal) is var cut and >= 0
            ? label[..cut]
            : label;
}