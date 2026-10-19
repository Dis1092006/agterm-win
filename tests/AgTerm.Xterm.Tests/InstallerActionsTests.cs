using AgTerm.App;
using AgTerm.Core.Install;

namespace AgTerm.Xterm.Tests;

public sealed class InstallerActionsTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), $"agterm-installer-actions-{Guid.NewGuid():N}");

    public InstallerActionsTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void The_command_line_tool_is_the_agtermctl_next_to_the_app()
    {
        File.WriteAllText(Path.Join(_root, CliInstall.ToolFileName), "");
        var environment = new FakePathEnvironment();

        var outcome = InstallerActions.InstallCommandLineTool(Path.Join(_root, "agterm-win.exe"), environment);

        Assert.Equal("Command Line Tool Installed", outcome.Title);
        Assert.Equal(_root, environment.UserPath?.Value);
    }

    [Fact]
    public void An_app_folder_without_agtermctl_reports_that_it_is_not_bundled()
    {
        var environment = new FakePathEnvironment();

        var outcome = InstallerActions.InstallCommandLineTool(Path.Join(_root, "agterm-win.exe"), environment);

        Assert.Equal(("Install Failed", "agtermctl is not bundled in this build."), (outcome.Title, outcome.Message));
        Assert.Null(environment.UserPath);
    }

    [Fact]
    public void An_unknown_process_path_reports_that_the_tool_is_not_bundled()
    {
        var outcome = InstallerActions.InstallCommandLineTool(null, new FakePathEnvironment());

        Assert.Equal(("Install Failed", "agtermctl is not bundled in this build."), (outcome.Title, outcome.Message));
    }

    [Fact]
    public void The_skill_install_copies_the_embedded_skill_under_the_home_directory()
    {
        var outcome = InstallerActions.InstallAgentSkill(_root);

        Assert.Equal("Agent Skill Installed", outcome.Title);
        var installed = Path.Join(SkillInstall.SkillDirectory(_root, ".claude"), "SKILL.md");
        Assert.Contains(SkillInstall.Marker, File.ReadAllText(installed), StringComparison.Ordinal);
        Assert.Equal(
            SkillBundle.Files.Select(file => file.RelativePath).Order(StringComparer.Ordinal),
            Directory.EnumerateFiles(SkillInstall.SkillDirectory(_root, ".claude"), "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(SkillInstall.SkillDirectory(_root, ".claude"), path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal));
    }

    private sealed class FakePathEnvironment : IUserPathEnvironment
    {
        public UserPathValue? UserPath { get; private set; }
        public string? ProcessPath { get; private set; }

        public UserPathValue? ReadUserPath() => UserPath;
        public void WriteUserPath(UserPathValue value) => UserPath = value;
        public void BroadcastChange() { }
        public string? ReadProcessPath() => ProcessPath;
        public void WriteProcessPath(string value) => ProcessPath = value;
        public string ExpandVariables(string value) => value;
    }
}
