namespace AgTerm.Terminal;

/// <summary>The shells this port can inject a cwd-reporting integration into.</summary>
public enum ShellKind
{
    /// <summary>Anything without a known integration recipe: spawned unchanged, no cwd tracking.</summary>
    Unsupported,
    PowerShellCore,
    WindowsPowerShell,
    Cmd,
    Bash,
}

/// <summary>
/// Which spawned shells get a cwd-reporting integration (PLATFORM-NOTES).
///
/// Upstream never writes this: libghostty injects its own shell-integration, and agterm only
/// disables the parts it dislikes (ghostty-defaults.conf `shell-integration-features`). A
/// WebView2/xterm.js surface has no such injector, and no Windows shell volunteers its cwd, so
/// without an integration Session.CurrentCwd is never set and a restored session always reopens
/// at InitialCwd.
///
/// Identification is by executable name because that is what reaches CreateProcessW: the shell
/// may arrive as a bare name (%COMSPEC%), a full path (ShellResolver's PATH probe), or a
/// user-configured string, and all three must resolve to the same recipe.
/// </summary>
public static class ShellIntegration
{
    /// <summary>
    /// Classifies a shell command (bare name or full path) for integration purposes. Returns
    /// Unsupported rather than throwing for anything unrecognized: an unknown shell must keep
    /// spawning exactly as before, only without cwd tracking.
    ///
    /// wsl.exe is deliberately Unsupported, not merely unrecognized — it runs another OS, where a
    /// Windows script path does not exist and a reported cwd would be a /mnt/c path this side
    /// cannot use as a spawn directory.
    /// </summary>
    public static ShellKind Identify(string? shellCommand)
    {
        if (string.IsNullOrWhiteSpace(shellCommand)) return ShellKind.Unsupported;

        // A trailing separator would make GetFileName return "", reading as Unsupported.
        var name = Path.GetFileName(shellCommand.Trim().TrimEnd('\\', '/'));

        return name.ToLowerInvariant() switch
        {
            "pwsh.exe" or "pwsh" => ShellKind.PowerShellCore,
            "powershell.exe" or "powershell" => ShellKind.WindowsPowerShell,
            "cmd.exe" or "cmd" => ShellKind.Cmd,
            "bash.exe" or "bash" => ShellKind.Bash,
            _ => ShellKind.Unsupported,
        };
    }

    /// <summary>The directory name the scripts are copied into, beside the assembly.</summary>
    public const string DirectoryName = "Integration";

    /// <summary>
    /// Locates the shipped integration scripts, or null when they are absent. Null is a supported
    /// state, not an error: a layout without the scripts still spawns working shells, only without
    /// cwd reporting.
    ///
    /// The EXE's own directory is tried before AppContext.BaseDirectory, and the order matters.
    /// For this app's single-file publish (WindowsAppSDKSelfContained + PublishSingleFile) the two
    /// differ: BaseDirectory is the bundle's extraction directory under
    /// %TEMP%\.net\agterm-win\&lt;hash&gt;\, while the ExcludeFromSingleFile scripts stay next to the
    /// exe. Measured — a plain console single-file app reports the two as equal, so this only
    /// reproduces in the real app.
    ///
    /// Existence is checked rather than assumed, so a layout that failed to carry the scripts
    /// degrades to a plain shell instead of handing every shell a -Command that cannot load.
    /// </summary>
    public static string? ResolveScriptDirectory(string? baseDirectory = null)
    {
        if (baseDirectory is not null)
            return DirectoryIn(baseDirectory);

        return DirectoryIn(Path.GetDirectoryName(Environment.ProcessPath))
            ?? DirectoryIn(AppContext.BaseDirectory);
    }

    private static string? DirectoryIn(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;
        var directory = Path.Combine(root, DirectoryName);
        return Directory.Exists(directory) ? directory : null;
    }

    /// <summary>The integration script's file name for each shell, inside the script directory.</summary>
    private static string? ScriptName(ShellKind kind) => kind switch
    {
        ShellKind.PowerShellCore or ShellKind.WindowsPowerShell => "integration.ps1",
        ShellKind.Cmd => "integration.cmd",
        ShellKind.Bash => "integration.bash",
        _ => null,
    };

    /// <summary>
    /// Returns the argv to spawn, with a cwd-reporting integration attached when the argv is a
    /// bare supported shell. Anything else is returned UNCHANGED — the argv is the one thing
    /// LiveControlHost also uses to launch a user's own program, so rewriting the wrong shape
    /// would break restore.
    ///
    /// Left untouched, deliberately:
    /// - a multi-element argv: LiveControlHost puts a captured foreground command, a --command
    ///   argv, or a restore override there INSTEAD of the shell, and it must exec verbatim;
    /// - a shell carrying its own switches: the user's explicit intent, which our switches could
    ///   contradict (pwsh rejects a second -Command);
    /// - an unsupported shell, or a missing script directory.
    ///
    /// Each recipe keeps the shell INTERACTIVE, which is the part that is easy to get wrong:
    /// pwsh without -NoExit would run the script and exit, and bash reads an rcfile only when it
    /// is interactive (-i).
    /// </summary>
    public static IReadOnlyList<string> Inject(
        IReadOnlyList<string> argv,
        string? scriptDirectory)
    {
        if (argv.Count != 1) return argv;
        if (string.IsNullOrWhiteSpace(scriptDirectory)) return argv;

        var shell = argv[0];
        var kind = Identify(shell);
        if (ScriptName(kind) is not { } scriptName) return argv;

        var script = Path.Combine(scriptDirectory.Trim(), scriptName);

        return kind switch
        {
            // Single quotes are PowerShell's literal string: a path with spaces stays one token,
            // and nothing inside it is expanded.
            ShellKind.PowerShellCore or ShellKind.WindowsPowerShell =>
                [shell, "-NoExit", "-Command", $". '{script}'"],

            // /k runs and stays (/c would exit). cmd quotes with doubled quotes, not backslashes.
            ShellKind.Cmd => [shell, "/k", $"call \"{script}\""],

            // --rcfile REPLACES ~/.bashrc, so integration.bash is responsible for sourcing the
            // user's own rc; -i is required for bash to read an rcfile at all.
            ShellKind.Bash => [shell, "--rcfile", script, "-i"],

            _ => argv,
        };
    }
}
