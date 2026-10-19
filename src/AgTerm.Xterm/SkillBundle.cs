using AgTerm.Core.Install;

namespace AgTerm.Xterm;

/// <summary>The agent skill files, embedded from <c>plugins/agterm/skills/agterm/</c> at build time.
/// Embedded resources survive single-file and packaged builds, where loose files can go missing.</summary>
public static class SkillBundle
{
    private const string ResourcePrefix = "skill/";

    private static readonly Lazy<IReadOnlyList<SkillFile>> LazyFiles = new(Load);

    /// <summary>Each file with its path relative to the skill directory, <c>/</c>-separated.</summary>
    public static IReadOnlyList<SkillFile> Files => LazyFiles.Value;

    private static IReadOnlyList<SkillFile> Load()
    {
        var assembly = typeof(SkillBundle).Assembly;
        var files = new List<SkillFile>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            // %(RecursiveDir) carries the Windows separator into the logical name
            var name = resource.Replace('\\', '/');
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                continue;
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"skill resource {resource} could not be opened");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            files.Add(new SkillFile(name[ResourcePrefix.Length..], buffer.ToArray()));
        }
        return files;
    }
}
