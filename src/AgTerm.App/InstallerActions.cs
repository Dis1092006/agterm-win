using AgTerm.Core.Install;
using AgTerm.Xterm;

namespace AgTerm.App;

/// <summary>The Help menu installers with the app's real inputs: the <c>agtermctl.exe</c> next to the
/// app, and the skill files embedded in the build.</summary>
public static class InstallerActions
{
    public static InstallOutcome InstallCommandLineTool(string? processPath, IUserPathEnvironment environment)
    {
        var directory = Path.GetDirectoryName(processPath);
        var toolExists = !string.IsNullOrEmpty(directory) && File.Exists(Path.Join(directory, CliInstall.ToolFileName));
        return CliInstall.Install(directory ?? string.Empty, toolExists, environment);
    }

    public static InstallOutcome InstallAgentSkill(string home) =>
        SkillInstaller.Run(home, SkillBundle.Files, new PhysicalSkillFileSystem());

    public static InstallOutcome InstallAgentStatusHooks(string home, string? processPath)
    {
        var directory = Path.GetDirectoryName(processPath);
        if (string.IsNullOrWhiteSpace(directory))
            return new InstallOutcome("Install Failed", "agtermctl is not bundled in this build.", true);
        var tool = Path.Join(directory, CliInstall.ToolFileName);
        if (!File.Exists(tool))
            return new InstallOutcome("Install Failed", "agtermctl is not bundled in this build.", true);
        try
        {
            return AgentHooksInstaller.Run(home, AgentStatusBundle.Read(AgentHooksInstaller.WrapperName),
                AgentStatusBundle.Read(AgentHooksInstaller.PiName), tool);
        }
        catch (IOException ex)
        {
            return new InstallOutcome("Install Failed", ex.Message, true);
        }
    }
}
