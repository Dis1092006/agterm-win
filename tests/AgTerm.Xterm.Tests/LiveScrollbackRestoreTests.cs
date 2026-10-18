using AgTerm.ControlServer;
using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// Saved pane output across a relaunch: the host writes each realized main/split pane's output at
/// shutdown and hands it to the pane's next realization, once.
/// </summary>
public sealed class LiveScrollbackRestoreTests : IDisposable
{
    private readonly string _stateDir = Directory.CreateTempSubdirectory("agterm-scrollback-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_stateDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Saved_output_reaches_the_main_and_split_requests_once()
    {
        var sessionId = SeedSplitSession();
        ScrollbackStorage.Write(sessionId, StatusPane.Left, "left-saved", _stateDir);
        ScrollbackStorage.Write(sessionId, StatusPane.Right, "right-saved", _stateDir);
        var requests = new List<SurfaceOpenRequest>();

        using var host = BuildHost(requests, new AppSettings());
        await SpinUntilAsync(() => Count(requests) == 2);

        lock (requests)
        {
            Assert.Equal("left-saved", Assert.Single(requests, r => r.Kind == LiveSurfaceKind.Main).Options.RestoredOutput);
            Assert.Equal("right-saved", Assert.Single(requests, r => r.Kind == LiveSurfaceKind.Split).Options.RestoredOutput);
        }
        Assert.False(File.Exists(ScrollbackStorage.FilePath(sessionId, StatusPane.Left, _stateDir)));
        Assert.False(File.Exists(ScrollbackStorage.FilePath(sessionId, StatusPane.Right, _stateDir)));
    }

    [Fact]
    public async Task Disabled_setting_drops_saved_output_instead_of_showing_it()
    {
        var sessionId = SeedSplitSession();
        ScrollbackStorage.Write(sessionId, StatusPane.Left, "left-saved", _stateDir);
        var requests = new List<SurfaceOpenRequest>();

        using var host = BuildHost(requests, new AppSettings { RestoreScrollback = false });
        await SpinUntilAsync(() => Count(requests) == 2);

        lock (requests) Assert.All(requests, r => Assert.Null(r.Options.RestoredOutput));
        Assert.False(File.Exists(ScrollbackStorage.FilePath(sessionId, StatusPane.Left, _stateDir)));
    }

    [Fact]
    public async Task Shutdown_saves_the_output_of_realized_main_and_split_panes()
    {
        var sessionId = SeedSplitSession();
        var requests = new List<SurfaceOpenRequest>();
        var host = BuildHost(requests, new AppSettings(), kind => $"{kind}-output");
        await SpinUntilAsync(() => Count(requests) == 2);

        await host.ShutdownAsync();
        host.Dispose();

        Assert.Equal("Main-output", ScrollbackStorage.Take(sessionId, StatusPane.Left, _stateDir));
        Assert.Equal("Split-output", ScrollbackStorage.Take(sessionId, StatusPane.Right, _stateDir));
    }

    [Fact]
    public async Task Shutdown_with_the_setting_disabled_saves_nothing()
    {
        var sessionId = SeedSplitSession();
        var requests = new List<SurfaceOpenRequest>();
        var host = BuildHost(requests, new AppSettings { RestoreScrollback = false }, kind => $"{kind}-output");
        await SpinUntilAsync(() => Count(requests) == 2);

        await host.ShutdownAsync();
        host.Dispose();

        Assert.False(Directory.Exists(ScrollbackStorage.DirectoryPath(_stateDir))
            && Directory.EnumerateFiles(ScrollbackStorage.DirectoryPath(_stateDir)).Any());
    }

    [Fact]
    public async Task Shutdown_drops_a_stale_split_file_of_an_unsplit_session()
    {
        var sessionId = SeedSplitSession(isSplit: false);
        var requests = new List<SurfaceOpenRequest>();
        var host = BuildHost(requests, new AppSettings(), kind => $"{kind}-output");
        await SpinUntilAsync(() => Count(requests) == 1);
        ScrollbackStorage.Write(sessionId, StatusPane.Right, "stale", _stateDir);

        await host.ShutdownAsync();
        host.Dispose();

        Assert.Equal("Main-output", ScrollbackStorage.Take(sessionId, StatusPane.Left, _stateDir));
        Assert.Null(ScrollbackStorage.Take(sessionId, StatusPane.Right, _stateDir));
    }

    private Guid SeedSplitSession(bool isSplit = true)
    {
        var sessionId = Guid.NewGuid();
        new PersistenceStore(_stateDir).Save(new Snapshot
        {
            SelectedSessionId = sessionId,
            Workspaces =
            [
                new WorkspaceSnapshot
                {
                    Id = Guid.NewGuid(),
                    Name = "w",
                    Sessions = [new SessionSnapshot { Id = sessionId, Cwd = _stateDir, IsSplit = isSplit }],
                },
            ],
        });
        return sessionId;
    }

    private LiveControlHost BuildHost(
        List<SurfaceOpenRequest> requests,
        AppSettings settings,
        Func<LiveSurfaceKind, string>? output = null) =>
        new(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-scrollback-" + Guid.NewGuid().ToString("N"),
            DefaultShell = "test-shell.exe",
            HomeDirectory = _stateDir,
            NotificationSettings = settings,
            OpenSurface = (request, _) =>
            {
                lock (requests) requests.Add(request);
                return Task.FromResult<IHeadlessSurface>(
                    new CapturingSurface(output?.Invoke(request.Kind)));
            },
        });

    private static int Count(List<SurfaceOpenRequest> requests)
    {
        lock (requests) return requests.Count;
    }

    private static async Task SpinUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Condition was not met within the test budget.");
            await Task.Delay(10);
        }
    }

    private sealed class CapturingSurface(string? output) : IHeadlessSurface, IScrollbackSurface
    {
        public string? PaneToken => null;
        public bool Inject(string text) => false;
        public string? ReadScreenText(bool all, int? lines) => null;
        public string? SelectedText => null;
        public void SelectAll() { }
        public bool Paste() => false;
        public bool PerformBindingAction(string action) => false;
        public void PromoteToPrimaryPane() { }
        public void Teardown() { }
        public Task<string?> CaptureScrollbackAsync() => Task.FromResult(output);
    }
}
