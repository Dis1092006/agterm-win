using AgTerm.Core.Install;

namespace AgTerm.Core.Tests.Install;

public sealed class InstallerCommandsTests
{
    [Fact]
    public void TitlesAreUpstreamWordingWithEllipsisCharacter()
    {
        Assert.Equal("Install Command Line Tool\u2026", InstallerCommands.InstallCommandLineTool);
        Assert.Equal("Install Agent Skill\u2026", InstallerCommands.InstallAgentSkill);
    }

    [Fact]
    public void PaletteIdsAreAppScoped()
    {
        Assert.Equal("app:install-command-line-tool", InstallerCommands.InstallCommandLineToolId);
        Assert.Equal("app:install-agent-skill", InstallerCommands.InstallAgentSkillId);
    }
}
