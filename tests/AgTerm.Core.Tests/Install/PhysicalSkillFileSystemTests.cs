using System.Diagnostics;
using System.Text;
using AgTerm.Core.Install;

namespace AgTerm.Core.Tests.Install;

/// <summary>Real links on a temp directory. Directory and file symlinks need Developer Mode or an
/// elevated process (CI runs elevated); junctions need neither.</summary>
public sealed class PhysicalSkillFileSystemTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agterm-skill-fs-" + Guid.NewGuid().ToString("N"));
    private readonly PhysicalSkillFileSystem _fs = new();

    public PhysicalSkillFileSystemTests() => Directory.CreateDirectory(_root);

    public void Dispose() => DeleteTree(_root);

    // links are removed with a non-recursive delete, so cleanup never walks into a target
    private void DeleteTree(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) || !Directory.Exists(path))
        {
            _fs.Remove(path);
            return;
        }
        foreach (var entry in Directory.EnumerateFileSystemEntries(path).ToList())
            DeleteTree(entry);
        Directory.Delete(path);
    }

    private string P(params string[] parts) => Path.Join(new[] { _root }.Concat(parts).ToArray());

    private static void Junction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private string MakeSkill(string dir, string skillText)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Join(dir, "SKILL.md"), skillText);
        return dir;
    }

    [Fact]
    public void ExistsFollowsLinks()
    {
        var target = Directory.CreateDirectory(P("target")).FullName;
        Directory.CreateSymbolicLink(P("link"), target);
        Directory.CreateSymbolicLink(P("dangling"), P("missing"));
        File.WriteAllText(P("file.txt"), "x");

        Assert.True(_fs.Exists(target));
        Assert.True(_fs.Exists(P("link")));
        Assert.True(_fs.Exists(P("file.txt")));
        Assert.False(_fs.Exists(P("dangling")));
        Assert.False(_fs.Exists(P("absent")));
    }

    [Fact]
    public void EntryExistsCountsDanglingLinks()
    {
        Directory.CreateSymbolicLink(P("dangling-dir"), P("missing-dir"));
        File.CreateSymbolicLink(P("dangling-file"), P("missing-file"));
        Directory.CreateDirectory(P("gone"));
        Junction(P("dangling-junction"), P("gone"));
        Directory.Delete(P("gone"));

        Assert.True(_fs.EntryExists(P("dangling-dir")));
        Assert.True(_fs.EntryExists(P("dangling-file")));
        Assert.True(_fs.EntryExists(P("dangling-junction")));
        Assert.False(_fs.EntryExists(P("absent")));
    }

    [Fact]
    public void ReadTextFollowsLinksAndReturnsNullWhenUnreadable()
    {
        MakeSkill(P("skill"), "hello");
        Directory.CreateSymbolicLink(P("link"), P("skill"));

        Assert.Equal("hello", _fs.ReadText(P("link", "SKILL.md")));
        Assert.Null(_fs.ReadText(P("absent", "SKILL.md")));
        Assert.Null(_fs.ReadText(P("skill"))); // a directory cannot be read as text
    }

    [Fact]
    public void RemoveDeletesALinkAndNeverItsTarget()
    {
        MakeSkill(P("target-a"), "a");
        MakeSkill(P("target-b"), "b");
        File.WriteAllText(P("target-file"), "f");
        Directory.CreateSymbolicLink(P("dir-link"), P("target-a"));
        Junction(P("junction"), P("target-b"));
        File.CreateSymbolicLink(P("file-link"), P("target-file"));
        Directory.CreateSymbolicLink(P("dangling"), P("missing"));

        _fs.Remove(P("dir-link"));
        _fs.Remove(P("junction"));
        _fs.Remove(P("file-link"));
        _fs.Remove(P("dangling"));

        foreach (var link in new[] { "dir-link", "junction", "file-link", "dangling" })
            Assert.False(_fs.EntryExists(P(link)), link);
        Assert.Equal("a", File.ReadAllText(P("target-a", "SKILL.md")));
        Assert.Equal("b", File.ReadAllText(P("target-b", "SKILL.md")));
        Assert.Equal("f", File.ReadAllText(P("target-file")));
    }

    [Fact]
    public void RemoveDeletesARealDirectoryRecursivelyAndIgnoresAnAbsentPath()
    {
        MakeSkill(P("real"), "x");
        Directory.CreateDirectory(P("real", "scripts"));
        File.WriteAllText(P("real", "scripts", "a.sh"), "y");

        _fs.Remove(P("real"));
        _fs.Remove(P("absent"));

        Assert.False(Directory.Exists(P("real")));
    }

    [Fact]
    public void WriteFileCreatesParentDirectories()
    {
        _fs.WriteFile(P("a", "b", "c.txt"), Encoding.UTF8.GetBytes("z"));
        Assert.Equal("z", File.ReadAllText(P("a", "b", "c.txt")));
    }

    // the install through the real file system, with links at the destination

    private static readonly SkillFile[] Files =
    {
        new("SKILL.md", Encoding.UTF8.GetBytes($"new\n{SkillInstall.Marker}\n")),
        new("scripts/show-image.sh", Encoding.UTF8.GetBytes("#!/bin/sh\n")),
    };

    [Fact]
    public void LinkToAMarkedSkillIsReplacedAndItsTargetIsUntouched()
    {
        var home = Directory.CreateDirectory(P("home")).FullName;
        Directory.CreateDirectory(P("home", ".claude", "skills"));
        var elsewhere = MakeSkill(P("elsewhere"), $"old\n{SkillInstall.Marker}\n");
        var destination = P("home", ".claude", "skills", "agterm");
        Directory.CreateSymbolicLink(destination, elsewhere);

        var report = SkillInstaller.Install(home, Files, _fs);

        Assert.Equal(new[] { new SkillInstallReport.Entry("Claude Code", destination) }, report.Installed);
        Assert.Null(new DirectoryInfo(destination).LinkTarget);
        Assert.Equal(Files[0].Contents, File.ReadAllBytes(Path.Join(destination, "SKILL.md")));
        Assert.True(File.Exists(Path.Join(destination, "scripts", "show-image.sh")));
        Assert.Equal($"old\n{SkillInstall.Marker}\n", File.ReadAllText(Path.Join(elsewhere, "SKILL.md")));
        Assert.False(File.Exists(Path.Join(elsewhere, "scripts", "show-image.sh")));
    }

    [Fact]
    public void LinkToAForeignSkillIsSkippedAndUntouched()
    {
        var home = Directory.CreateDirectory(P("home")).FullName;
        Directory.CreateDirectory(P("home", ".claude", "skills"));
        var elsewhere = MakeSkill(P("mine"), "my own agterm skill\n");
        var destination = P("home", ".claude", "skills", "agterm");
        Junction(destination, elsewhere);

        var report = SkillInstaller.Install(home, Files, _fs);

        Assert.Empty(report.Installed);
        Assert.Single(report.Skipped);
        Assert.NotNull(new DirectoryInfo(destination).LinkTarget);
        Assert.Equal("my own agterm skill\n", File.ReadAllText(Path.Join(elsewhere, "SKILL.md")));
    }

    [Fact]
    public void DanglingLinkAtTheDestinationIsSkippedAndKept()
    {
        var home = Directory.CreateDirectory(P("home")).FullName;
        Directory.CreateDirectory(P("home", ".codex", "skills"));
        var destination = P("home", ".codex", "skills", "agterm");
        Directory.CreateSymbolicLink(destination, P("missing"));

        var report = SkillInstaller.Install(home, Files, _fs);

        Assert.Empty(report.Installed);
        Assert.Equal(
            new[] { new SkillInstallReport.Entry("Codex", "a different 'agterm' skill is already there — left untouched") },
            report.Skipped);
        Assert.True(_fs.EntryExists(destination));
        Assert.Equal(P("missing"), new DirectoryInfo(destination).LinkTarget);
    }

    [Fact]
    public void FreshHomeGetsClaudeSkillDirectory()
    {
        var home = Directory.CreateDirectory(P("home")).FullName;

        var report = SkillInstaller.Install(home, Files, _fs);

        Assert.True(report.FreshSkillsDirectory);
        Assert.True(File.Exists(P("home", ".claude", "skills", "agterm", "scripts", "show-image.sh")));
        Assert.False(Directory.Exists(P("home", ".codex")));
    }
}
