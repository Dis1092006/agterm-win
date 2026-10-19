using AgTerm.Core.Install;

namespace AgTerm.Core.Tests.Install;

public sealed class SkillInstallerTests
{
    private const string Home = @"C:\h";
    private static readonly string ClaudeBase = @"C:\h\.claude";
    private static readonly string CodexBase = @"C:\h\.codex";
    private static readonly string ClaudeSkills = @"C:\h\.claude\skills";
    private static readonly string CodexSkills = @"C:\h\.codex\skills";
    private static readonly string ClaudeDest = @"C:\h\.claude\skills\agterm";
    private static readonly string CodexDest = @"C:\h\.codex\skills\agterm";

    private static readonly SkillFile[] Files =
    {
        new("SKILL.md", "---\n---\n<!-- agterm-skill -->\n"u8.ToArray()),
        new("scripts/show-image.sh", "#!/bin/sh\n"u8.ToArray()),
    };

    private const string UseHint =
        "The agent uses it automatically when you ask it to control agterm. It drives the app via the agtermctl CLI, so install that too if you haven't: Help ▸ Install Command Line Tool…";

    private const string FreshHint =
        "A skills directory was created for the first time — restart any running agent session so it picks up the new skill.";

    /// <summary>A file system in memory. <see cref="Present"/> answers with links followed;
    /// <see cref="Entries"/> answers without, so a dangling link is an entry but not present.</summary>
    private sealed class FakeFileSystem : ISkillInstallFileSystem
    {
        public HashSet<string> Present { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Texts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Log { get; } = new();
        public Dictionary<string, byte[]> Written { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? FailWritesUnder { get; set; }

        public void AddDirectory(string path)
        {
            Present.Add(path);
            Entries.Add(path);
        }

        public bool Exists(string path) => Present.Contains(path);
        public bool EntryExists(string path) => Entries.Contains(path);
        public string? ReadText(string path) => Texts.TryGetValue(path, out var text) ? text : null;

        public void CreateDirectory(string path)
        {
            Log.Add($"mkdir {path}");
            AddDirectory(path);
        }

        public void Remove(string path)
        {
            Log.Add($"rm {path}");
            Present.Remove(path);
            Entries.Remove(path);
        }

        public void WriteFile(string path, byte[] contents)
        {
            if (FailWritesUnder is not null && path.StartsWith(FailWritesUnder, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException($"Access to the path '{path}' is denied.");
            Log.Add($"write {path}");
            Written[path] = contents;
        }
    }

    [Fact]
    public void EmptyFileSetIsNotBundled()
    {
        var fs = new FakeFileSystem();
        var outcome = SkillInstaller.Run(Home, Array.Empty<SkillFile>(), fs);
        Assert.Equal(new InstallOutcome("Install Failed", "The agent skill is not bundled in this build.", IsWarning: true), outcome);
        Assert.Empty(fs.Log);
    }

    [Fact]
    public void NeitherAgentPresentInstallsForClaudeAndReportsFreshSkillsDirectory()
    {
        var fs = new FakeFileSystem();
        var report = SkillInstaller.Install(Home, Files, fs);

        Assert.Equal(new[] { new SkillInstallReport.Entry("Claude Code", ClaudeDest) }, report.Installed);
        Assert.Empty(report.Skipped);
        Assert.True(report.FreshSkillsDirectory);
        Assert.Equal(
            new[]
            {
                $"mkdir {ClaudeSkills}",
                $"rm {ClaudeDest}",
                $@"write {ClaudeDest}\SKILL.md",
                $@"write {ClaudeDest}\scripts\show-image.sh",
            },
            fs.Log);
        Assert.Equal(Files[1].Contents, fs.Written[$@"{ClaudeDest}\scripts\show-image.sh"]);

        var outcome = report.ToOutcome();
        Assert.Equal("Agent Skill Installed", outcome.Title);
        Assert.False(outcome.IsWarning);
        Assert.Equal(
            string.Join("\n", "Installed for:", $"  • Claude Code: {ClaudeDest}", "", UseHint, "", FreshHint),
            outcome.Message);
    }

    [Fact]
    public void BothAgentsPresentInstallsForBothWithoutFreshHint()
    {
        var fs = new FakeFileSystem();
        foreach (var dir in new[] { ClaudeBase, CodexBase, ClaudeSkills, CodexSkills })
            fs.AddDirectory(dir);

        var report = SkillInstaller.Install(Home, Files, fs);

        Assert.Equal(
            new[] { new SkillInstallReport.Entry("Claude Code", ClaudeDest), new SkillInstallReport.Entry("Codex", CodexDest) },
            report.Installed);
        Assert.False(report.FreshSkillsDirectory);
        var outcome = report.ToOutcome();
        Assert.False(outcome.IsWarning);
        Assert.Equal(
            string.Join("\n", "Installed for:", $"  • Claude Code: {ClaudeDest}", $"  • Codex: {CodexDest}", "", UseHint),
            outcome.Message);
    }

    [Fact]
    public void CodexOnlyInstallsForCodex()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(CodexBase);
        var report = SkillInstaller.Install(Home, Files, fs);
        Assert.Equal(new[] { new SkillInstallReport.Entry("Codex", CodexDest) }, report.Installed);
        Assert.True(report.FreshSkillsDirectory);
    }

    [Fact]
    public void MarkedDestinationIsReplaced()
    {
        var fs = new FakeFileSystem();
        foreach (var dir in new[] { ClaudeBase, ClaudeSkills, ClaudeDest })
            fs.AddDirectory(dir);
        fs.Texts[$@"{ClaudeDest}\SKILL.md"] = $"old\n{SkillInstall.Marker}\n";

        var report = SkillInstaller.Install(Home, Files, fs);

        Assert.Single(report.Installed);
        Assert.Contains($"rm {ClaudeDest}", fs.Log);
        Assert.Contains($@"write {ClaudeDest}\SKILL.md", fs.Log);
    }

    [Fact]
    public void UnmarkedDestinationIsSkippedAndUntouched()
    {
        var fs = new FakeFileSystem();
        foreach (var dir in new[] { ClaudeBase, ClaudeSkills, ClaudeDest })
            fs.AddDirectory(dir);
        fs.Texts[$@"{ClaudeDest}\SKILL.md"] = "---\ndescription: mine\n---\n";

        var report = SkillInstaller.Install(Home, Files, fs);

        Assert.Empty(report.Installed);
        Assert.Equal(
            new[] { new SkillInstallReport.Entry("Claude Code", "a different 'agterm' skill is already there — left untouched") },
            report.Skipped);
        Assert.Empty(fs.Log);
        var outcome = report.ToOutcome();
        Assert.Equal("Skill Not Installed", outcome.Title);
        Assert.True(outcome.IsWarning);
        Assert.Equal(
            string.Join("\n", "The skill was not installed.", "", "Skipped:",
                "  • Claude Code: a different 'agterm' skill is already there — left untouched"),
            outcome.Message);
    }

    [Fact]
    public void DanglingLinkAtDestinationCountsAsPresentAndIsNotRemoved()
    {
        var fs = new FakeFileSystem();
        foreach (var dir in new[] { ClaudeBase, ClaudeSkills })
            fs.AddDirectory(dir);
        fs.Entries.Add(ClaudeDest); // an entry that does not resolve: a dangling link

        var report = SkillInstaller.Install(Home, Files, fs);

        Assert.Empty(report.Installed);
        Assert.Single(report.Skipped);
        Assert.Empty(fs.Log);
    }

    [Fact]
    public void WriteErrorSkipsThatTargetWithTheMessageAndKeepsTheOther()
    {
        var fs = new FakeFileSystem { FailWritesUnder = CodexDest };
        foreach (var dir in new[] { ClaudeBase, CodexBase, ClaudeSkills, CodexSkills })
            fs.AddDirectory(dir);

        var report = SkillInstaller.Install(Home, Files, fs);

        Assert.Equal(new[] { new SkillInstallReport.Entry("Claude Code", ClaudeDest) }, report.Installed);
        var reason = $@"Access to the path '{CodexDest}\SKILL.md' is denied.";
        Assert.Equal(new[] { new SkillInstallReport.Entry("Codex", reason) }, report.Skipped);
        var outcome = report.ToOutcome();
        Assert.Equal("Agent Skill Installed", outcome.Title);
        Assert.True(outcome.IsWarning);
        Assert.Equal(
            string.Join("\n", "Installed for:", $"  • Claude Code: {ClaudeDest}", "", UseHint, "", "Skipped:", $"  • Codex: {reason}"),
            outcome.Message);
    }

    [Fact]
    public void RunCatchesUnexpectedErrorsAsInstallFailed()
    {
        var outcome = SkillInstaller.Run(Home, Files, new ThrowingFileSystem());
        Assert.Equal(new InstallOutcome("Install Failed", "boom", IsWarning: true), outcome);
    }

    private sealed class ThrowingFileSystem : ISkillInstallFileSystem
    {
        public bool Exists(string path) => throw new InvalidOperationException("boom");
        public bool EntryExists(string path) => throw new InvalidOperationException("boom");
        public string? ReadText(string path) => throw new InvalidOperationException("boom");
        public void CreateDirectory(string path) => throw new InvalidOperationException("boom");
        public void Remove(string path) => throw new InvalidOperationException("boom");
        public void WriteFile(string path, byte[] contents) => throw new InvalidOperationException("boom");
    }
}
