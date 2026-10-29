using System.Text.Json;
using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public sealed class SettingsCoordinatorTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("agterm-settings-apply-").FullName;
    private SettingsStore Store => new(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public async Task Application_policy_is_awaited_without_counting_as_a_window_subscriber()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new SettingsCoordinator(Store, async (snapshot, kind) =>
        {
            Assert.Equal(SettingsApplyKind.Edit, kind);
            Assert.True(snapshot.Settings.AutoHideSidebarInactiveWindows);
            entered.SetResult();
            await release.Task;
        });
        Assert.False(coordinator.HasSubscribers);

        var saving = coordinator.UpdateCurrentAsync(settings =>
            settings with { AutoHideSidebarInactiveWindows = true });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(saving.IsCompleted);
        release.SetResult();
        await saving;
        Assert.False(coordinator.HasSubscribers);
    }

    [Fact]
    public async Task Application_policy_failure_is_reported_but_window_application_still_runs()
    {
        var fail = true;
        var coordinator = new SettingsCoordinator(Store, (_, _) =>
            fail ? Task.FromException(new IOException("application policy failed")) : Task.CompletedTask);
        var calls = 0;
        using var subscription = await coordinator.SubscribeAsync((_, _) =>
        {
            calls++;
            return Task.CompletedTask;
        });

        var error = await Assert.ThrowsAsync<SettingsApplyException>(() =>
            coordinator.UpdateCurrentAsync(settings => settings with { AutoHideSidebarInactiveWindows = true }));

        Assert.Equal("application policy failed", Assert.Single(error.InnerExceptions).Message);
        Assert.Same(error, coordinator.State.Error);
        Assert.True(Store.ReadStrict().Settings.AutoHideSidebarInactiveWindows);
        Assert.Equal(2, calls);
        fail = false;
        await coordinator.ReloadAsync();
        Assert.Null(coordinator.State.Error);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Edit_is_persisted_then_applied_to_every_subscriber_and_is_observable()
    {
        var coordinator = new SettingsCoordinator(Store);
        var first = new List<AppSettings>();
        var second = new List<AppSettings>();
        var states = new List<SettingsApplicationState>();
        using var a = await coordinator.SubscribeAsync((snapshot, _) =>
        {
            first.Add(snapshot.Settings);
            Assert.Equal(Store.Load(), snapshot.Settings);
            return Task.CompletedTask;
        });
        using var b = await coordinator.SubscribeAsync((snapshot, _) =>
        {
            second.Add(snapshot.Settings);
            return Task.CompletedTask;
        });
        coordinator.Changed += states.Add;
        var saved = await coordinator.UpdateAsync(Store.ReadStrict(), settings => settings with { FontSize = 19 });
        Assert.Equal(19, first.Last().FontSize);
        Assert.Equal(19, second.Last().FontSize);
        Assert.Same(saved, coordinator.State.Snapshot);
        Assert.Equal([true, false], states.Select(state => state.IsApplying));
        Assert.Null(coordinator.State.Error);
    }

    [Fact]
    public async Task A_new_window_receives_the_latest_settings_and_unsubscribe_stops_updates()
    {
        var coordinator = new SettingsCoordinator(Store);
        await coordinator.UpdateAsync(Store.ReadStrict(), settings => settings with { FontSize = 19 });
        var seen = new List<AppSettings>();
        var subscription = await coordinator.SubscribeAsync((snapshot, _) =>
        {
            seen.Add(snapshot.Settings);
            return Task.CompletedTask;
        });
        Assert.Equal(19, Assert.Single(seen).FontSize);
        subscription.Dispose();
        await coordinator.UpdateAsync(Store.ReadStrict(), settings => settings with { FontSize = 20 });
        Assert.Single(seen);
    }

    [Fact]
    public async Task Apply_failure_keeps_the_saved_snapshot_attempts_other_windows_and_can_be_retried()
    {
        var coordinator = new SettingsCoordinator(Store);
        var fail = false;
        var secondCalls = 0;
        using var a = await coordinator.SubscribeAsync((_, _) =>
            fail ? Task.FromException(new IOException("renderer unavailable")) : Task.CompletedTask);
        using var b = await coordinator.SubscribeAsync((_, _) =>
        {
            secondCalls++;
            return Task.CompletedTask;
        });
        fail = true;
        var error = await Assert.ThrowsAsync<SettingsApplyException>(() =>
            coordinator.UpdateAsync(Store.ReadStrict(), settings => settings with { FontSize = 21 }));
        Assert.Equal(21, error.Snapshot.Settings.FontSize);
        Assert.Equal(21, Store.ReadStrict().Settings.FontSize);
        Assert.Equal(2, secondCalls);
        Assert.Same(error, coordinator.State.Error);
        fail = false;
        await coordinator.ReloadAsync();
        Assert.Null(coordinator.State.Error);
        Assert.Equal(3, secondCalls);
    }

    [Fact]
    public async Task A_corrupt_reload_keeps_the_previous_applied_state_and_does_not_invoke_windows()
    {
        var coordinator = new SettingsCoordinator(Store);
        var calls = 0;
        using var subscription = await coordinator.SubscribeAsync((_, _) =>
        {
            calls++;
            return Task.CompletedTask;
        });
        File.WriteAllText(Store.FilePath, "{broken");
        await Assert.ThrowsAnyAsync<JsonException>(() => coordinator.ReloadAsync());
        Assert.Equal(1, calls);
        Assert.Equal("agterm", coordinator.State.Snapshot.Settings.Theme);
        Assert.NotNull(coordinator.State.Error);
        Assert.Equal("{broken", File.ReadAllText(Store.FilePath));
    }

    [Fact]
    public async Task A_conflict_does_not_apply_or_replace_the_last_successful_snapshot()
    {
        Store.Save(new AppSettings { FontSize = 14 });
        var coordinator = new SettingsCoordinator(Store);
        var baseline = Store.ReadStrict();
        File.WriteAllText(Store.FilePath, """{"fontSize":20}""");
        await Assert.ThrowsAsync<SettingsConflictException>(() =>
            coordinator.UpdateAsync(baseline, settings => settings with { FontSize = 18 }));
        Assert.Equal(14, coordinator.State.Snapshot.Settings.FontSize);
        Assert.IsType<SettingsConflictException>(coordinator.State.Error);
        Assert.Equal(20, Store.Load().FontSize);
    }

    [Fact]
    public async Task Updates_wait_for_the_previous_all_window_apply()
    {
        var coordinator = new SettingsCoordinator(Store);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = await coordinator.SubscribeAsync(async (_, kind) =>
        {
            if (kind == SettingsApplyKind.Startup) return;
            entered.TrySetResult();
            await release.Task;
        });
        var baseline = Store.ReadStrict();
        var first = coordinator.UpdateAsync(baseline, settings => settings with { FontSize = 18 });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = coordinator.UpdateAsync(baseline, settings => settings with { SidebarFontSize = 16 });
        Assert.False(second.IsCompleted);
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(18, coordinator.State.Snapshot.Settings.FontSize);
        Assert.Equal(16, coordinator.State.Snapshot.Settings.SidebarFontSize);
    }

    [Fact]
    public async Task A_window_closed_during_initial_application_is_not_left_subscribed()
    {
        var coordinator = new SettingsCoordinator(Store);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.SubscribeAsync((_, _) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        }, cancellation.Token));
        Assert.False(coordinator.HasSubscribers);
    }
}
