namespace AgTerm.Xterm;

/// <summary>The InfoBar text for theme files that failed to parse. A shipped build has none (a test
/// requires zero errors); this exists so a broken pack update never drops themes silently.</summary>
public static class ThemeCatalogFailureNotice
{
    public const string Title = "Theme files failed to load";

    private const int ListedThemes = 3;

    public static LiveFailureNotice? From(IReadOnlyList<ThemeCatalogError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
            return null;

        var firstErrorPerTheme = errors
            .GroupBy(error => error.Theme, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var message = string.Join("; ", firstErrorPerTheme.Take(ListedThemes).Select(Describe));
        if (firstErrorPerTheme.Length > ListedThemes)
            message += $"; and {firstErrorPerTheme.Length - ListedThemes} more";
        return new LiveFailureNotice(Title, message);
    }

    private static string Describe(ThemeCatalogError error) =>
        error.Line > 0
            ? $"{error.Theme} (line {error.Line}): {error.Message}"
            : $"{error.Theme}: {error.Message}";
}
