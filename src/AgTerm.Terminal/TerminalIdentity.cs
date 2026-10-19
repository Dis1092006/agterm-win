namespace AgTerm.Terminal;

/// <summary>
/// What a spawned shell is told about the terminal around it. Upstream needs none of this: agterm
/// runs on Ghostty, which identifies itself to its own children. Here the children are spawned by
/// a WinUI app, so without a declaration they inherit whatever launched agterm-win (an IDE's
/// terminal, another shell) and TUIs then negotiate against the wrong terminal.
///
/// The name declared here is chosen from the short list Claude Code trusts for extended keys, not
/// from what this port resembles: see PLATFORM-NOTES, "The terminal name is an allowlist key".
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
        // A version without its program is worse than neither: TERM_PROGRAM is declared below, and
        // an inherited version string would be read as that program's.
        "TERM_PROGRAM_VERSION",
        // Launching agterm-win from Windows Terminal would otherwise leak its session id into the
        // child. terminal=windows-terminal is the one name that also demands a CSI ? u reply,
        // which ConPTY never delivers, so it costs the kitty protocol instead of granting it.
        "WT_SESSION",
    };

    /// <summary>
    /// The terminal identity declared for a pane's child, on top of the inherited one. The AGTERM_*
    /// set, the pane token included, comes from the host (SurfaceEnvironment).
    /// </summary>
    public static IReadOnlyDictionary<string, string> PaneEnvironment() =>
        new Dictionary<string, string>
        {
            // What xterm.js actually implements. Left to inheritance it would carry a value
            // describing a different emulator, or be missing entirely under a GUI-launched app.
            ["TERM"] = "xterm-256color",
            // Read before every other signal and returned verbatim, so this is THE terminal name.
            // Claude Code grants extended keys from the environment only to names on its own short
            // list (iTerm.app, kitty, WezTerm, ghostty, tmux, windows-terminal, WarpTerminal) and
            // pushes the kitty keyboard protocol on the same test. Leaving it unset does not mean
            // "no name": detection falls through to TERM, and "xterm-256color" is not on the list.
            // ghostty is the entry this port can carry: windows-terminal additionally requires a
            // CSI ? u reply that ConPTY swallows, and the rest are gated on a version string.
            // The claim is narrow by design, and the protocol stays opt-in over the wire.
            ["TERM_PROGRAM"] = "ghostty",
        };
}
