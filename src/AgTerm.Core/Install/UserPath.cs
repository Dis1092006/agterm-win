namespace AgTerm.Core.Install;

/// <summary>Edits a <c>;</c>-separated <c>Path</c> value as it is stored, without expanding
/// <c>%VARIABLES%</c> in it. Entries compare case-insensitively and ignore trailing <c>\</c> or <c>/</c>; empty
/// entries never match. With an <c>expand</c> function, both sides are expanded for the compare only, so
/// <c>%LOCALAPPDATA%\x</c> matches the folder it names. Entries that are not the given directory stay exactly
/// as they were.</summary>
public static class UserPath
{
    private const char Separator = ';';

    public static bool Contains(string? value, string directory, Func<string, string>? expand = null)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        var wanted = Normalize(directory, expand);
        return value.Split(Separator).Any(entry => Matches(entry, wanted, expand));
    }

    /// <summary>The value with <paramref name="directory"/> added at the end, or the same value when it is
    /// already there.</summary>
    public static string Append(string? value, string directory, Func<string, string>? expand = null)
    {
        if (string.IsNullOrEmpty(value))
            return directory;
        if (Contains(value, directory, expand))
            return value;
        return value.EndsWith(Separator) ? value + directory : value + Separator + directory;
    }

    /// <summary>The value without any entry that matches <paramref name="directory"/>, or the same value
    /// when none does.</summary>
    public static string Remove(string value, string directory, Func<string, string>? expand = null)
    {
        if (!Contains(value, directory, expand))
            return value;
        var wanted = Normalize(directory, expand);
        var kept = value.Split(Separator).Where(entry => !Matches(entry, wanted, expand));
        return string.Join(Separator, kept);
    }

    private static string Normalize(string entry, Func<string, string>? expand) =>
        (expand is null ? entry : expand(entry)).TrimEnd('\\', '/');

    private static bool Matches(string entry, string normalizedDirectory, Func<string, string>? expand)
    {
        var normalized = Normalize(entry, expand);
        return normalized.Length > 0 && string.Equals(normalized, normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }
}
