namespace AgTerm.Terminal;

/// <summary>
/// The default-shell policy (PLATFORM-NOTES: Windows has no login-shell semantics / $SHELL).
/// A configured shell wins verbatim; otherwise PowerShell 7 (pwsh.exe) when found on PATH,
/// falling back to %COMSPEC%, then a literal "cmd.exe".
/// </summary>
public static class ShellResolver
{
    public static string Resolve(string? configured, Func<string, string?> which, Func<string, string?> env)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        return which("pwsh.exe") ?? env("COMSPEC") ?? "cmd.exe";
    }

    /// <summary>The production probe pair: PATH search + process environment.</summary>
    public static string Resolve(string? configured = null) =>
        Resolve(configured, WhichOnPath, Environment.GetEnvironmentVariable);

    /// <summary>Finds an executable on the process PATH (the CreateProcessW search would resolve
    /// a bare name too, but resolving here keeps the picked shell inspectable and testable).</summary>
    internal static string? WhichOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path is null) return null;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
