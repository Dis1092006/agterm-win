namespace AgTerm.Core.Install;

/// <summary>Builds the PATH for a custom command without changing the app or pane environment.</summary>
public static class CommandPath
{
    public static string Widened(string? inheritedPath, string? bundledCliDirectory)
    {
        var entries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Add(bundledCliDirectory);
        if (inheritedPath is not null)
        {
            foreach (var entry in inheritedPath.Split(';'))
                Add(entry);
        }

        return string.Join(';', entries);

        void Add(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return;
            if (seen.Add(UserPath.Normalize(directory, null)))
                entries.Add(directory);
        }
    }
}
