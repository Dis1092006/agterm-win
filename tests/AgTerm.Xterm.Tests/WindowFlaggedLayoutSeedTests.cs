using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm.Tests;

/// <summary>Upstream 8088e146 WindowLibraryFlaggedLayoutTests: a window store takes the app-wide flagged layout
/// when it is built, before its saved state is restored, so control commands that reach the window before the UI
/// attaches already navigate the flagged view in the right layout.</summary>
public sealed class WindowFlaggedLayoutSeedTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("agterm-flagged-seed-").FullName;
    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void Launch_restored_windows_carry_the_saved_layout()
    {
        new SettingsStore(directory).Save(new AppSettings { FlaggedViewLayout = "tree" });

        using var app = new LiveApplicationHost(new() { StateDir = directory, HomeDirectory = directory });

        Assert.Equal(FlaggedViewLayout.Tree, app.GetOpenWindow(app.LaunchWindowIds.Single()).Store.FlaggedLayout);
    }

    [Fact]
    public void Without_a_setting_the_layout_is_flat()
    {
        using var app = new LiveApplicationHost(new() { StateDir = directory, HomeDirectory = directory });

        Assert.Equal(FlaggedViewLayout.Flat, app.GetOpenWindow(app.LaunchWindowIds.Single()).Store.FlaggedLayout);
    }

    [Fact]
    public async Task A_window_opened_after_the_change_takes_the_current_layout()
    {
        using var app = new LiveApplicationHost(new()
        {
            StateDir = directory, HomeDirectory = directory, WindowController = new Windows(),
        });
        await app.Settings.UpdateCurrentAsync(
            settings => settings with { FlaggedViewLayout = "plain" }, SettingsApplyKind.Command);

        var opened = Guid.Parse((await app.CreateWindowAsync()).Result!.Id!);

        Assert.Equal(FlaggedViewLayout.Plain, app.GetOpenWindow(opened).Store.FlaggedLayout);
    }

    private sealed class Windows : ILiveWindowController
    {
        public Task OpenWindowAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task ActivateWindowAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task CloseWindowAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task<LiveWindowPresentation?> CaptureWindowAsync(Guid id, CancellationToken ct) => Task.FromResult<LiveWindowPresentation?>(null);
        public Task<int> GetDisplayCountAsync(CancellationToken ct) => Task.FromResult(1);
        public Task<LiveWindowSize?> ResizeWindowAsync(Guid id, int width, int height, CancellationToken ct) => Task.FromResult<LiveWindowSize?>(null);
        public Task<LiveWindowMoveResult> MoveWindowAsync(Guid id, int x, int y, int? display, CancellationToken ct) => Task.FromResult(new LiveWindowMoveResult(true, 1));
        public Task ToggleZoomAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task ToggleFullscreenAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task<LiveWindowMinimizeResult> MinimizeWindowAsync(Guid id, ControlToggleMode mode, CancellationToken ct) =>
            Task.FromResult(new LiveWindowMinimizeResult(LiveWindowMinimizeStatus.Applied, mode == ControlToggleMode.On));
    }
}
