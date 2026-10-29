namespace AgTerm.Core.Icons;

/// <summary>
/// A workspace icon that is an image file rather than a Segoe name (Windows-only). Segoe names are
/// letters and digits only, so a value with any other character is a path.
/// </summary>
public static class WorkspaceIconFile
{
    private static readonly string[] Extensions = [".ico", ".png", ".svg"];

    public static bool IsFile(string value) => !value.All(char.IsAsciiLetterOrDigit);

    /// <summary>Why the path cannot be a workspace icon, or null when it can. The path must be
    /// absolute because the app and agtermctl run in different directories.</summary>
    public static string? Error(string path, Func<string, bool> fileExists)
    {
        if (!Path.IsPathFullyQualified(path))
            return $"icon file path must be absolute: {path}";
        if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            return $"unsupported icon file: {path}";
        if (!fileExists(path))
            return $"icon file not found: {path}";
        return null;
    }
}
