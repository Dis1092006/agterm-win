using System.Diagnostics;
using System.Reflection;

namespace AgTerm.Core.Protocol;

/// <summary>
/// Reads the build identity the version command reports. The running identity comes from an
/// assembly's informational version; the installed one comes from the product version of the
/// executable on disk, read again on every call so an app replaced after launch shows up.
/// </summary>
public static class AppIdentity
{
    private const int ShortCommitLength = 7;

    /// <summary>Splits "1.2.3+abcdef1234" into the version and a short commit. A missing,
    /// empty or "unknown" revision leaves the commit out; an empty version is unreadable.</summary>
    public static ControlAppIdentity? Parse(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return null;
        var plus = informationalVersion.IndexOf('+');
        var version = (plus < 0 ? informationalVersion : informationalVersion[..plus]).Trim();
        if (version.Length == 0) return null;

        string? commit = null;
        if (plus >= 0)
        {
            var revision = informationalVersion[(plus + 1)..].Trim();
            if (revision.Length > 0 && !revision.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                commit = revision.Length > ShortCommitLength ? revision[..ShortCommitLength] : revision;
        }
        return new ControlAppIdentity { Version = version, Commit = commit };
    }

    public static ControlAppIdentity Running(Assembly assembly) =>
        Parse(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? new ControlAppIdentity { Version = assembly.GetName().Version?.ToString(3) ?? "unknown" };

    /// <summary>The identity of the file at <paramref name="path"/>, or null when there is no path,
    /// the file is gone or it carries no version.</summary>
    public static ControlAppIdentity? Installed(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            return Parse(FileVersionInfo.GetVersionInfo(path).ProductVersion);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The version result: installed rides along only when it differs from the running
    /// build, so a plain reply stays short and a stale running app is the exception.</summary>
    public static ControlResult Result(ControlAppIdentity running, ControlAppIdentity? installed) =>
        new() { App = running, Installed = installed is not null && installed != running ? installed : null };
}
