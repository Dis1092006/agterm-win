namespace AgTerm.Core.Protocol;

/// <summary>
/// The text of the About dialog. Upstream opens the standard macOS About panel: the version with
/// the build commit in parentheses, a link to agterm.com and the copyright. The port shows the
/// same and adds a line that names agterm, since this app is a port of it.
/// </summary>
public static class AboutPanel
{
    public const string Name = "agterm-win";
    public const string UpstreamText = "Windows port of agterm";
    public const string UpstreamUrl = "https://agterm.com";
    public const string RepositoryText = "github.com/Dis1092006/agterm-win";
    public const string RepositoryUrl = "https://" + RepositoryText;
    public const string Copyright = "© 2026 Dmitry Samsonov";

    public static string VersionLine(ControlAppIdentity identity) =>
        identity.Commit is { } commit ? $"Version {identity.Version} ({commit})" : $"Version {identity.Version}";
}
