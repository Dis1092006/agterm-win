using AgTerm.Core.Install;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported from agtermCore SkillInstallTests.swift (v0.36.0). The bundled-text tests are covered by
/// AgTerm.Cli.Tests SkillTextTests; the plugin and marketplace manifest tests belong to step 4.
/// Paths use the Windows separator: SkillDirectory joins with Path.Join.</summary>
public sealed class SkillInstallTests
{
    [Fact]
    public void SkillDirectoryComposesUnderAgentBase()
    {
        Assert.Equal(@"C:\Users\x\.claude\skills\agterm", SkillInstall.SkillDirectory(@"C:\Users\x", ".claude"));
        Assert.Equal(@"C:\Users\x\.codex\skills\agterm", SkillInstall.SkillDirectory(@"C:\Users\x", ".codex"));
        Assert.Equal("agterm", SkillInstall.SkillName);
    }

    [Fact]
    public void MarkerIsTheHtmlComment()
    {
        Assert.Equal("<!-- agterm-skill -->", SkillInstall.Marker);
    }

    [Fact]
    public void TargetsCoverBothWhenBothExist()
    {
        var t = SkillInstall.InstallTargets(@"C:\h", claudeExists: true, codexExists: true);
        Assert.Equal(
            new[]
            {
                new SkillInstall.Target("Claude Code", @"C:\h\.claude\skills\agterm"),
                new SkillInstall.Target("Codex", @"C:\h\.codex\skills\agterm"),
            },
            t);
    }

    [Fact]
    public void TargetsClaudeOnlyWhenOnlyClaudeExists()
    {
        var t = SkillInstall.InstallTargets(@"C:\h", claudeExists: true, codexExists: false);
        Assert.Equal(new[] { new SkillInstall.Target("Claude Code", @"C:\h\.claude\skills\agterm") }, t);
    }

    [Fact]
    public void TargetsCodexOnlyWhenOnlyCodexExists()
    {
        var t = SkillInstall.InstallTargets(@"C:\h", claudeExists: false, codexExists: true);
        Assert.Equal(new[] { new SkillInstall.Target("Codex", @"C:\h\.codex\skills\agterm") }, t);
    }

    [Fact]
    public void TargetsFallBackToClaudeWhenNeitherExists()
    {
        var t = SkillInstall.InstallTargets(@"C:\h", claudeExists: false, codexExists: false);
        Assert.Equal(new[] { new SkillInstall.Target("Claude Code", @"C:\h\.claude\skills\agterm") }, t);
    }

    [Fact]
    public void MayOverwriteWhenDirectoryAbsent()
    {
        Assert.True(SkillInstall.MayOverwrite(directoryExists: false, existingSkill: null));
    }

    [Fact]
    public void RefusesOverwriteOfDirectoryWithoutSkillFile()
    {
        Assert.False(SkillInstall.MayOverwrite(directoryExists: true, existingSkill: null));
    }

    [Fact]
    public void MayOverwriteWhenMarkerPresent()
    {
        var contents = $"---\nx\n---\n{SkillInstall.Marker}\nbody";
        Assert.True(SkillInstall.MayOverwrite(directoryExists: true, existingSkill: contents));
    }

    [Fact]
    public void RefusesOverwriteOfForeignSkill()
    {
        var contents = "---\ndescription: someone else's agterm\n---\nbody";
        Assert.False(SkillInstall.MayOverwrite(directoryExists: true, existingSkill: contents));
    }
}
