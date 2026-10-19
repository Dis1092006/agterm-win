namespace AgTerm.Xterm.Tests;

/// <summary>The installer copies what is embedded, so the embedded set must be the skill directory on
/// disk. A file added to the skill and missed by the build fails here.</summary>
public sealed class SkillBundleTests
{
    [Fact]
    public void The_embedded_files_equal_the_skill_directory_on_disk()
    {
        var root = Path.Combine(FindRepoRoot(), "plugins", "agterm", "skills", "agterm");
        var onDisk = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => (Relative: Path.GetRelativePath(root, path).Replace('\\', '/'), Bytes: File.ReadAllBytes(path)))
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .ToArray();

        var embedded = SkillBundle.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();

        Assert.NotEmpty(onDisk);
        Assert.Equal(onDisk.Select(file => file.Relative), embedded.Select(file => file.RelativePath));
        for (var i = 0; i < onDisk.Length; i++)
            Assert.True(onDisk[i].Bytes.AsSpan().SequenceEqual(embedded[i].Contents), onDisk[i].Relative);
    }

    // the bytes come from the checkout, and core.autocrlf=true turns LF into CRLF there. Claude Code does
    // not read "---\r\n" as frontmatter, so the skill would load without its description.
    [Fact]
    public void The_embedded_files_have_lf_line_endings()
    {
        Assert.NotEmpty(SkillBundle.Files);
        foreach (var file in SkillBundle.Files)
            Assert.False(Array.IndexOf(file.Contents, (byte)'\r') >= 0, $"{file.RelativePath} contains CR");
    }

    [Fact]
    public void The_bundle_carries_the_marker_so_a_reinstall_may_replace_it()
    {
        var skill = Assert.Single(SkillBundle.Files, file => file.RelativePath == "SKILL.md");
        Assert.Contains(AgTerm.Core.Install.SkillInstall.Marker, System.Text.Encoding.UTF8.GetString(skill.Contents));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgTerm.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
