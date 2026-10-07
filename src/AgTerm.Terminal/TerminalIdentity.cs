namespace AgTerm.Terminal;

/// <summary>
/// What a spawned shell is told about the terminal around it. Upstream needs none of this: agterm
/// runs on Ghostty, which identifies itself to its own children. Here the children are spawned by
/// a WinUI app, so without a declaration they inherit whatever launched agterm-win (an IDE's
/// terminal, another shell) and TUIs then negotiate against the wrong terminal.
/// </summary>
public static class TerminalIdentity
{
    /// <summary>
    /// Variables that must be ABSENT from a child, not overwritten. Terminal detection generally
    /// tests whether TERM_PROGRAM is set before falling through to Windows-specific signals, and
    /// returns its value verbatim, so an inherited name wins over anything declared below.
    /// TERM_PROGRAM_VERSION goes with it: a version without its program is worse than neither.
    /// </summary>
    public static IReadOnlyList<string> RemovedVariables { get; } = new[]
    {
        "TERM_PROGRAM",
        "TERM_PROGRAM_VERSION",
    };

    /// <summary>
    /// The environment declared for a pane's child, on top of the inherited one.
    /// <paramref name="paneToken"/> is the surface's stable spawn token (§7 session.status).
    /// </summary>
    public static IReadOnlyDictionary<string, string> PaneEnvironment(string paneToken) =>
        new Dictionary<string, string>
        {
            ["AGTERM_PANE_ID"] = paneToken,
            // What xterm.js actually implements. Left to inheritance it would carry a value
            // describing a different emulator, or be missing entirely under a GUI-launched app.
            ["TERM"] = "xterm-256color",
            // Announces a Windows terminal session. Read by TUIs that gate Windows behaviour on
            // it, including Claude Code, which will only request the kitty keyboard protocol
            // (CSI > 1 u) for a terminal it recognizes — and that request is what makes
            // Shift+Enter arrive as ESC [13;2u instead of a bare CR. A fresh id per pane matches
            // what a real Windows terminal does: one session id per tab.
            ["WT_SESSION"] = Guid.NewGuid().ToString(),
        };
}
