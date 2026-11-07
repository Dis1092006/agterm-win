using AgTerm.Core.Install;
using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

public sealed class AgentResumeHostTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("agterm-agent-resume-").FullName;
    private string Home => Path.Join(directory, "home");
    private string Bin => Path.Join(directory, "bin");
    private string State => Path.Join(directory, "state");
    private string ClaudeSettings => Path.Join(Home, ".claude", "settings.json");
    private SettingsStore Store => new(State);

    public AgentResumeHostTests()
    {
        Directory.CreateDirectory(Path.Join(Home, ".claude"));
        Directory.CreateDirectory(Bin);
        Directory.CreateDirectory(State);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private void CreateTool() => File.WriteAllText(Path.Join(Bin, CliInstall.ToolFileName), "");

    private LiveApplicationHost CreateApplication() => new(new LiveApplicationHostOptions
    {
        StateDir = State,
        HomeDirectory = Home,
        InstalledExecutablePath = Path.Join(Bin, "agterm.exe"),
        PipeName = "agterm-resume-" + Guid.NewGuid().ToString("N"),
        AgentEnvironment = _ => null,
    });

    private bool ClaudeHasHooks() =>
        File.Exists(ClaudeSettings) && File.ReadAllText(ClaudeSettings).Contains(AgentResumeInstaller.WrapperName);

    [Fact]
    public void The_bundled_wrapper_carries_the_ownership_marker() =>
        Assert.Contains(AgentResumeInstaller.WrapperMarker, AgentStatusBundle.Read(AgentResumeInstaller.WrapperName));

    [Fact]
    public async Task Turning_the_setting_on_and_off_installs_and_removes_the_hooks()
    {
        CreateTool();
        using var application = CreateApplication();
        Assert.Null(application.LastAgentResumeReport);

        await application.Settings.UpdateAsync(Store.ReadStrict(),
            settings => settings with { RestoreRunningCommand = true, ResumeAgentSessions = true });
        Assert.True(ClaudeHasHooks());
        Assert.Contains(application.LastAgentResumeReport!.Agents, agent => agent.State == AgentResumeState.On);

        await application.Settings.UpdateAsync(Store.ReadStrict(), settings => settings with { ResumeAgentSessions = false });
        Assert.False(ClaudeHasHooks());
        Assert.DoesNotContain(application.LastAgentResumeReport!.Agents, agent => agent.State == AgentResumeState.On);
    }

    [Fact]
    public async Task An_unrelated_settings_change_leaves_the_agent_configs_alone()
    {
        CreateTool();
        using var application = CreateApplication();
        await application.Settings.UpdateAsync(Store.ReadStrict(),
            settings => settings with { RestoreRunningCommand = true, ResumeAgentSessions = true });
        File.Delete(ClaudeSettings);

        await application.Settings.UpdateAsync(Store.ReadStrict(), settings => settings with { FontSize = 17 });

        Assert.False(File.Exists(ClaudeSettings));
    }

    [Fact]
    public async Task A_failed_install_is_reported_and_retried_on_the_next_save()
    {
        using var application = CreateApplication();

        var failure = await Assert.ThrowsAsync<SettingsApplyException>(() => application.Settings.UpdateAsync(
            Store.ReadStrict(), settings => settings with { RestoreRunningCommand = true, ResumeAgentSessions = true }));
        Assert.True(application.LastAgentResumeReport!.HasFailure);
        Assert.Contains(application.LastAgentResumeReport.Describe(), failure.ToString());
        Assert.False(ClaudeHasHooks());

        CreateTool();
        await application.Settings.UpdateAsync(Store.ReadStrict(), settings => settings with { FontSize = 17 });
        Assert.True(ClaudeHasHooks());
        Assert.False(application.LastAgentResumeReport!.HasFailure);
    }

    [Fact]
    public async Task Startup_with_the_setting_on_refreshes_the_hooks()
    {
        CreateTool();
        Store.Save(new AppSettings { RestoreRunningCommand = true, ResumeAgentSessions = true });

        using var application = CreateApplication();
        await application.AgentResumeStartup;

        Assert.True(ClaudeHasHooks());
        Assert.NotNull(application.LastAgentResumeReport);
    }

    [Fact]
    public async Task Startup_with_the_setting_off_touches_nothing()
    {
        CreateTool();
        File.WriteAllText(ClaudeSettings, "{}");

        using var application = CreateApplication();
        await application.AgentResumeStartup;

        Assert.Null(application.LastAgentResumeReport);
        Assert.Equal("{}", File.ReadAllText(ClaudeSettings));
    }
}
