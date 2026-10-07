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
    public void Pane_environment_announces_a_windows_terminal_session()
    {
        // WT_SESSION is how a Windows terminal is recognized by TUIs that special-case it;
        // Claude Code reads it to decide it may push the kitty keyboard protocol, which is what
        // makes Shift+Enter distinguishable from Enter.
        var env = TerminalIdentity.PaneEnvironment("tok");
        Assert.True(Guid.TryParse(env["WT_SESSION"], out var id), $"not a GUID: {env["WT_SESSION"]}");
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public void Each_pane_gets_its_own_session_id()
    {
        Assert.NotEqual(
            TerminalIdentity.PaneEnvironment("a")["WT_SESSION"],
            TerminalIdentity.PaneEnvironment("b")["WT_SESSION"]);
    }

    [Fact]
    public void Term_is_declared_and_does_not_inherit()
    {
        // xterm.js is an xterm, and a 256-color TERM is what it actually implements; inheriting
        // the launcher's value (a bare "cygwin", or nothing at all) misdescribes it
        Assert.Equal("xterm-256color", TerminalIdentity.PaneEnvironment("tok")["TERM"]);
    }

    [Fact]
    public void Inherited_terminal_identification_is_removed()
    {
        // TERM_PROGRAM is checked BEFORE WT_SESSION by the detection order Claude Code uses and
        // is returned verbatim, so an inherited "zed" or "vscode" would win and the protocol
        // would never be requested. Overwriting is not enough — it must be absent.
        Assert.Contains("TERM_PROGRAM", TerminalIdentity.RemovedVariables);
        Assert.Contains("TERM_PROGRAM_VERSION", TerminalIdentity.RemovedVariables);
        Assert.DoesNotContain("TERM_PROGRAM", TerminalIdentity.PaneEnvironment("tok").Keys);
    }

    [Fact]
    public void Removed_variables_and_declared_variables_do_not_overlap()
    {
        var declared = TerminalIdentity.PaneEnvironment("tok").Keys;
        Assert.Empty(declared.Intersect(TerminalIdentity.RemovedVariables));
    }
}
