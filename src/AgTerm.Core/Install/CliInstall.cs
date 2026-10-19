namespace AgTerm.Core.Install;

/// <summary>The user <c>Path</c> as stored: the raw value with <c>%VARIABLES%</c> unexpanded, and whether it
/// is an expandable string (<c>REG_EXPAND_SZ</c>) or a plain one (<c>REG_SZ</c>).</summary>
public sealed record UserPathValue(string Value, bool Expandable);

/// <summary>Where the command line tool install reads and writes <c>Path</c>: the stored user value, the
/// change broadcast, and this process's own <c>PATH</c>, which new panes inherit.</summary>
public interface IUserPathEnvironment
{
    /// <summary>The stored user value, or null when there is none.</summary>
    UserPathValue? ReadUserPath();

    void WriteUserPath(UserPathValue value);

    /// <summary>Tells running applications that the environment changed.</summary>
    void BroadcastChange();

    string? ReadProcessPath();

    void WriteProcessPath(string value);

    /// <summary>The value with its <c>%VARIABLES%</c> expanded. Used only to compare entries; the stored value
    /// is written back raw.</summary>
    string ExpandVariables(string value);
}

/// <summary>Puts the folder of <c>agtermctl.exe</c> on the user <c>Path</c>. The Windows form of upstream
/// CLIInstaller.swift, which links the tool into <c>/usr/local/bin</c>.</summary>
public static class CliInstall
{
    public const string ToolName = "agtermctl";
    public const string ToolFileName = ToolName + ".exe";

    public static InstallOutcome Install(string toolDirectory, bool toolExists, IUserPathEnvironment environment)
    {
        if (!toolExists)
            return new InstallOutcome("Install Failed", $"{ToolName} is not bundled in this build.", IsWarning: true);
        try
        {
            var stored = environment.ReadUserPath();
            var added = !UserPath.Contains(stored?.Value, toolDirectory, environment.ExpandVariables);
            if (added)
            {
                // a new value is expandable, like the Path that Windows creates
                var value = new UserPathValue(UserPath.Append(stored?.Value, toolDirectory, environment.ExpandVariables), stored?.Expandable ?? true);
                environment.WriteUserPath(value);
                environment.BroadcastChange();
            }
            var processPath = environment.ReadProcessPath();
            if (!UserPath.Contains(processPath, toolDirectory, environment.ExpandVariables))
                environment.WriteProcessPath(UserPath.Append(processPath, toolDirectory));

            var text = added
                ? $"{ToolName} was added to your user Path: {toolDirectory}. Terminals that are already open, "
                  + "including open agterm-win panes, do not see the change. "
                : $"{ToolName} is already on your user Path: {toolDirectory}. ";
            return new InstallOutcome(
                "Command Line Tool Installed",
                text + $"Open a new terminal and run “{ToolName} --help”.",
                IsWarning: false);
        }
        catch (Exception ex)
        {
            return new InstallOutcome("Install Failed", ex.Message, IsWarning: true);
        }
    }
}
