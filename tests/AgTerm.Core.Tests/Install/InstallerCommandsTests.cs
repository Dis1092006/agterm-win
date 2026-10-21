using AgTerm.Core.Install;

namespace AgTerm.Core.Tests.Install;

public sealed class InstallerCommandsTests
{
    [Fact]
    public void TitlesAreUpstreamWordingWithEllipsisCharacter()
    {
        Assert.Equal("Install Command Line Tool\u2026", InstallerCommands.InstallCommandLineTool);
        Assert.Equal("Install Agent Skill\u2026", InstallerCommands.InstallAgentSkill);
        Assert.Equal("Install Agent Status Hooks\u2026", InstallerCommands.InstallAgentStatusHooks);
    }

    [Fact]
    public void PaletteIdsAreAppScoped()
    {
        Assert.Equal("app:install-command-line-tool", InstallerCommands.InstallCommandLineToolId);
        Assert.Equal("app:install-agent-skill", InstallerCommands.InstallAgentSkillId);
        Assert.Equal("app:install-agent-status-hooks", InstallerCommands.InstallAgentStatusHooksId);
    }
}
