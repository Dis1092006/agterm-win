namespace AgTerm.Core.Install;

/// <summary>Host-free helpers for installing the bundled agent skill (<c>agterm</c>) into the skills
/// directories of Claude Code (<c>~/.claude/skills</c>) and Codex (<c>~/.codex/skills</c>). Port of
/// agtermCore SkillInstall.swift (v0.36.0). The copy itself is <see cref="SkillInstaller"/>.</summary>
public static class SkillInstall
{
    /// <summary>The skill's directory name under each agent's <c>skills</c>, also its command name.</summary>
    public const string SkillName = "agterm";

    /// <summary>An HTML comment in the bundled SKILL.md. A reinstall overwrites only a directory whose
    /// SKILL.md carries it, so a user's own skill named <c>agterm</c> is never deleted.</summary>
    public const string Marker = "<!-- agterm-skill -->";

    /// <summary>One install destination: the agent label and the <c>…\skills\agterm</c> directory.</summary>
    public sealed record Target(string Agent, string SkillDirectory);

    /// <summary><c>&lt;home&gt;\&lt;base&gt;\skills\agterm</c>, where <paramref name="base"/> is the dotted
    /// agent directory name (<c>.claude</c> or <c>.codex</c>).</summary>
    public static string SkillDirectory(string home, string @base) => Path.Join(home, @base, "skills", SkillName);

    /// <summary>Each agent whose base directory exists, Claude Code first. When neither exists, Claude
    /// Code's, so the install is never a no-op.</summary>
    public static IReadOnlyList<Target> InstallTargets(string home, bool claudeExists, bool codexExists)
    {
        var targets = new List<Target>();
        if (claudeExists)
            targets.Add(new Target("Claude Code", SkillDirectory(home, ".claude")));
        if (codexExists)
            targets.Add(new Target("Codex", SkillDirectory(home, ".codex")));
        if (targets.Count == 0)
            targets.Add(new Target("Claude Code", SkillDirectory(home, ".claude")));
        return targets;
    }

    /// <summary>An absent destination is always writable. A present one only when its SKILL.md was read
    /// and contains <see cref="Marker"/>; an absent, unreadable or unmarked SKILL.md means the user wrote it.</summary>
    public static bool MayOverwrite(bool directoryExists, string? existingSkill)
    {
        if (!directoryExists)
            return true;
        return existingSkill is not null && existingSkill.Contains(Marker, StringComparison.Ordinal);
    }
}
