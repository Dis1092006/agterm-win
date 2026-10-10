using AgTerm.Terminal;

namespace AgTerm.Terminal.Tests;

/// <summary>
/// What a spawned shell is told about the terminal it runs in. Upstream inherits Ghostty's own
/// identification; this port spawns from a WinUI app, so whatever launched agterm-win would
/// otherwise leak through and misidentify the terminal.
/// </summary>
public class TerminalIdentityTests
{
    [Fact]
    public void Pane_environment_carries_the_spawn_token()
    {
        var env = TerminalIdentity.PaneEnvironment("tok123");
        Assert.Equal("tok123", env["AGTERM_PANE_ID"]);
    }

    [Fact]
    public void Pane_environment_does_not_claim_to_be_windows_terminal()
    {
        // WT_SESSION was declared to make Claude Code request the kitty keyboard protocol. It does
        // the opposite: for terminal=windows-terminal it demands a CONFIRMED reply to CSI ? u
        // before enabling extended keys, and ConPTY answers terminal queries itself instead of
        // passing them through, so that reply never arrives and Shift+Enter stays a bare CR.
        Assert.DoesNotContain("WT_SESSION", TerminalIdentity.PaneEnvironment("tok").Keys);
    }

    [Fact]
    public void Pane_environment_declares_a_terminal_that_is_trusted_for_extended_keys()
    {
        // Claude Code settles extendedKeys from the environment only for a name on its own short
        // list, and pushes the kitty protocol on the same test. Leaving TERM_PROGRAM unset does
        // not mean "no name": detection falls through to TERM, so the terminal became
        // "xterm-256color", which is not on the list, and extendedKeys went off entirely.
        Assert.Equal("ghostty", TerminalIdentity.PaneEnvironment("tok")["TERM_PROGRAM"]);
    }

    [Fact]
    public void Inherited_windows_terminal_session_is_removed()
    {
        // launching agterm-win from Windows Terminal would otherwise leak its WT_SESSION into the
        // child and reinstate the strict path
        Assert.Contains("WT_SESSION", TerminalIdentity.RemovedVariables);
    }

    [Fact]
    public void Term_is_declared_and_does_not_inherit()
    {
        // xterm.js is an xterm, and a 256-color TERM is what it actually implements; inheriting
        // the launcher's value (a bare "cygwin", or nothing at all) misdescribes it
        Assert.Equal("xterm-256color", TerminalIdentity.PaneEnvironment("tok")["TERM"]);
    }

    [Fact]
    public void Inherited_terminal_identification_is_overridden_and_its_version_removed()
    {
        // TERM_PROGRAM is declared, and an explicit extra of the same name beats what the child
        // would inherit, so an inherited "zed" or "vscode" cannot win. TERM_PROGRAM_VERSION has no
        // declared value to beat it with, so it has to be removed: left alone, the launcher's
        // version would be read as this terminal's.
        Assert.Contains("TERM_PROGRAM_VERSION", TerminalIdentity.RemovedVariables);
        Assert.DoesNotContain("TERM_PROGRAM_VERSION", TerminalIdentity.PaneEnvironment("tok").Keys);
        Assert.DoesNotContain("TERM_PROGRAM", TerminalIdentity.RemovedVariables);
    }

    [Fact]
    public void Removed_variables_and_declared_variables_do_not_overlap()
    {
        var declared = TerminalIdentity.PaneEnvironment("tok").Keys;
        Assert.Empty(declared.Intersect(TerminalIdentity.RemovedVariables));
    }
}
