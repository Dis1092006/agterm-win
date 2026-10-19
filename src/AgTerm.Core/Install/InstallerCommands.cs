namespace AgTerm.Core.Install;

/// <summary>The two installer commands of the Help menu and the command palette. The titles are
/// upstream's menu wording and the single source for the menu, the palette and the skill text.
/// The ids are app-scoped palette ids; they are not <c>BuiltinAction</c>s.</summary>
public static class InstallerCommands
{
    public const string InstallCommandLineTool = "Install Command Line Tool\u2026";
    public const string InstallAgentSkill = "Install Agent Skill\u2026";

    public const string InstallCommandLineToolId = "app:install-command-line-tool";
    public const string InstallAgentSkillId = "app:install-agent-skill";
}
