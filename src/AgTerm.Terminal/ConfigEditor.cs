namespace AgTerm.Terminal;

/// <summary>
/// The editor command for a config file (upstream ConfigPaths.editorCommand): %VISUAL%, then
/// %EDITOR%, then Microsoft Edit (edit.exe) when it is on PATH, then notepad.exe. The command
/// names the file only, so the caller runs it in the file's directory and the line needs no
/// quotes.
/// </summary>
public static class ConfigEditor
{
    public static string Command(string fileName, Func<string, string?> which, Func<string, string?> env)
    {
        var editor = NonBlank(env("VISUAL"))
            ?? NonBlank(env("EDITOR"))
            ?? (which("edit.exe") is not null ? "edit.exe" : "notepad.exe");
        return $"{editor} {fileName}";
    }

    /// <summary>The production probe pair: PATH search + process environment.</summary>
    public static string Command(string fileName) =>
        Command(fileName, ShellResolver.WhichOnPath, Environment.GetEnvironmentVariable);

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
