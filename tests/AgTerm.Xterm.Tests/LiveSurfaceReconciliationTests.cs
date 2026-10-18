using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm.Tests;

public sealed class LiveSurfaceReconciliationTests : IDisposable
{
    private static readonly LiveSurfaceKind[] AllSurfaceKinds =
    [
        LiveSurfaceKind.Main,
        LiveSurfaceKind.Split,
        LiveSurfaceKind.Scratch,
        LiveSurfaceKind.Overlay,
        LiveSurfaceKind.Quick,
    ];

    private readonly string _stateDir;

    public LiveSurfaceReconciliationTests()
    {
        _stateDir = Directory.CreateTempSubdirectory("agterm-p1-").FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_stateDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static TheoryData<LiveSurfaceKind> SurfaceKinds
    {
        get
        {
            var data = new TheoryData<LiveSurfaceKind>();
            foreach (var kind in AllSurfaceKinds)
            {
                data.Add(kind);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SurfaceKinds))]
    public async Task Reservation_commits_only_the_requested_slot(LiveSurfaceKind kind)
    {
        using var host = BuildHost();
        var scopes = new LiveDispatchScopeFactory();
        var sessionId = host.Store.ActiveSession!.Id;
        var request = Request(kind, sessionId);
        var siblings = SeedSiblingSlots(host, sessionId, kind);
        var realized = new FakeSurface { PaneToken = $"{kind}-realized" };
        using var coordinator = BuildCoordinator(host, scopes, (_, _) => Task.FromResult<IHeadlessSurface>(realized));

        PendingTerminalSurface? pending = null;
        using var scope = scopes.Begin();
        host.RunLocked(() =>
        {
            pending = coordinator.Reserve(request);
            Assert.Same(pending, GetSlot(host, sessionId, kind));
            AssertSiblingSlots(host, sessionId, kind, siblings);
        });

        await scope.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(realized, GetSlot(host, sessionId, kind));
        AssertSiblingSlots(host, sessionId, kind, siblings);
        Assert.Same(realized, await pending!.Ready.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [MemberData(nameof(SurfaceKinds))]
    public async Task Cancellation_clears_only_the_requested_slot_and_tears_down_late_surface(LiveSurfaceKind kind)
    {
        using var host = BuildHost();
        var scopes = new LiveDispatchScopeFactory();
        var sessionId = host.Store.ActiveSession!.Id;
        var request = Request(kind, sessionId);
        var siblings = SeedSiblingSlots(host, sessionId, kind);
        var openerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var openerCompletion = new TaskCompletionSource<IHeadlessSurface>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = BuildCoordinator(host, scopes, (_, _) =>
        {
            openerEntered.TrySetResult();
            return openerCompletion.Task;
        });

        PendingTerminalSurface? pending = null;
        using var scope = scopes.Begin();
        host.RunLocked(() => pending = coordinator.Reserve(request));

        var completion = scope.CompleteAsync(CancellationToken.None);
        await openerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        pending!.Teardown();

        var late = new FakeSurface { PaneToken = $"{kind}-late" };
        openerCompletion.TrySetResult(late);
        await completion.WaitAsync(TimeSpan.FromSeconds(5));

        await SpinUntilAsync(() => late.TeardownCount == 1, TimeSpan.FromSeconds(5));
        Assert.Null(GetSlot(host, sessionId, kind));
        AssertSiblingSlots(host, sessionId, kind, siblings);
        Assert.Null(await pending.Ready.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Control_dispatch_reconciles_slot_specific_requests_before_returning()
    {
        var requests = new List<SurfaceOpenRequest>();
        var readClipboard = new Func<string?>(() => "clipboard");
        using var host = BuildLiveHost(requests, readClipboard);
        host.Start();
        var client = new ControlPipeClient(host.PipeName);
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);
        var mainCwd = Directory.CreateDirectory(Path.Combine(_stateDir, "main-cwd")).FullName;
        var splitCwd = Directory.CreateDirectory(Path.Combine(_stateDir, "split-cwd")).FullName;
        var overlayCwd = Directory.CreateDirectory(Path.Combine(_stateDir, "overlay-cwd")).FullName;

        await host.StoreCoordinator.MutateAsync(store =>
        {
            var session = store.SessionWithId(sessionId)!;
            session.CurrentCwd = mainCwd;
            session.InitialSplitCwd = splitCwd;
        });
        lock (requests) requests.Clear();

        AssertOk(client.Send(new ControlRequest
        {
            Cmd = Command.SessionSplit,
            Args = new ControlArgs { Mode = "on" },
        }));
        AssertOk(client.Send(new ControlRequest
        {
            Cmd = Command.SessionScratch,
            Args = new ControlArgs { Mode = "on", Command = "scratch-tool --flag" },
        }));
        AssertOk(client.Send(new ControlRequest
        {
            Cmd = Command.SessionOverlayOpen,
            Args = new ControlArgs
            {
                Command = "overlay-tool --flag",
                Cwd = overlayCwd,
            },
        }));
        AssertOk(client.Send(new ControlRequest
        {
            Cmd = Command.Quick,
            Args = new ControlArgs { Mode = "show" },
        }));

        SurfaceOpenRequest[] captured;
        lock (requests) captured = requests.ToArray();
        Assert.Equal(4, captured.Length);

        AssertRequest(captured, LiveSurfaceKind.Split, sessionId, splitCwd,
            new[] { "test-shell.exe" }, 19, readClipboard);
        AssertRequest(captured, LiveSurfaceKind.Scratch, sessionId, mainCwd,
            new[] { "scratch-tool", "--flag" }, 19, readClipboard);
        AssertRequest(captured, LiveSurfaceKind.Overlay, sessionId, overlayCwd,
            new[]
            {
                Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                "/c",
                "overlay-tool --flag",
            }, 19, readClipboard);
        AssertRequest(captured, LiveSurfaceKind.Quick, null, mainCwd,
            new[] { "test-shell.exe" }, 19, readClipboard);

        var scratchCommand = await host.StoreCoordinator.ReadAsync(
            store => store.SessionWithId(sessionId)!.ScratchCommand);
        Assert.Null(scratchCommand);
    }

    [Fact]
    public void Bootstrap_reconciles_a_persisted_shown_split_before_construction_returns()
    {
        using (var seed = BuildHost())
        {
            var sessionId = seed.Store.ActiveSession!.Id;
            seed.RunLocked(() =>
            {
                seed.Store.ToggleSplit(sessionId);
                seed.Store.Save();
            });
        }

        var requests = new List<SurfaceOpenRequest>();
        using var host = BuildLiveHost(requests);

        SurfaceOpenRequest[] captured;
        lock (requests) captured = requests.ToArray();
        Assert.Contains(captured, request => request.Kind == LiveSurfaceKind.Main);
        Assert.Contains(captured, request => request.Kind == LiveSurfaceKind.Split);
        Assert.Equal(2, captured.Length);
    }

    [Fact]
    public async Task Store_coordinator_mutation_reconciles_without_a_control_dispatch()
    {
        var splitRequested = new TaskCompletionSource<SurfaceOpenRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-p1-ui-" + Guid.NewGuid().ToString("N"),
            DefaultShell = "test-shell.exe",
            HomeDirectory = _stateDir,
            OpenSurface = (request, _) =>
            {
                if (request.Kind == LiveSurfaceKind.Split)
                {
                    splitRequested.TrySetResult(request);
                }

                return Task.FromResult<IHeadlessSurface>(new FakeSurface());
            },
        });
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);

        await host.StoreCoordinator.MutateAsync(store => store.ToggleSplit(sessionId));

        var request = await splitRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(sessionId, request.SessionId);
        Assert.Equal(LiveSurfaceKind.Split, request.Kind);
    }

    [Theory]
    [InlineData(LiveSurfaceKind.Main)]
    [InlineData(LiveSurfaceKind.Split)]
    public async Task Restore_override_is_captured_as_shell_input_and_consumed_once(LiveSurfaceKind kind)
    {
        var requests = new List<SurfaceOpenRequest>();
        using var host = BuildLiveHost(requests, restoreRunningCommand: true);
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);
        lock (requests) requests.Clear();

        await host.StoreCoordinator.MutateAsync(store =>
        {
            var session = store.SessionWithId(sessionId)!;
            if (kind == LiveSurfaceKind.Main)
            {
                session.Surface?.Teardown();
                session.Surface = null;
                session.InitialCommand = "initial-tool --stale";
                session.ForegroundCommand = new[] { "captured-tool", "--stale" };
                session.PendingRestoreCommand = "restore-tool --resume main";
            }
            else
            {
                session.HasSplit = true;
                session.SplitSurface = null;
                session.SplitForegroundCommand = new[] { "captured-split", "--stale" };
                session.PendingSplitRestoreCommand = "restore-tool --resume split";
            }
        });

        await SpinUntilAsync(
            () =>
            {
                lock (requests) return requests.Any(item => item.Kind == kind);
            },
            TimeSpan.FromSeconds(5));

        SurfaceOpenRequest request;
        lock (requests) request = Assert.Single(requests, item => item.Kind == kind);
        Assert.Equal(new[] { "test-shell.exe" }, request.Options.Argv);
        Assert.Equal(
            kind == LiveSurfaceKind.Main
                ? "restore-tool --resume main\n"
                : "restore-tool --resume split\n",
            request.Options.InitialInput);

        var consumed = await host.StoreCoordinator.ReadAsync(store =>
        {
            var session = store.SessionWithId(sessionId)!;
            return kind == LiveSurfaceKind.Main
                ? (session.PendingRestoreCommand, session.ForegroundCommand)
                : (session.PendingSplitRestoreCommand, session.SplitForegroundCommand);
        });
        Assert.Null(consumed.Item1);
        Assert.Null(consumed.Item2);
    }

    [Theory]
    [InlineData(LiveSurfaceKind.Main, true, "tail -f\u001Bx")]
    [InlineData(LiveSurfaceKind.Split, true, "tail -f\u001Bx")]
    [InlineData(LiveSurfaceKind.Main, false, "grep a\u0015b")]
    [InlineData(LiveSurfaceKind.Split, false, "grep a\u0015b")]
    public async Task Restore_seed_carrying_a_control_character_starts_a_plain_shell(
        LiveSurfaceKind kind, bool asOverride, string seed)
    {
        await AssertSeedStartsPlainShell(kind, asOverride, seed.Split(' '));
    }

    [Theory]
    [InlineData(LiveSurfaceKind.Main)]
    [InlineData(LiveSurfaceKind.Split)]
    public async Task Captured_command_with_a_shell_metacharacter_starts_a_plain_shell(LiveSurfaceKind kind)
    {
        await AssertSeedStartsPlainShell(kind, asOverride: false, new[] { "echo", "a&calc" });
    }

    private async Task AssertSeedStartsPlainShell(LiveSurfaceKind kind, bool asOverride, string[] seed)
    {
        var requests = new List<SurfaceOpenRequest>();
        using var host = BuildLiveHost(requests, restoreRunningCommand: true);
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);
        lock (requests) requests.Clear();
        var line = string.Join(' ', seed);

        await host.StoreCoordinator.MutateAsync(store =>
        {
            var session = store.SessionWithId(sessionId)!;
            if (kind == LiveSurfaceKind.Main)
            {
                session.Surface?.Teardown();
                session.Surface = null;
                if (asOverride) session.PendingRestoreCommand = line;
                else session.ForegroundCommand = seed;
            }
            else
            {
                session.HasSplit = true;
                session.SplitSurface = null;
                if (asOverride) session.PendingSplitRestoreCommand = line;
                else session.SplitForegroundCommand = seed;
            }
        });

        await SpinUntilAsync(() => { lock (requests) return requests.Any(r => r.Kind == kind); }, TimeSpan.FromSeconds(5));
        SurfaceOpenRequest request;
        lock (requests) request = Assert.Single(requests, r => r.Kind == kind);
        Assert.Equal(new[] { "test-shell.exe" }, request.Options.Argv);
        Assert.Null(request.Options.InitialInput);

        var consumed = await host.StoreCoordinator.ReadAsync(store =>
        {
            var session = store.SessionWithId(sessionId)!;
            return kind == LiveSurfaceKind.Main
                ? (session.PendingRestoreCommand, session.ForegroundCommand)
                : (session.PendingSplitRestoreCommand, session.SplitForegroundCommand);
        });
        Assert.Null(consumed.Item1);
        Assert.Null(consumed.Item2);
    }

    [Theory]
    [InlineData(true, "captured-tool --resume\n")]
    [InlineData(false, null)]
    public async Task Captured_foreground_command_is_typed_into_a_shell_not_executed(bool restoreRunningCommand, string? expectedInput)
    {
        var requests = new List<SurfaceOpenRequest>();
        using var host = BuildLiveHost(requests, restoreRunningCommand: restoreRunningCommand);
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);
        lock (requests) requests.Clear();

        await host.StoreCoordinator.MutateAsync(store =>
        {
            var session = store.SessionWithId(sessionId)!;
            session.Surface?.Teardown();
            session.Surface = null;
            session.ForegroundCommand = new[] { "captured-tool", "--resume" };
        });

        await SpinUntilAsync(() => { lock (requests) return requests.Any(r => r.Kind == LiveSurfaceKind.Main); }, TimeSpan.FromSeconds(5));
        SurfaceOpenRequest request;
        lock (requests) request = Assert.Single(requests, r => r.Kind == LiveSurfaceKind.Main);
        Assert.Equal(new[] { "test-shell.exe" }, request.Options.Argv);
        Assert.Equal(expectedInput, request.Options.InitialInput);
    }

    [Theory]
    [InlineData(true, "", null)]
    [InlineData(false, "restore-tool --disabled", null)]
    public async Task Empty_or_disabled_restore_override_starts_a_plain_shell(
        bool restoreRunningCommand,
        string restoreOverride,
        string? expectedInitialInput)
    {
        var requests = new List<SurfaceOpenRequest>();
        using var host = BuildLiveHost(requests, restoreRunningCommand: restoreRunningCommand);
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);
        lock (requests) requests.Clear();

        await host.StoreCoordinator.MutateAsync(store =>
        {
            var session = store.SessionWithId(sessionId)!;
            session.Surface?.Teardown();
            session.Surface = null;
            session.InitialCommand = "initial-tool --stale";
            session.ForegroundCommand = new[] { "captured-tool", "--stale" };
            session.PendingRestoreCommand = restoreOverride;
        });

        await SpinUntilAsync(
            () =>
            {
                lock (requests) return requests.Any(item => item.Kind == LiveSurfaceKind.Main);
            },
            TimeSpan.FromSeconds(5));

        SurfaceOpenRequest request;
        lock (requests) request = Assert.Single(requests, item => item.Kind == LiveSurfaceKind.Main);
        Assert.Equal(new[] { "test-shell.exe" }, request.Options.Argv);
        Assert.Equal(expectedInitialInput, request.Options.InitialInput);
    }

    [Theory]
    [MemberData(nameof(SurfaceKinds))]
    public async Task Commit_callback_carries_the_exact_surface_request(LiveSurfaceKind kind)
    {
        using var host = BuildHost();
        var scopes = new LiveDispatchScopeFactory();
        var sessionId = host.Store.ActiveSession!.Id;
        var request = Request(kind, sessionId);
        var realized = new FakeSurface { PaneToken = $"{kind}-callback" };
        SurfaceOpenRequest? committedRequest = null;
        IHeadlessSurface? committedSurface = null;
        using var coordinator = BuildCoordinator(
            host,
            scopes,
            (_, _) => Task.FromResult<IHeadlessSurface>(realized),
            (committed, surface) =>
            {
                committedRequest = committed;
                committedSurface = surface;
            });

        using var scope = scopes.Begin();
        host.RunLocked(() => coordinator.Reserve(request));
        await scope.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(request, committedRequest);
        Assert.Same(realized, committedSurface);
    }

    [Fact]
    public async Task Hide_retains_keep_alive_slots_and_repeated_dispatch_does_not_duplicate_them()
    {
        var requests = new List<SurfaceOpenRequest>();
        using var host = BuildLiveHost(requests);
        host.Start();
        var client = new ControlPipeClient(host.PipeName);
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);
        lock (requests) requests.Clear();

        AssertOk(client.Send(new ControlRequest
        {
            Cmd = Command.SessionSplit,
            Args = new ControlArgs { Mode = "on" },
        }));
        AssertOk(client.Send(new ControlRequest
        {
            Cmd = Command.SessionScratch,
            Args = new ControlArgs { Mode = "on" },
        }));
        AssertOk(client.Send(new ControlRequest
        {
            Cmd = Command.Quick,
            Args = new ControlArgs { Mode = "show" },
        }));

        var before = await host.StoreCoordinator.ReadAsync(store =>
        {
            var session = store.SessionWithId(sessionId)!;
            return (session.SplitSurface, session.ScratchSurface, host.Host.Actions.Quick.Surface);
        });
        Assert.NotNull(before.SplitSurface);
        Assert.NotNull(before.ScratchSurface);
        Assert.NotNull(before.Surface);

        AssertOk(client.Send(new ControlRequest
        {
            Cmd = Command.SessionSplit,
            Args = new ControlArgs { Mode = "off" },
        }));
        AssertOk(client.Send(new ControlRequest
        {
            Cmd = Command.SessionScratch,
            Args = new ControlArgs { Mode = "off" },
        }));
        AssertOk(client.Send(new ControlRequest
        {
            Cmd = Command.Quick,
            Args = new ControlArgs { Mode = "hide" },
        }));
        AssertOk(client.Send(new ControlRequest { Cmd = Command.Tree }));

        var after = await host.StoreCoordinator.ReadAsync(store =>
        {
            var session = store.SessionWithId(sessionId)!;
            return (session.SplitSurface, session.ScratchSurface, host.Host.Actions.Quick.Surface);
        });
        Assert.Same(before.SplitSurface, after.SplitSurface);
        Assert.Same(before.ScratchSurface, after.ScratchSurface);
        Assert.Same(before.Surface, after.Surface);
        lock (requests) Assert.Equal(3, requests.Count);
    }

    [Fact]
    public async Task Reconciliation_tears_down_split_and_overlay_no_longer_present_in_model_state()
    {
        var requests = new List<SurfaceOpenRequest>();
        using var host = BuildLiveHost(requests);
        host.Start();
        var client = new ControlPipeClient(host.PipeName);
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);

        AssertOk(client.Send(new ControlRequest
        {
            Cmd = Command.SessionSplit,
            Args = new ControlArgs { Mode = "on" },
        }));
        AssertOk(client.Send(new ControlRequest
        {
            Cmd = Command.SessionOverlayOpen,
            Args = new ControlArgs { Command = "overlay-tool" },
        }));

        var before = await host.StoreCoordinator.ReadAsync(store =>
        {
            var session = store.SessionWithId(sessionId)!;
            return ((FakeSurface)session.SplitSurface!, (FakeSurface)session.OverlaySurface!);
        });

        await host.StoreCoordinator.MutateAsync(store =>
        {
            var session = store.SessionWithId(sessionId)!;
            session.HasSplit = false;
            session.IsSplit = false;
            session.OverlayActive = false;
        });

        var after = await host.StoreCoordinator.ReadAsync(store =>
        {
            var session = store.SessionWithId(sessionId)!;
            return (session.SplitSurface, session.OverlaySurface);
        });
        Assert.Null(after.SplitSurface);
        Assert.Null(after.OverlaySurface);
        Assert.Equal(1, before.Item1.TeardownCount);
        Assert.Equal(1, before.Item2.TeardownCount);
    }

    [Fact]
    public async Task Failed_open_is_retried_by_the_next_reconciliation()
    {
        var splitAttempts = 0;
        var recovered = new FakeSurface();
        using var host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-p1-retry-" + Guid.NewGuid().ToString("N"),
            DefaultShell = "test-shell.exe",
            HomeDirectory = _stateDir,
            OpenSurface = (request, _) =>
            {
                if (request.Kind != LiveSurfaceKind.Split)
                {
                    return Task.FromResult<IHeadlessSurface>(new FakeSurface());
                }

                splitAttempts++;
                return splitAttempts == 1
                    ? Task.FromException<IHeadlessSurface>(new InvalidOperationException("first open fails"))
                    : Task.FromResult<IHeadlessSurface>(recovered);
            },
        });
        host.Start();
        var client = new ControlPipeClient(host.PipeName);
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);

        AssertOk(client.Send(new ControlRequest
        {
            Cmd = Command.SessionSplit,
            Args = new ControlArgs { Mode = "on" },
        }));
        Assert.Equal(1, splitAttempts);
        Assert.Null(await host.StoreCoordinator.ReadAsync(
            store => store.SessionWithId(sessionId)!.SplitSurface));

        AssertOk(client.Send(new ControlRequest { Cmd = Command.Tree }));

        Assert.Equal(2, splitAttempts);
        Assert.Same(recovered, await host.StoreCoordinator.ReadAsync(
            store => store.SessionWithId(sessionId)!.SplitSurface));
    }

    [Fact]
    public void Surface_realized_event_preserves_slot_identity()
    {
        var realized = typeof(LiveControlHost).GetEvent(nameof(LiveControlHost.SurfaceRealized));

        Assert.NotNull(realized);
        Assert.Equal(
            typeof(Action<SurfaceOpenRequest, XtermPaneSurface>),
            realized.EventHandlerType);
    }

    [Fact]
    public void App_process_creation_is_confined_to_the_injected_surface_composition_root()
    {
        var repoRoot = FindRepoRoot();
        var appRoot = Path.Combine(repoRoot, "src", "AgTerm.App");
        var sourceFiles = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToArray();
        var sources = sourceFiles.ToDictionary(path => path, File.ReadAllText);

        Assert.DoesNotContain(sources, pair => pair.Value.Contains("ConPtySession.Start", StringComparison.Ordinal));
        Assert.DoesNotContain(sources, pair => pair.Value.Contains("TerminalPane.OpenAsync", StringComparison.Ordinal));

        var directOpenCalls = sources
            .Where(pair => pair.Value.Contains("XtermPaneSurface.OpenAsync", StringComparison.Ordinal))
            .ToArray();
        var allowedPath = Path.Combine(appRoot, "MainWindow.xaml.cs");
        var allowedSource = Assert.Single(directOpenCalls);
        Assert.Equal(allowedPath, allowedSource.Key, ignoreCase: true);
        Assert.Contains(
            "private Task<IHeadlessSurface> OpenSurfaceAsync(SurfaceOpenRequest request, CancellationToken cancellationToken)",
            allowedSource.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_passes_the_restore_running_command_setting_to_the_live_host()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Contains(
            "RestoreRunningCommand = _settings.RestoreRunningCommand == true,",
            source,
            StringComparison.Ordinal);
    }

    private HeadlessHost BuildHost() =>
        new(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-p1-" + Guid.NewGuid().ToString("N"),
        });

    private LiveControlHost BuildLiveHost(
        List<SurfaceOpenRequest> requests,
        Func<string?>? readClipboard = null,
        bool restoreRunningCommand = false) =>
        new(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-p1-live-" + Guid.NewGuid().ToString("N"),
            DefaultShell = "test-shell.exe",
            HomeDirectory = _stateDir,
            BaseFontSize = 19,
            ReadClipboard = readClipboard,
            RestoreRunningCommand = restoreRunningCommand,
            OpenSurface = (request, _) =>
            {
                lock (requests) requests.Add(request);
                return Task.FromResult<IHeadlessSurface>(new FakeSurface());
            },
        });

    private static LiveSurfaceCoordinator BuildCoordinator(
        HeadlessHost host,
        LiveDispatchScopeFactory scopes,
        Func<SurfaceOpenRequest, CancellationToken, Task<IHeadlessSurface>> opener,
        Action<SurfaceOpenRequest, IHeadlessSurface>? committed = null) =>
        new(host, scopes, opener, TimeSpan.FromSeconds(15), committed);

    private XtermSurfaceOptions DefaultOptions() =>
        new() { Argv = new[] { "cmd.exe" }, Cwd = _stateDir };

    private SurfaceOpenRequest Request(LiveSurfaceKind kind, Guid sessionId) =>
        new(kind == LiveSurfaceKind.Quick ? null : sessionId, kind, DefaultOptions());

    private static void AssertOk(ControlResponse response) =>
        Assert.True(response.Ok, response.Error);

    private static void AssertRequest(
        IEnumerable<SurfaceOpenRequest> requests,
        LiveSurfaceKind kind,
        Guid? sessionId,
        string cwd,
        IReadOnlyList<string> argv,
        double baseFontSize,
        Func<string?> readClipboard)
    {
        var request = Assert.Single(requests, request => request.Kind == kind);
        Assert.Equal(sessionId, request.SessionId);
        Assert.Equal(cwd, request.Options.Cwd);
        Assert.Equal(argv, request.Options.Argv);
        Assert.Equal(baseFontSize, request.Options.BaseFontSize);
        Assert.Same(readClipboard, request.Options.ReadClipboard);
    }

    private static IReadOnlyDictionary<LiveSurfaceKind, FakeSurface> SeedSiblingSlots(
        HeadlessHost host,
        Guid sessionId,
        LiveSurfaceKind target)
    {
        var siblings = AllSurfaceKinds
            .Where(kind => kind != target)
            .ToDictionary(kind => kind, kind => new FakeSurface { PaneToken = $"{kind}-sibling" });

        host.RunLocked(() =>
        {
            foreach (var (kind, surface) in siblings)
            {
                SetSlot(host, sessionId, kind, surface);
            }

            SetSlot(host, sessionId, target, null);
        });

        return siblings;
    }

    private static void AssertSiblingSlots(
        HeadlessHost host,
        Guid sessionId,
        LiveSurfaceKind target,
        IReadOnlyDictionary<LiveSurfaceKind, FakeSurface> siblings)
    {
        foreach (var (kind, surface) in siblings)
        {
            Assert.NotEqual(target, kind);
            Assert.Same(surface, GetSlot(host, sessionId, kind));
            Assert.Equal(0, surface.TeardownCount);
        }
    }

    private static ITerminalSurface? GetSlot(HeadlessHost host, Guid sessionId, LiveSurfaceKind kind)
    {
        var session = host.Store.SessionWithId(sessionId)!;
        return kind switch
        {
            LiveSurfaceKind.Main => session.Surface,
            LiveSurfaceKind.Split => session.SplitSurface,
            LiveSurfaceKind.Scratch => session.ScratchSurface,
            LiveSurfaceKind.Overlay => session.OverlaySurface,
            LiveSurfaceKind.Quick => host.Actions.Quick.Surface,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static void SetSlot(
        HeadlessHost host,
        Guid sessionId,
        LiveSurfaceKind kind,
        ITerminalSurface? surface)
    {
        var session = host.Store.SessionWithId(sessionId)!;
        switch (kind)
        {
            case LiveSurfaceKind.Main:
                session.Surface = surface;
                break;
            case LiveSurfaceKind.Split:
                session.SplitSurface = surface;
                break;
            case LiveSurfaceKind.Scratch:
                session.ScratchSurface = surface;
                break;
            case LiveSurfaceKind.Overlay:
                session.OverlaySurface = surface;
                break;
            case LiveSurfaceKind.Quick:
                host.Actions.Quick.Surface = (IHeadlessSurface?)surface;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static async Task SpinUntilAsync(Func<bool> condition, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not met within the test budget.");
            }

            await Task.Delay(10);
        }
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}