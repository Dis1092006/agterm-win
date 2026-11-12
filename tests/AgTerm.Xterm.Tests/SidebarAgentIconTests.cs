using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

// Windows-only: a session row with a running agent CLI in its focused pane draws an agent icon
// before its name, strips Claude's leading OSC star while that icon is shown, and hides when the
// agentIcons interface element is hidden.
public sealed class SidebarAgentIconTests
{
    [Theory]
    [InlineData(SessionAgent.Claude, "Claude Code")]
    [InlineData(SessionAgent.Copilot, "Copilot CLI")]
    [InlineData(SessionAgent.Codex, "Codex")]
    public void Each_agent_shows_on_a_session_row_with_its_own_path_and_tooltip(SessionAgent agent, string expectedTip)
    {
        Assert.True(SidebarAgentIcon.IsShown(SidebarRowKind.Session, agent, new AppSettings()));
        Assert.False(SidebarAgentIcon.IsShown(SidebarRowKind.Workspace, agent, new AppSettings()));
        Assert.NotEmpty(SidebarAgentIcon.PathData(agent));
        Assert.Equal(expectedTip, SidebarAgentIcon.ToolTip(agent));
    }

    [Fact]
    public void Distinct_geometries_distinguish_all_three_agents_and_null_draws_nothing()
    {
        var paths = new[]
        {
            SidebarAgentIcon.PathData(SessionAgent.Claude),
            SidebarAgentIcon.PathData(SessionAgent.Copilot),
            SidebarAgentIcon.PathData(SessionAgent.Codex),
        };
        Assert.Equal(3, paths.Distinct(StringComparer.Ordinal).Count());
        // x:Bind converts the string to Path.Data, and an empty string fails that conversion
        // with E_INVALIDARG and crashes the app on its first session row without an agent
        Assert.Equal("M0,0", SidebarAgentIcon.PathData(null));
        Assert.Null(SidebarAgentIcon.ToolTip(null));
        Assert.False(SidebarAgentIcon.IsShown(SidebarRowKind.Session, null, new AppSettings()));
    }

    [Fact]
    public void Hiding_agent_icons_hides_only_the_agent_icon()
    {
        var hidden = new AppSettings { HiddenInterfaceElements = ["agentIcons"] };
        Assert.False(SidebarAgentIcon.IsShown(SidebarRowKind.Session, SessionAgent.Copilot, hidden));
        Assert.True(SidebarRowIcon.IsShown(SidebarRowKind.Session, hidden));
        Assert.True(SidebarSessionFlag.IsShown(SidebarRowKind.Session, flagged: true, hidden));

        var sessionIconsHidden = new AppSettings { HiddenInterfaceElements = ["sessionIcons"] };
        Assert.True(SidebarAgentIcon.IsShown(SidebarRowKind.Session, SessionAgent.Copilot, sessionIconsHidden));
    }

    [Theory]
    [InlineData("✳ Claude Code", "Claude Code")]
    [InlineData("✳\uFE0F  Fix auth bug", "Fix auth bug")]
    [InlineData("✳\uFE0E Refactor", "Refactor")]
    [InlineData("✳", "✳")]
    [InlineData("✳   ", "✳   ")]
    [InlineData("⠋ Working...", "⠋ Working...")]
    [InlineData("Plain title", "Plain title")]
    public void Leading_claude_star_is_stripped_only_when_text_follows_it(string raw, string expected) =>
        Assert.Equal(expected, SidebarAgentIcon.StripLeadingClaudeStar(raw));

    [Fact]
    public void Format_label_strips_claudes_star_only_while_the_claude_icon_is_shown_on_an_auto_named_row()
    {
        var row = new SidebarRow(SidebarRowKind.Session, Guid.NewGuid(), "✳ Fix login")
        {
            Agent = SessionAgent.Claude,
            HasCustomName = false,
        };

        Assert.Equal("Fix login",
            SidebarAgentIcon.FormatLabel(row, SidebarMode.Tree, FlaggedViewLayout.Flat, new AppSettings()));

        // Hiding agentIcons restores the raw label so Claude's own star comes back.
        Assert.Equal("✳ Fix login",
            SidebarAgentIcon.FormatLabel(row, SidebarMode.Tree, FlaggedViewLayout.Flat,
                new AppSettings { HiddenInterfaceElements = ["agentIcons"] }));

        // A user's custom name that starts with ✳ is kept verbatim.
        var custom = row with { HasCustomName = true };
        Assert.Equal("✳ Fix login",
            SidebarAgentIcon.FormatLabel(custom, SidebarMode.Tree, FlaggedViewLayout.Flat, new AppSettings()));

        // Other agents do not strip ✳.
        var copilot = row with { Agent = SessionAgent.Copilot };
        Assert.Equal("✳ Fix login",
            SidebarAgentIcon.FormatLabel(copilot, SidebarMode.Tree, FlaggedViewLayout.Flat, new AppSettings()));

        // Flat flagged layout strips the session name part while preserving " : workspace".
        var flatFlagged = row with { Label = "✳ Fix login : backend" };
        Assert.Equal("Fix login : backend",
            SidebarAgentIcon.FormatLabel(flatFlagged, SidebarMode.Flagged, FlaggedViewLayout.Flat, new AppSettings()));

        var bareStarFlagged = row with { Label = "✳ : backend" };
        Assert.Equal("✳ : backend",
            SidebarAgentIcon.FormatLabel(bareStarFlagged, SidebarMode.Flagged, FlaggedViewLayout.Flat, new AppSettings()));
    }
}
