using AgTerm.Core.Install;
using AgTerm.Xterm;

namespace AgTerm.App;

/// <summary>The two Help menu installers with the app's real inputs: the <c>agtermctl.exe</c> next to the
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
}
