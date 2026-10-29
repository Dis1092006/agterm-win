using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm.Tests;

public sealed class DetachedQuickHostTests
{
    [Fact]
    public async Task Every_window_shares_one_quick_and_closing_its_first_window_keeps_it_alive()
    {
        var directory = Directory.CreateTempSubdirectory("agterm-app-quick-").FullName;
        try
        {
            using var app = new LiveApplicationHost(new()
            {
                StateDir = directory, HomeDirectory = directory,
                WindowController = new Windows(),
            });
            var first = app.LaunchWindowIds.Single();
            var secondResult = await app.CreateWindowAsync();
            var second = Guid.Parse(secondResult.Result!.Id!);
            Assert.Same(app.Quick, app.GetOpenWindow(first).Actions.Quick);
            Assert.Same(app.Quick, app.GetOpenWindow(second).Actions.Quick);
            var surface = new FakeSurface { PaneToken = "app-quick" };
            app.Quick.Surface = surface;
            app.Quick.Show(false);
            app.Quick.SetZoom(ControlToggleMode.On);
            await app.CloseWindowAsync(first);
            Assert.True(app.Quick.Visible);
            Assert.Equal(0, surface.TeardownCount);
            Assert.Same(surface, app.GetOpenWindow(second).Actions.Quick.Surface);
            await app.CloseWindowAsync(second);
            Assert.False(app.Quick.Visible);
            Assert.False(app.Quick.IsZoomed);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Ordinary_window_layout_never_hosts_quick()
    {
        var layout = new LiveSelectionSnapshot(null, null, QuickPaneId: "quick", QuickVisible: true)
            .ToTerminalLayout(1);
        Assert.IsNotType<TerminalLayout.Layered>(layout);
    }

    [Fact]
    public async Task Timed_out_creation_reports_failure_and_tears_down_late_surface()
    {
        var directory = Directory.CreateTempSubdirectory("agterm-app-quick-").FullName;
        try
        {
            using var app = new LiveApplicationHost(new() { StateDir = directory, HomeDirectory = directory });
            var pending = new TaskCompletionSource<IHeadlessSurface>(TaskCreationOptions.RunContinuationsAsynchronously);
            var discarded = new FakeSurface { PaneToken = "late" };
            LiveFailureNotice? failure = null;
            app.ConfigureQuickTerminal(new()
            {
                OpenSurface = (_, _) => pending.Task,
                RealizeBudget = TimeSpan.FromMilliseconds(50),
                FailureReported = notice => failure = notice,
            });
            app.Quick.Show();
            await app.EnsureQuickSurfaceAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(failure);
            Assert.False(app.Quick.Visible);
            pending.SetResult(discarded);
            for (var attempt = 0; attempt < 100 && discarded.TeardownCount == 0; attempt++)
                await Task.Delay(10);
            Assert.Equal(1, discarded.TeardownCount);
            Assert.Null(app.Quick.Surface);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Quick_input_records_activity_in_the_current_window()
    {
        var directory = Directory.CreateTempSubdirectory("agterm-app-quick-").FullName;
        try
        {
            using var app = new LiveApplicationHost(new() { StateDir = directory, HomeDirectory = directory });
            var window = app.GetOpenWindow(app.ActiveWindowId!.Value);
            Assert.Null(window.Actions.ControlTree(null).Result!.Tree!.IdleMs);
            await app.NoteQuickUserActivityAsync();
            Assert.NotNull(window.Actions.ControlTree(null).Result!.Tree!.IdleMs);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Shared_quick_changes_refresh_normal_window_chrome_without_reparenting_the_surface()
    {
        var directory = Directory.CreateTempSubdirectory("agterm-app-quick-").FullName;
        try
        {
            using var app = new LiveApplicationHost(new() { StateDir = directory, HomeDirectory = directory });
            var live = app.AttachWindow(app.LaunchWindowIds.Single(), new()
            {
                OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface { PaneToken = Guid.NewGuid().ToString() }),
            });
            var changed = new TaskCompletionSource<LiveSelectionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            live.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
            {
                if (snapshot.QuickVisible) changed.TrySetResult(snapshot);
            };
            app.Quick.Show();
            var snapshot = await changed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsNotType<TerminalLayout.Layered>(snapshot.ToTerminalLayout(1));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Global_hotkey_reload_is_published_once_not_once_per_window()
    {
        var directory = Directory.CreateTempSubdirectory("agterm-app-quick-").FullName;
        try
        {
            using var app = new LiveApplicationHost(new()
            {
                StateDir = directory, HomeDirectory = directory, WindowController = new Windows(),
            });
            await app.CreateWindowAsync();
            var reloads = 0;
            app.KeymapReloaded += _ => reloads++;
            app.Start();
            var response = await Task.Run(() => new ControlPipeClient(app.PipeName)
                .Send(new ControlRequest { Cmd = Command.KeymapReload }));
            Assert.True(response.Ok);
            Assert.Equal(1, reloads);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task A_background_gui_picker_hides_pinned_quick_and_the_active_picker_blocks_global_summons()
    {
        var directory = Directory.CreateTempSubdirectory("agterm-app-quick-").FullName;
        try
        {
            using var app = new LiveApplicationHost(new()
            {
                StateDir = directory, HomeDirectory = directory, WindowController = new Windows(),
            });
            var first = app.LaunchWindowIds.Single();
            var second = Guid.Parse((await app.CreateWindowAsync()).Result!.Id!);
            await app.ReportActivatedAsync(first);
            app.Start();
            app.Quick.Show(false);
            var response = await Task.Run(() => new ControlPipeClient(app.PipeName).Send(new ControlRequest
            {
                Cmd = Command.PickOpen,
                Args = new ControlArgs
                {
                    Window = second.ToString(),
                    Items = [new ControlPickItem { Id = "one", Label = "One" }],
                },
            }));
            Assert.True(response.Ok);
            Assert.False(app.Quick.Visible);
            await app.ToggleQuickAsync(fromGlobalHotkey: true);
            Assert.True(app.Quick.Visible);
            app.Quick.Hide();
            await app.ReportActivatedAsync(second);
            await app.ToggleQuickAsync(fromGlobalHotkey: true);
            Assert.False(app.Quick.Visible);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class Windows : ILiveWindowController
    {
        public Task OpenWindowAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task ActivateWindowAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task CloseWindowAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task<LiveWindowPresentation?> CaptureWindowAsync(Guid id, CancellationToken ct) => Task.FromResult<LiveWindowPresentation?>(null);
        public Task<int> GetDisplayCountAsync(CancellationToken ct) => Task.FromResult(1);
        public Task ResizeWindowAsync(Guid id, int width, int height, CancellationToken ct) => Task.CompletedTask;
        public Task<LiveWindowMoveResult> MoveWindowAsync(Guid id, int x, int y, int? display, CancellationToken ct) => Task.FromResult(new LiveWindowMoveResult(true, 1));
        public Task ToggleZoomAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task ToggleFullscreenAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
    }
}
