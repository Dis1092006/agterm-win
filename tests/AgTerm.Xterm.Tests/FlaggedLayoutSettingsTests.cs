using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using System.Text.Json;

namespace AgTerm.Xterm.Tests;

/// <summary>The flagged view layout is an app-wide setting (upstream 902cd853 + f8c3eba3): the settings
/// fan-out sets it on every window's store, and sidebar.flagged-layout writes it through the shared
/// settings coordinator.</summary>
public sealed class FlaggedLayoutSettingsTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("agterm-flagged-layout-").FullName;
    private SettingsStore Store => new(directory);
    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public async Task The_layout_applies_at_startup_and_on_reload()
    {
        using var host = CreateHost(new AppSettings { FlaggedViewLayout = "tree" });
        Assert.Equal("tree", Layout(host));

        Store.Save(new AppSettings { FlaggedViewLayout = "plain" });
        await host.ApplySettingsAsync(Store.ReadStrict());
        Assert.Equal("plain", Layout(host));

        Store.Save(new AppSettings { FlaggedViewLayout = "outline" });
        await host.ApplySettingsAsync(Store.ReadStrict());
        Assert.Equal("flat", Layout(host));
    }

    [Fact]
    public async Task The_command_persists_a_change_and_every_window_reads_it_back()
    {
        var coordinator = new SettingsCoordinator(Store);
        using var first = CreateHost(new AppSettings());
        using var second = CreateHost(new AppSettings());
        using var a = await coordinator.SubscribeAsync((snapshot, _) => first.ApplySettingsAsync(snapshot));
        using var b = await coordinator.SubscribeAsync((snapshot, _) => second.ApplySettingsAsync(snapshot));
        var pending = new List<Task>();
        first.FlaggedLayoutChangeRequested += layout => pending.Add(coordinator.UpdateCurrentAsync(
            settings => layout.WriteTo(settings), SettingsApplyKind.Command));
        File.WriteAllText(Store.FilePath, """{"fontSize":23,"future":{"keep":true}}""");

        ControlResponse toggled = null!;
        first.Host.RunLocked(() => toggled = first.Host.Actions.SetFlaggedViewLayout(ControlFlaggedLayoutMode.Toggle));
        Assert.Equal("tree", toggled.Result!.Text);
        await Task.WhenAll(pending);
        Assert.Equal("tree", Store.ReadStrict().Settings.FlaggedViewLayout);
        Assert.Equal(23, Store.ReadStrict().Settings.FontSize);
        Assert.Contains("\"future\":{\"keep\":true}", File.ReadAllText(Store.FilePath));
        Assert.Equal("tree", Layout(first));
        Assert.Equal("tree", Layout(second));

        first.Host.RunLocked(() => { _ = first.Host.Actions.SetFlaggedViewLayout(ControlFlaggedLayoutMode.Tree); });
        _ = Assert.Single(pending);

        first.Host.RunLocked(() => { _ = first.Host.Actions.SetFlaggedViewLayout(ControlFlaggedLayoutMode.Flat); });
        await Task.WhenAll(pending);
        Assert.DoesNotContain("flaggedViewLayout", File.ReadAllText(Store.FilePath));
        Assert.Equal("flat", Layout(second));
    }

    [Fact]
    public async Task A_failed_save_restores_the_readback_to_the_last_committed_layout()
    {
        var coordinator = new SettingsCoordinator(Store);
        using var host = CreateHost(new AppSettings());
        using var subscription = await coordinator.SubscribeAsync((snapshot, _) => host.ApplySettingsAsync(snapshot));
        Task? pending = null;
        host.FlaggedLayoutChangeRequested += layout => pending = coordinator.UpdateCurrentAsync(
            settings => layout.WriteTo(settings), SettingsApplyKind.Command);
        File.WriteAllText(Store.FilePath, "{broken");

        ControlResponse response = null!;
        host.Host.RunLocked(() => response = host.Host.Actions.SetFlaggedViewLayout(ControlFlaggedLayoutMode.Tree));
        Assert.Equal("tree", response.Result!.Text);
        Assert.NotNull(pending);
        await Assert.ThrowsAnyAsync<JsonException>(() => pending);

        Assert.Equal("flat", Layout(host));
        Assert.Equal("{broken", File.ReadAllText(Store.FilePath));
    }

    [Fact]
    public void App_persists_the_layout_through_the_shared_settings_without_resetting_pane_zoom()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AgTerm.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root.FullName, "src", "AgTerm.App", "MainWindow.xaml.cs"));
        Assert.Contains("host.FlaggedLayoutChangeRequested +=", source, StringComparison.Ordinal);
        Assert.Contains("settings => layout.WriteTo(settings), SettingsApplyKind.Command", source, StringComparison.Ordinal);
        Assert.Contains("kind is SettingsApplyKind.Startup or SettingsApplyKind.Theme or SettingsApplyKind.Command",
            source, StringComparison.Ordinal);
    }

    private static string? Layout(LiveControlHost host)
    {
        string? layout = null;
        host.Host.RunLocked(() => layout = host.Store.ControlTree().SidebarFlaggedLayout);
        return layout;
    }

    private LiveControlHost CreateHost(AppSettings settings)
    {
        var state = Directory.CreateDirectory(Path.Combine(directory, "state-" + Guid.NewGuid().ToString("N"))).FullName;
        return new(new LiveControlHostOptions
        {
            StateDir = state,
            ConfigDir = state,
            PipeName = "agterm-flagged-layout-" + Guid.NewGuid().ToString("N"),
            NotificationSettings = settings,
            OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
        });
    }

    private sealed class FakeSurface : IHeadlessSurface
    {
        public string? PaneToken { get; } = Guid.NewGuid().ToString();
        public bool Inject(string text) => true;
        public string? ReadScreenText(bool all, int? lines) => "";
        public string? SelectedText => null;
        public void SelectAll() { }
        public bool Paste() => true;
        public bool PerformBindingAction(string action) => true;
        public void PromoteToPrimaryPane() { }
        public void Teardown() { }
    }
}
