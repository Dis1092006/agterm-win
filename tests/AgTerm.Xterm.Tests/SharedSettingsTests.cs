using AgTerm.App;
using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using System.Text.Json;

namespace AgTerm.Xterm.Tests;

public sealed class SharedSettingsTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("agterm-shared-settings-").FullName;
    private SettingsStore Store => new(directory);
    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public async Task Reload_coordinator_exposes_awaitable_ui_application_and_propagates_errors()
    {
        Action? queued = null;
        using var coordinator = new SettingsReloadCoordinator(
            _ => Task.FromResult(new AppSettings()), action => queued = action,
            _ => Task.FromException(new IOException("renderer failed")));
        var applying = coordinator.ApplyAsync(new AppSettings { FontSize = 19 });
        Assert.False(applying.IsCompleted);
        Assert.NotNull(queued);
        queued();
        var error = await Assert.ThrowsAsync<IOException>(() => applying);
        Assert.Equal("renderer failed", error.Message);
    }

    [Fact]
    public async Task Disposing_an_awaited_ui_apply_prevents_the_queued_callback_from_touching_the_window()
    {
        Action? queued = null;
        var calls = 0;
        var coordinator = new SettingsReloadCoordinator(
            _ => Task.FromResult(new AppSettings()), action => queued = action,
            _ => { calls++; return Task.CompletedTask; });
        var applying = coordinator.ApplyAsync(new AppSettings());
        coordinator.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => applying);
        Assert.NotNull(queued);
        queued();
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Applying_after_disposal_cancels_without_accessing_the_window()
    {
        var coordinator = new SettingsReloadCoordinator(
            _ => Task.FromResult(new AppSettings()), _ => Assert.Fail("Disposed window was queued."),
            _ => Task.CompletedTask);
        coordinator.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.ApplyAsync(new AppSettings()));
    }

    [Fact]
    public async Task Disposing_a_queued_reload_prevents_the_callback_from_touching_the_window()
    {
        var queued = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var coordinator = new SettingsReloadCoordinator(
            _ => Task.FromResult(new AppSettings()), action => queued.TrySetResult(action),
            _ => { calls++; return Task.CompletedTask; });
        coordinator.Request();
        var callback = await queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.Dispose();
        callback();
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Applying_a_snapshot_swaps_keymap_directory_and_reconciles_theme_command_slots()
    {
        var config = Path.Combine(directory, "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "keymap.conf"), "map new_session ctrl+shift+9\nnot a binding");
        using var host = CreateHost();
        var reloads = 0;
        host.Host.Actions.KeymapReloaded += _ => reloads++;
        Store.Save(new AppSettings
        {
            ConfigDirectory = config, Theme = "Builtin Light", DarkTheme = "Dracula", FollowSystemAppearance = true,
        });

        await host.ApplySettingsAsync(Store.ReadStrict());

        var listed = host.Host.Actions.ListKeymap().Result!.Keymap!;
        Assert.Equal(Path.Combine(config, "keymap.conf"), listed.Path);
        Assert.NotEmpty(listed.Diagnostics);
        Assert.Equal(1, reloads);
        var theme = host.Host.Actions.ListThemes().Result!;
        Assert.True(theme.Sync);
        Assert.Equal("Builtin Light", theme.Light);
        Assert.Equal("Dracula", theme.Dark);
        await host.ApplySettingsAsync(Store.ReadStrict());
        Assert.Equal(1, reloads);

        Store.Save(new AppSettings());
        await host.ApplySettingsAsync(Store.ReadStrict());
        Assert.Equal(Path.Combine(directory, "keymap.conf"), host.Host.Actions.ListKeymap().Result!.Keymap!.Path);
        Assert.Equal(2, reloads);
        Assert.False(host.Host.Actions.ListThemes().Result!.Sync);
    }

    [Fact]
    public async Task Theme_commands_replay_their_intent_over_latest_disk_settings_in_order()
    {
        var coordinator = new SettingsCoordinator(Store);
        using var host = CreateHost();
        using var subscription = await coordinator.SubscribeAsync((snapshot, _) => host.ApplySettingsAsync(snapshot));
        var pending = new List<Task>();
        host.ThemeUpdateRequested += change => pending.Add(coordinator.UpdateCurrentAsync(
            settings => LiveThemeSettings.ApplyChange(settings, change), SettingsApplyKind.Theme));
        File.WriteAllText(Store.FilePath, """
            {"theme":"Builtin Light","darkTheme":"Dracula","followSystemAppearance":true,
             "fontSize":23,"toolbarMode":"future","future":{"keep":true}}
            """);

        host.Host.RunLocked(() =>
        {
            Assert.True(host.Host.Actions.SetTheme(new ControlArgs { Name = "agterm" }).Ok);
            Assert.True(host.Host.Actions.SetTheme(new ControlArgs { Dark = "none" }).Ok);
            Assert.True(host.Host.Actions.SetTheme(new ControlArgs { Name = "Dracula" }).Ok);
        });
        await Task.WhenAll(pending);

        var final = Store.ReadStrict().Settings;
        Assert.Equal("Dracula", final.Theme);
        Assert.Null(final.DarkTheme);
        Assert.False(final.FollowSystemAppearance);
        Assert.Equal(23, final.FontSize);
        Assert.Equal("future", final.ToolbarMode);
        Assert.Contains("\"future\":{\"keep\":true}", File.ReadAllText(Store.FilePath));
        Assert.Equal("Dracula", host.Host.Actions.ListThemes().Result!.Theme);
    }

    [Fact]
    public async Task A_failed_theme_save_restores_command_readback_to_the_last_committed_snapshot()
    {
        var coordinator = new SettingsCoordinator(Store);
        using var host = CreateHost();
        using var subscription = await coordinator.SubscribeAsync((snapshot, _) => host.ApplySettingsAsync(snapshot));
        Task? pending = null;
        host.ThemeUpdateRequested += change => pending = coordinator.UpdateCurrentAsync(
            settings => LiveThemeSettings.ApplyChange(settings, change), SettingsApplyKind.Theme);
        File.WriteAllText(Store.FilePath, "{broken");

        Assert.True(host.Host.Actions.SetTheme(new ControlArgs { Name = "Dracula" }).Ok);
        Assert.NotNull(pending);
        await Assert.ThrowsAnyAsync<JsonException>(() => pending);

        Assert.Equal("agterm", host.Host.Actions.ListThemes().Result!.Theme);
        Assert.Equal("{broken", File.ReadAllText(Store.FilePath));
        Assert.NotNull(coordinator.State.Error);
    }

    [Fact]
    public async Task Application_settings_fan_out_to_two_hosts_without_restarting_surfaces()
    {
        using var application = new LiveApplicationHost(new LiveApplicationHostOptions
        {
            StateDir = directory, HomeDirectory = directory, PipeName = "agterm-settings-" + Guid.NewGuid().ToString("N"),
        });
        using var first = CreateHost();
        using var second = CreateHost();
        var firstSurface = first.Store.ActiveSession!.Surface;
        var secondSurface = second.Store.ActiveSession!.Surface;
        using var a = await application.Settings.SubscribeAsync((snapshot, _) => first.ApplySettingsAsync(snapshot));
        using var b = await application.Settings.SubscribeAsync((snapshot, _) => second.ApplySettingsAsync(snapshot));
        await application.Settings.UpdateAsync(Store.ReadStrict(),
            settings => settings with { Theme = "Dracula", NotificationsEnabled = false, RestoreRunningCommand = true });
        Assert.Equal("Dracula", first.Host.Actions.ListThemes().Result!.Theme);
        Assert.Equal("Dracula", second.Host.Actions.ListThemes().Result!.Theme);
        Assert.Same(firstSurface, first.Store.ActiveSession!.Surface);
        Assert.Same(secondSurface, second.Store.ActiveSession!.Surface);
        Assert.Contains("is off", first.Host.Actions.SetSessionRestore(
            first.Store.ActiveSession!.Id.ToString("D"), null,
            new ControlSessionRestoreUpdate(new ControlRestoreOverride.Pin("echo restored"))).Result!.Text!);
    }

    [Fact]
    public async Task Config_reload_queues_one_shared_apply_and_preserves_its_wire_reply()
    {
        using var application = new LiveApplicationHost(new LiveApplicationHostOptions
        {
            StateDir = directory, HomeDirectory = directory, PipeName = "agterm-reload-" + Guid.NewGuid().ToString("N"),
        });
        var first = new TaskCompletionSource<AppSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<AppSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var a = await application.Settings.SubscribeAsync((snapshot, kind) =>
        {
            if (kind == SettingsApplyKind.Reload) first.TrySetResult(snapshot.Settings);
            return Task.CompletedTask;
        });
        using var b = await application.Settings.SubscribeAsync((snapshot, kind) =>
        {
            if (kind == SettingsApplyKind.Reload) second.TrySetResult(snapshot.Settings);
            return Task.CompletedTask;
        });
        Store.Save(new AppSettings { FontSize = 22 });
        application.Start();
        var response = await Task.Run(() => new ControlPipeClient(application.PipeName)
            .Send(new ControlRequest { Cmd = Command.ConfigReload }));
        Assert.True(response.Ok, response.Error);
        Assert.Equal(0, response.Result!.Count);
        Assert.Equal(22, (await first.Task.WaitAsync(TimeSpan.FromSeconds(5))).FontSize);
        Assert.Equal(22, (await second.Task.WaitAsync(TimeSpan.FromSeconds(5))).FontSize);
    }

    [Fact]
    public void App_wires_awaited_shared_application_errors_and_subscription_cleanup()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AgTerm.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root.FullName, "src", "AgTerm.App", "MainWindow.xaml.cs"));
        Assert.Contains("_applicationHost.Settings.SubscribeAsync(", source, StringComparison.Ordinal);
        Assert.Contains("await host.ApplySettingsAsync(snapshot)", source, StringComparison.Ordinal);
        Assert.Contains("_settingsReloadCoordinator.RunOnUiAsync(", source, StringComparison.Ordinal);
        Assert.Contains("_settingsSubscription?.Dispose();", source, StringComparison.Ordinal);
        Assert.Contains("_applicationHost.Settings.Changed -= OnSettingsChanged;", source, StringComparison.Ordinal);
        Assert.Contains("host.ThemeUpdateRequested +=", source, StringComparison.Ordinal);
        Assert.Contains("SettingsApplyKind.Startup or SettingsApplyKind.Theme", source, StringComparison.Ordinal);
        Assert.Contains("await ArmHotkeysAsync(host);", source, StringComparison.Ordinal);
        Assert.Contains("await Volatile.Read(ref _keymapReloadApplication);", source, StringComparison.Ordinal);
        Assert.Contains("Volatile.Write(ref _keymapReloadApplication, application);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_page_initialization_keeps_host_readiness_and_retries_the_settings_subscription_after_recovery()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AgTerm.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root.FullName, "src", "AgTerm.App", "MainWindow.xaml.cs"));
        var startup = source[source.IndexOf("private async Task StartHostAsync()", StringComparison.Ordinal)..];
        var ready = startup.IndexOf("_hostReady.TrySetResult(host);", StringComparison.Ordinal);
        var page = startup.IndexOf("await SubscribeToSettingsAsync(host);", StringComparison.Ordinal);
        Assert.True(ready >= 0 && ready < page, "The application pipe must remain available when the page fails.");
        var recovery = source[source.IndexOf("private async Task ReloadTerminalViewAsync()", StringComparison.Ordinal)..
            source.IndexOf("private async Task StartHostAsync()", StringComparison.Ordinal)];
        Assert.Contains("if (_settingsSubscription is null)", recovery, StringComparison.Ordinal);
        Assert.Contains("await SubscribeToSettingsAsync(host);", recovery, StringComparison.Ordinal);
    }

    private LiveControlHost CreateHost() => new(new LiveControlHostOptions
    {
        StateDir = directory,
        HomeDirectory = directory,
        PipeName = "agterm-settings-host-" + Guid.NewGuid().ToString("N"),
        AvailableThemes = ["agterm", "Builtin Light", "Dracula"],
        OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
    });

    private sealed class FakeSurface : IHeadlessSurface
    {
        public string? PaneToken => "settings-" + GetHashCode();
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
