using AgTerm.ControlServer;
using AgTerm.Core.Model;
using System.Reflection;

namespace AgTerm.Xterm.Tests;

public sealed class AutoFollowRuntimeTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("off", null)]
    [InlineData("s5", 5000)]
    [InlineData("s10", 10000)]
    [InlineData("s30", 30000)]
    [InlineData("s60", 60000)]
    [InlineData("m5", 300000)]
    [InlineData("future", null)]
    [InlineData("S5", null)]
    public void Startup_applies_the_effective_timeout_before_returning(string? mode, int? milliseconds)
    {
        var directory = Directory.CreateTempSubdirectory("agterm-follow-runtime-").FullName;
        try
        {
            using var host = CreateHost(directory, new AppSettings { AutoFollowAttention = mode });
            host.Host.RunLocked(() => Assert.Equal(milliseconds, host.Store.ControlTree().AutoFollowMs));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Settings_reload_applies_and_disabling_cancels_pending_follow()
    {
        var directory = Directory.CreateTempSubdirectory("agterm-follow-reload-").FullName;
        try
        {
            using var host = CreateHost(directory, new AppSettings());
            var settings = new SettingsStore(directory);
            settings.Save(new AppSettings { AutoFollowAttention = "s30", AutoFollowStayOnActive = true });
            await host.ApplySettingsAsync(settings.ReadStrict());
            host.Host.RunLocked(() => Assert.Equal(30000, host.Store.ControlTree().AutoFollowMs));
            var (current, _) = PrepareBlocked(host, directory);
            settings.Save(new AppSettings { AutoFollowAttention = "future" });
            await host.ApplySettingsAsync(settings.ReadStrict());
            Flush(host);
            host.Host.RunLocked(() =>
            {
                Assert.Null(host.Store.ControlTree().AutoFollowMs);
                Assert.Equal(current.Id, host.Store.SelectedSessionId);
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Timer_waits_for_the_host_gate_and_does_not_stamp_its_own_selection_as_activity()
    {
        var directory = Directory.CreateTempSubdirectory("agterm-follow-gate-").FullName;
        try
        {
            using var host = CreateHost(directory, new AppSettings { AutoFollowAttention = "s5" });
            var (current, target) = PrepareBlocked(host, directory);
            using var entered = new ManualResetEventSlim();
            using var completed = new ManualResetEventSlim();
            Task fire = Task.CompletedTask;
            host.Host.RunLocked(() =>
            {
                fire = Task.Run(() => { entered.Set(); Flush(host); completed.Set(); });
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                Assert.False(completed.Wait(TimeSpan.FromMilliseconds(100)));
                Assert.Equal(current.Id, host.Store.SelectedSessionId);
            });
            await fire.WaitAsync(TimeSpan.FromSeconds(5));
            host.Host.RunLocked(() =>
            {
                Assert.Equal(target.Id, host.Store.SelectedSessionId);
                Assert.True(target.AutoFollowConsumed);
                Assert.Null(host.Store.ControlTree().IdleMs);
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Ui_suppression_takes_effect_before_its_queued_store_mutation_and_resume_does_not_rearm()
    {
        var directory = Directory.CreateTempSubdirectory("agterm-follow-modal-").FullName;
        try
        {
            using var host = CreateHost(directory, new AppSettings { AutoFollowAttention = "s5" });
            var (current, target) = PrepareBlocked(host, directory);
            Task suppression = Task.CompletedTask;
            host.Host.RunLocked(() =>
            {
                suppression = host.SetUiAutoFollowSuppressedAsync(true);
                Flush(host);
                Assert.Equal(current.Id, host.Store.SelectedSessionId);
            });
            await suppression;
            await host.SetUiAutoFollowSuppressedAsync(false);
            Flush(host);
            host.Host.RunLocked(() =>
            {
                Assert.Equal(current.Id, host.Store.SelectedSessionId);
                host.Store.NoteUserActivity();
            });
            Flush(host);
            host.Host.RunLocked(() => Assert.Equal(target.Id, host.Store.SelectedSessionId));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(StatusPane.Left)]
    [InlineData(StatusPane.Scratch)]
    public async Task Key_window_reveals_scratch_or_hides_it_without_forcing_the_left_split(StatusPane pane)
    {
        var directory = Directory.CreateTempSubdirectory("agterm-follow-pane-").FullName;
        try
        {
            using var host = CreateHost(directory, new AppSettings());
            Session? target = null;
            host.Host.RunLocked(() =>
            {
                target = host.Store.ActiveSession!;
                target.SplitSurface = new FakeSurface();
                target.HasSplit = true;
                target.SplitFocused = true;
                target.ScratchSurface = new FakeSurface();
                target.ScratchActive = pane != StatusPane.Scratch;
                host.Store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, StatusPane: pane), target.Id);
            });
            var token = await host.RevealAutoFollowedAsync(target!.Id, () => true);
            Assert.Equal(pane == StatusPane.Scratch ? target.ScratchSurface!.PaneToken : target.SplitSurface!.PaneToken, token);
            Assert.Equal(pane == StatusPane.Scratch, target.ScratchActive);
            Assert.True(target.SplitFocused);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Independent_modal_leases_and_shutdown_cancel_follow()
    {
        var directory = Directory.CreateTempSubdirectory("agterm-follow-close-").FullName;
        try
        {
            using var host = CreateHost(directory, new AppSettings());
            var (current, _) = PrepareBlocked(host, directory);
            host.Host.RunLocked(host.Store.SuppressAutoFollow);
            await host.SetUiAutoFollowSuppressedAsync(true);
            await host.SetUiAutoFollowSuppressedAsync(false);
            Flush(host);
            host.Host.RunLocked(() =>
            {
                Assert.True(host.Store.IsAutoFollowSuppressed);
                Assert.Equal(current.Id, host.Store.SelectedSessionId);
                host.Store.ResumeAutoFollow();
                host.Store.NoteUserActivity();
            });
            await host.ShutdownAsync();
            Flush(host);
            Assert.Equal(current.Id, host.Store.SelectedSessionId);
            Assert.Null(host.Store.ControlTree().AutoFollowMs);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Reveal_changes_the_status_pane_only_in_the_key_window_and_not_behind_zoom(bool key, bool zoom)
    {
        var directory = Directory.CreateTempSubdirectory("agterm-follow-reveal-").FullName;
        try
        {
            using var host = CreateHost(directory, new AppSettings());
            Session? target = null;
            host.Host.RunLocked(() =>
            {
                target = host.Store.ActiveSession!;
                target.SplitSurface = new FakeSurface();
                target.HasSplit = true;
                target.ScratchSurface = new FakeSurface();
                target.ScratchActive = true;
                host.Store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, StatusPane: StatusPane.Right),
                    target.Id);
                if (zoom) Assert.True(host.Host.Actions.SetSurfaceZoom(null, null,
                    AgTerm.Core.Protocol.ControlToggleMode.On).Ok);
            });
            var pane = await host.RevealAutoFollowedAsync(target!.Id, () => key);
            host.Host.RunLocked(() =>
            {
                Assert.Equal(key && !zoom, target.SplitFocused);
                Assert.Equal(!key || zoom, target.ScratchActive);
                Assert.Equal(key && !zoom ? target.SplitSurface!.PaneToken : null, pane);
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static (Session Current, Session Target) PrepareBlocked(LiveControlHost host, string directory)
    {
        Session? current = null, target = null;
        host.Host.RunLocked(() =>
        {
            current = host.Store.ActiveSession!;
            target = host.Store.AddSession(host.Store.CurrentWorkspaceId!.Value, directory)!;
            target.Surface ??= new FakeSurface();
            host.Store.SelectSession(current.Id);
            host.Store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), target.Id);
            host.Store.SetAutoFollow(100, false);
        });
        return (current!, target!);
    }

    private static void Flush(LiveControlHost host) =>
        ((Debouncer)typeof(AppStore).GetField("AutoFollowDebouncer",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host.Store)!).Flush();

    private static LiveControlHost CreateHost(string directory, AppSettings settings) => new(new LiveControlHostOptions
    {
        StateDir = directory,
        ConfigDir = directory,
        PipeName = "agterm-follow-" + Guid.NewGuid().ToString("N"),
        NotificationSettings = settings,
        OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
    });

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
