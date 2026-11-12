using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// Presentation rules and 14x14 stroked vector geometries for the agent CLI icon shown before a
/// session row's name in the sidebar (Windows-only).
/// </summary>
public static class SidebarAgentIcon
{
    private const string ClaudePath =
        "M7,1.5 V12.5 M1.5,7 H12.5 M3.1,3.1 L10.9,10.9 M10.9,3.1 L3.1,10.9";

    private const string CopilotPath =
        "M3.5,6.5 A3.5,3.5 0 0 1 10.5,6.5 " +
        "M2.5,6.5 H11.5 A1,1 0 0 1 12.5,7.5 V9.5 A2,2 0 0 1 10.5,11.5 H8.5 L7,10 L5.5,11.5 H3.5 A2,2 0 0 1 1.5,9.5 V7.5 A1,1 0 0 1 2.5,6.5 Z " +
        "M4.8,8 V9.5 M9.2,8 V9.5";

    private const string CodexPath =
        "M7,1.5 L11.8,4.25 V9.75 L7,12.5 L2.2,9.75 V4.25 Z " +
        "M8.8,5.95 V8.05 L7,9.1 L5.2,8.05 V5.95 L7,4.9 Z " +
        "M7,1.5 L8.8,5.95 M11.8,4.25 L8.8,8.05 M11.8,9.75 L7,9.1 " +
        "M7,12.5 L5.2,8.05 M2.2,9.75 L5.2,5.95 M2.2,4.25 L7,4.9";

    public static bool IsShown(SidebarRowKind kind, SessionAgent? agent, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return kind == SidebarRowKind.Session
            && agent is not null
            && !settings.IsInterfaceElementHidden(InterfaceElement.AgentIcons);
    }

    public static string PathData(SessionAgent? agent) => agent switch
    {
        SessionAgent.Claude => ClaudePath,
        SessionAgent.Copilot => CopilotPath,
        SessionAgent.Codex => CodexPath,
        // an empty figure: x:Bind cannot convert "" to Path.Data
        _ => "M0,0",
    };

    public static string? ToolTip(SessionAgent? agent) => agent switch
    {
        SessionAgent.Claude => "Claude Code",
        SessionAgent.Copilot => "Copilot CLI",
        SessionAgent.Codex => "Codex",
        _ => null,
    };

    public static string FormatLabel(
        SidebarRow row,
        SidebarMode mode,
        FlaggedViewLayout layout,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(settings);
        if (!IsShown(row.Kind, row.Agent, settings) || row.Agent != SessionAgent.Claude || row.HasCustomName)
            return row.Label;

        if (mode == SidebarMode.Flagged
            && layout == FlaggedViewLayout.Flat
            && row.Label.LastIndexOf(" : ", StringComparison.Ordinal) is var cut and >= 0)
        {
            return StripLeadingClaudeStar(row.Label[..cut]) + row.Label[cut..];
        }

        return StripLeadingClaudeStar(row.Label);
    }

    public static string StripLeadingClaudeStar(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || text[0] != '\u2733') return text;
        var index = 1;
        if (index < text.Length && text[index] is '\uFE0E' or '\uFE0F') index++;
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index < text.Length ? text[index..] : text;
    }
}
