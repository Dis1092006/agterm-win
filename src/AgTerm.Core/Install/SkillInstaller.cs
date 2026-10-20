namespace AgTerm.Core.Install;

/// <summary>One file of the bundled skill. <see cref="RelativePath"/> uses <c>/</c>, relative to the
/// skill directory (for example <c>scripts/show-image.sh</c>).</summary>
public sealed record SkillFile(string RelativePath, byte[] Contents);

/// <summary>A dialog result: title, text, and whether it is a warning.</summary>
public sealed record InstallOutcome(string Title, string Message, bool IsWarning);

/// <summary>The file-system operations the skill install needs, so tests cover the report.</summary>
public interface ISkillInstallFileSystem
{
    /// <summary>Whether a file or directory is at <paramref name="path"/>, following links.</summary>
    bool Exists(string path);

    /// <summary>Whether anything is at <paramref name="path"/> without following the last link, so a
    /// dangling link counts.</summary>
    bool EntryExists(string path);

    /// <summary>The UTF-8 text of a file, or null when it is absent or cannot be read.</summary>
    string? ReadText(string path);

    void CreateDirectory(string path);

    /// <summary>Removes the entry at <paramref name="path"/>: a link itself (never its target), or a real
    /// directory with its contents. Does nothing when the path is absent.</summary>
    void Remove(string path);

    /// <summary>Writes a file, creating its parent directories.</summary>
    void WriteFile(string path, byte[] contents);
}

/// <summary>What one install did: installed targets, skipped targets with the reason, and whether a
/// skills directory was created for the first time.</summary>
public sealed class SkillInstallReport
{
    public sealed record Entry(string Agent, string Detail);

    public List<Entry> Installed { get; } = new();
    public List<Entry> Skipped { get; } = new();
    public bool FreshSkillsDirectory { get; set; }

    /// <summary>The dialog for this report. Title and text are upstream SkillInstaller.swift's, plus one
    /// paragraph about the plugin channel.</summary>
    public InstallOutcome ToOutcome()
    {
        var problem = Installed.Count == 0 || Skipped.Count > 0;
        var title = Installed.Count == 0 ? "Skill Not Installed" : "Agent Skill Installed";
        return new InstallOutcome(title, Message(), problem);
    }

    private string Message()
    {
        var lines = new List<string>();
        if (Installed.Count == 0)
        {
            lines.Add("The skill was not installed.");
        }
        else
        {
            lines.Add("Installed for:");
            foreach (var entry in Installed)
                lines.Add($"  • {entry.Agent}: {entry.Detail}");
            lines.Add("");
            lines.Add("The agent uses it automatically when you ask it to control agterm. It drives the "
                + "app via the agtermctl CLI, so install that too if you haven't: Help ▸ "
                + InstallerCommands.InstallCommandLineTool);
            lines.Add("");
            lines.Add("Use this install or the agterm plugin, not both. With both, the agent loads two copies of "
                + "the skill.");
        }
        if (Skipped.Count > 0)
        {
            lines.Add("");
            lines.Add("Skipped:");
            foreach (var entry in Skipped)
                lines.Add($"  • {entry.Agent}: {entry.Detail}");
        }
        if (FreshSkillsDirectory)
        {
            lines.Add("");
            lines.Add("A skills directory was created for the first time — restart any running agent session "
                + "so it picks up the new skill.");
        }
        return string.Join("\n", lines);
    }
}

/// <summary>Copies the bundled skill into each target from <see cref="SkillInstall.InstallTargets"/>. Port
/// of the copy loop in upstream SkillInstaller.swift, with the file system behind an interface.</summary>
public static class SkillInstaller
{
    public const string NotBundledMessage = "The agent skill is not bundled in this build.";

    /// <summary>Runs the install and returns the dialog to show. Never throws.</summary>
    public static InstallOutcome Run(string home, IReadOnlyList<SkillFile> files, ISkillInstallFileSystem fs)
    {
        if (files.Count == 0)
            return new InstallOutcome("Install Failed", NotBundledMessage, IsWarning: true);
        try
        {
            return Install(home, files, fs).ToOutcome();
        }
        catch (Exception ex)
        {
            return new InstallOutcome("Install Failed", ex.Message, IsWarning: true);
        }
    }

    public static SkillInstallReport Install(string home, IReadOnlyList<SkillFile> files, ISkillInstallFileSystem fs)
    {
        var targets = SkillInstall.InstallTargets(
            home,
            claudeExists: fs.Exists(Path.Join(home, ".claude")),
            codexExists: fs.Exists(Path.Join(home, ".codex")));
        var report = new SkillInstallReport();
        foreach (var target in targets)
        {
            var destination = target.SkillDirectory;
            // link semantics: a link at the destination, dangling or not, counts as present
            var directoryExists = fs.EntryExists(destination);
            var existingSkill = directoryExists ? fs.ReadText(Path.Join(destination, "SKILL.md")) : null;
            if (!SkillInstall.MayOverwrite(directoryExists, existingSkill))
            {
                report.Skipped.Add(new SkillInstallReport.Entry(
                    target.Agent, $"a different '{SkillInstall.SkillName}' skill is already there — left untouched"));
                continue;
            }
            try
            {
                var skillsDirectory = Path.GetDirectoryName(destination)!;
                if (!fs.Exists(skillsDirectory))
                    report.FreshSkillsDirectory = true;
                fs.CreateDirectory(skillsDirectory);
                fs.Remove(destination);
                foreach (var file in files)
                {
                    var relative = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);
                    fs.WriteFile(Path.Join(destination, relative), file.Contents);
                }
                report.Installed.Add(new SkillInstallReport.Entry(target.Agent, destination));
            }
            catch (Exception ex)
            {
                report.Skipped.Add(new SkillInstallReport.Entry(target.Agent, ex.Message));
            }
        }
        return report;
    }
}
