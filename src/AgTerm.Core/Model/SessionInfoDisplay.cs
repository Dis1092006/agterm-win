namespace AgTerm.Core.Model;

/// <summary>Where the sidebar draws a session's info line: in every sidebar view, only in the flagged
/// view, or nowhere (upstream 80f822bf). It gates drawing alone; the line, its expiry and its read-back
/// stay either way. Stored raw in AppSettings.sessionInfoDisplay.</summary>
public enum SessionInfoDisplay
{
    Always,
    Flagged,
    Off,
}

public static class SessionInfoDisplays
{
    public static IReadOnlyList<SessionInfoDisplay> All { get; } =
        [SessionInfoDisplay.Always, SessionInfoDisplay.Flagged, SessionInfoDisplay.Off];

    public static string RawValue(this SessionInfoDisplay display) => display switch
    {
        SessionInfoDisplay.Flagged => "flagged",
        SessionInfoDisplay.Off => "off",
        _ => "always",
    };

    /// <summary>The Settings picker's label for the value.</summary>
    public static string DisplayName(this SessionInfoDisplay display) => display switch
    {
        SessionInfoDisplay.Flagged => "Flagged",
        SessionInfoDisplay.Off => "Off",
        _ => "Always",
    };

    /// <summary>Whether the sidebar draws an info line in this view (upstream showsSessionInfo).</summary>
    public static bool Shows(SessionInfoDisplay display, SidebarMode mode) => display switch
    {
        SessionInfoDisplay.Off => false,
        SessionInfoDisplay.Flagged => mode == SidebarMode.Flagged,
        _ => true,
    };

    /// <summary>A known raw value matched exactly, as upstream's enum decode does; anything else is null.</summary>
    public static SessionInfoDisplay? FromRaw(string? raw) => raw switch
    {
        "always" => SessionInfoDisplay.Always,
        "flagged" => SessionInfoDisplay.Flagged,
        "off" => SessionInfoDisplay.Off,
        _ => null,
    };
}
