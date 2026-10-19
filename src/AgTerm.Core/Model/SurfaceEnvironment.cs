namespace AgTerm.Core.Model;

/// <summary>
/// The AGTERM_* variables a spawned shell gets, so a script inside it knows its session, window,
/// workspace and the control socket. Ports upstream's SurfaceEnvironment without its terminal identity
/// part, which stays in TerminalIdentity (PLATFORM-NOTES, "Terminal identification for spawned shells").
/// </summary>
public static class SurfaceEnvironment
{
    public const string Enabled = "AGTERM_ENABLED";
    public const string SessionId = "AGTERM_SESSION_ID";
    public const string WindowId = "AGTERM_WINDOW_ID";
    public const string WorkspaceId = "AGTERM_WORKSPACE_ID";
    public const string Socket = "AGTERM_SOCKET";
    public const string Pane = "AGTERM_PANE";
    public const string PaneId = "AGTERM_PANE_ID";

    /// <summary>Every name the builder can emit. A spawn removes all of them from the inherited
    /// environment first, so a value from another agterm instance never leaks through.</summary>
    public static IReadOnlyList<string> OwnedVariables { get; } =
        [Enabled, SessionId, WindowId, WorkspaceId, Socket, Pane, PaneId];

    /// <summary>The set for a session surface. A null socket path omits AGTERM_SOCKET: the
    /// AGTERM_CONTROL_SOCKET override names a pipe, and no path derives to it.</summary>
    public static IReadOnlyDictionary<string, string> Session(
        Guid sessionId, Guid? windowId, Guid? workspaceId, string? socketPath,
        StatusPane? pane = null, string? paneToken = null)
    {
        var env = new Dictionary<string, string>
        {
            [Enabled] = "1",
            [SessionId] = Format(sessionId),
        };
        if (windowId is { } window) env[WindowId] = Format(window);
        if (workspaceId is { } workspace) env[WorkspaceId] = Format(workspace);
        if (socketPath is not null) env[Socket] = socketPath;
        if (pane is { } p) env[Pane] = WireName(p);
        if (!string.IsNullOrEmpty(paneToken)) env[PaneId] = paneToken;
        return env;
    }

    /// <summary>The set for the quick terminal, which belongs to a window but to no session.</summary>
    public static IReadOnlyDictionary<string, string> QuickTerminal(Guid windowId, string? socketPath)
    {
        var env = new Dictionary<string, string>
        {
            [Enabled] = "1",
            [WindowId] = Format(windowId),
        };
        if (socketPath is not null) env[Socket] = socketPath;
        return env;
    }

    // Swift uuidString is upper-case.
    private static string Format(Guid id) => id.ToString("D").ToUpperInvariant();

    private static string WireName(StatusPane pane) => pane switch
    {
        StatusPane.Left => "left",
        StatusPane.Right => "right",
        StatusPane.Scratch => "scratch",
        _ => throw new ArgumentOutOfRangeException(nameof(pane), pane, null),
    };
}
