namespace AgTerm.Cli.Tests;

/// <summary>
/// A backslash escape written through a shell or a script (`\b`, `\a`, `\n`) can turn into a control byte
/// and pass review unnoticed, because the byte is invisible. Source and documentation never need one.
/// </summary>
public sealed class SourceTextHygieneTests
{
    private static readonly string[] Roots = ["src", "tests", "docs", "plugins"];
    private static readonly string[] SkippedFolders = ["bin", "obj", "artifacts", "TestResults", ".superpowers"];
    private static readonly string[] Extensions = [".cs", ".md"];

    [Fact]
    public void Tracked_source_and_docs_hold_no_control_characters()
    {
        var repo = SkillTextTests.FindRepoRoot();
        var found = Roots
            .Select(root => Path.Combine(repo, root))
            .Where(Directory.Exists)
            .SelectMany(Files)
            .SelectMany(ControlCharacterLines)
            .ToList();

        Assert.True(found.Count == 0, "control characters in:" + Environment.NewLine + string.Join(Environment.NewLine, found));
    }

    private static IEnumerable<string> Files(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) yield return file;
        }
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (SkippedFolders.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)) continue;
            foreach (var file in Files(child)) yield return file;
        }
    }

    private static IEnumerable<string> ControlCharacterLines(string file)
    {
        var line = 1;
        foreach (var c in File.ReadAllText(file))
        {
            if (c == '\n') line++;
            else if (c < 0x20 && c != '\t' && c != '\r') yield return $"{file}:{line} (0x{(int)c:X2})";
        }
    }
}
