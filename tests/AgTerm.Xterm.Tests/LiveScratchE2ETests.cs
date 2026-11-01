using AgTerm.Cli;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

[Collection("webview2")]
public sealed class LiveScratchE2ETests : IDisposable
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-live-scratch-").FullName;
    private readonly LiveControlHost _host;
    private readonly Lazy<Task<XtermPage>> _page;

    public LiveScratchE2ETests(WebView2Fixture webView)
    {
        _webView = webView;
        _page = new Lazy<Task<XtermPage>>(() => _webView.CreatePageAsync());
        _host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-scratch-" + Guid.NewGuid().ToString("N"),
            DefaultShell = Cmd,
            HomeDirectory = _stateDir,
            OpenSurface = async (request, cancellationToken) =>
            {
                var shared = await _page.Value;
                return await _webView.RunAsync(async _ =>
                    await XtermPaneSurface.OpenAsync(shared, request.Options, cancellationToken));
            },
        });
        _host.Start();
    }

    public void Dispose()
    {
        _host.Dispose();
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private (int Exit, string Out, string Error) Ctl(params string[] argv)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = Agtermctl.Run(argv, new CliEnvironment
        {
            GetEnv = key => key == "AGTERM_CONTROL_SOCKET" ? _host.PipeName : null,
            Out = stdout,
            Error = stderr,
        });
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private string CtlOk(params string[] argv)
    {
        var (exit, output, error) = Ctl(argv);
        Assert.True(exit == 0, $"agtermctl {string.Join(' ', argv)} failed ({exit}): {error}");
        return output;
    }

    private string WaitForText(string contains, string pane = "left", int timeoutMs = 30000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var text = "";
        while (Environment.TickCount64 < deadline)
        {
            text = Ctl("session", "text", "--all", "--pane", pane).Out;
            if (text.Contains(contains)) return text;
            Thread.Sleep(100);
        }
        Assert.Fail($"session text --pane {pane} never contained \"{contains}\"; last:\n{text}");
        return "";
    }

    private async Task<XtermPaneSurface> EnableScratchAndWaitForRealizationAsync()
    {
        var scratchRealized = new TaskCompletionSource<XtermPaneSurface>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _host.SurfaceRealized += (request, surface) =>
        {
            if (request.Kind == LiveSurfaceKind.Scratch)
                scratchRealized.TrySetResult(surface);
        };
        CtlOk("session", "scratch", "on");
        return await scratchRealized.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private async Task<XtermPaneSurface> EnableSplitAndWaitForRealizationAsync()
    {
        var splitRealized = new TaskCompletionSource<XtermPaneSurface>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _host.SurfaceRealized += (request, surface) =>
        {
            if (request.Kind == LiveSurfaceKind.Split)
                splitRealized.TrySetResult(surface);
        };
        CtlOk("session", "split", "on");
        return await splitRealized.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private Task<LiveSelectionSnapshot> ReadActiveSelectionSnapshotAsync() =>
        _host.StoreCoordinator.ReadAsync(store =>
        {
            var session = store.ActiveSession!;
            return new LiveSelectionSnapshot(
                session.Id,
                session.DisplayName,
                store.WorkspaceForSession(session.Id)?.Name,
                session.Surface?.PaneToken,
                session.SplitSurface?.PaneToken,
                session.IsSplit,
                session.SplitFocused,
                session.SplitRatio,
                session.ScratchSurface?.PaneToken,
                session.ScratchActive);
        });

    [Fact]
    public async Task Scratch_shell_exit_closes_scratch_surface()
    {
        await EnableScratchAndWaitForRealizationAsync();
        WaitForText(">", pane: "scratch");

        CtlOk("session", "type", "--pane", "scratch", "exit\n");

        var deadline = Environment.TickCount64 + 10000;
        while (Environment.TickCount64 < deadline)
        {
            var closed = false;
            _host.Host.RunLocked(() =>
            {
                var session = _host.Host.Store.Workspaces
                    .SelectMany(workspace => workspace.Sessions)
                    .FirstOrDefault();
                closed = session is not null &&
                    !session.ScratchActive &&
                    session.ScratchSurface is null;
            });
            if (closed) return;
            Thread.Sleep(100);
        }

        Assert.Fail($"scratch stayed active after its shell exited; {ThreadPoolSnapshot.Describe()}");
    }

    // Q2.1: session.scratch on must flip ScratchActive synchronously with the control response,
    // independent of the async surface realization that follows.
    [Fact]
    public void Session_scratch_on_sets_active_state_before_surface_realizes()
    {
        WaitForText(">"); // main shell ready

        CtlOk("session", "scratch", "on");

        var active = false;
        _host.Host.RunLocked(() => active = _host.Host.Store.ActiveSession?.ScratchActive ?? false);
        Assert.True(active, "session.scratch on must set ScratchActive before the control response returns");
    }

    // Q2.1: hiding scratch clears ScratchActive but keeps the shell alive (keep-alive hide/show,
    // matching split's behavior).
    [Fact]
    public async Task Session_scratch_off_clears_active_but_keeps_surface_alive()
    {
        await EnableScratchAndWaitForRealizationAsync();
        WaitForText(">", pane: "scratch");

        CtlOk("session", "scratch", "off");

        var active = true;
        var surfaceAlive = false;
        _host.Host.RunLocked(() =>
        {
            var session = _host.Host.Store.ActiveSession;
            active = session?.ScratchActive ?? true;
            surfaceAlive = session?.ScratchSurface is not null;
        });
        Assert.False(active);
        Assert.True(surfaceAlive, "scratch off must hide, not tear down, the shell");
    }

    [Fact]
    public async Task Scratch_on_and_off_publish_presentable_selection_snapshots()
    {
        WaitForText(">");
        var shown = new TaskCompletionSource<LiveSelectionSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var hidden = new TaskCompletionSource<LiveSelectionSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        string? shownPaneId = null;
        _host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
        {
            if (snapshot.ScratchActive && !string.IsNullOrWhiteSpace(snapshot.ScratchPaneId))
            {
                shownPaneId = snapshot.ScratchPaneId;
                shown.TrySetResult(snapshot);
            }
            else if (!snapshot.ScratchActive &&
                shownPaneId is not null &&
                snapshot.ScratchPaneId == shownPaneId)
            {
                hidden.TrySetResult(snapshot);
            }
        };

        CtlOk("session", "scratch", "on");
        var shownSnapshot = await shown.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsType<TerminalLayout.Layered>(shownSnapshot.ToTerminalLayout(1));

        CtlOk("session", "scratch", "off");
        var hiddenSnapshot = await hidden.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsType<TerminalLayout.Single>(hiddenSnapshot.ToTerminalLayout(2));
        Assert.Equal(shownSnapshot.ScratchPaneId, hiddenSnapshot.ScratchPaneId);
    }

    // Q2.1: opening scratch for a non-active session selects that session (the scratch is a
    // full-coverage, focus-grabbing surface, so it must bring its owner to the front).
    [Fact]
    public void Scratch_on_for_a_background_target_selects_that_session()
    {
        WaitForText(">"); // first (default, active) session's main shell ready
        var backgroundId = CtlOk("session", "new", "--no-select").Trim();

        Guid? selectedBefore = null;
        _host.Host.RunLocked(() => selectedBefore = _host.Host.Store.SelectedSessionId);
        Assert.NotEqual(Guid.Parse(backgroundId), selectedBefore);

        CtlOk("session", "scratch", "on", "--target", backgroundId);

        Guid? selectedAfter = null;
        _host.Host.RunLocked(() => selectedAfter = _host.Host.Store.SelectedSessionId);
        Assert.Equal(Guid.Parse(backgroundId), selectedAfter);
    }

    // Q2.1: main and scratch are independent I/O surfaces — typing to one never appears on the
    // other's screen.
    [Fact]
    public async Task Main_and_scratch_pane_type_text_stay_independent()
    {
        WaitForText(">"); // main shell ready
        await EnableScratchAndWaitForRealizationAsync();
        WaitForText(">", pane: "scratch");

        var mainMarker = "MAIN-" + Guid.NewGuid().ToString("N")[..6];
        var scratchMarker = "SCRATCH-" + Guid.NewGuid().ToString("N")[..6];
        CtlOk("session", "type", $"echo {mainMarker}\n");
        CtlOk("session", "type", "--pane", "scratch", $"echo {scratchMarker}\n");

        WaitForText(mainMarker, pane: "left");
        WaitForText(scratchMarker, pane: "scratch");

        var mainText = Ctl("session", "text", "--all", "--pane", "left").Out;
        Assert.DoesNotContain(scratchMarker, mainText);

        var scratchText = Ctl("session", "text", "--all", "--pane", "scratch").Out;
        Assert.DoesNotContain(mainMarker, scratchText);
    }

    // Q2.1: toggling scratch off then on reuses the same pane token and shell — same behavior as
    // split's hide/show keep-alive.
    [Fact]
    public async Task Scratch_off_on_keeps_same_pane_token_and_screen()
    {
        WaitForText(">"); // main shell ready
        var scratchSurface = await EnableScratchAndWaitForRealizationAsync();
        WaitForText(">", pane: "scratch");

        var marker = "KEEP-ALIVE-" + Guid.NewGuid().ToString("N")[..6];
        CtlOk("session", "type", "--pane", "scratch", $"echo {marker}\n");
        WaitForText(marker, pane: "scratch");

        var tokenBefore = scratchSurface.PaneToken;
        CtlOk("session", "scratch", "off"); // hide — scratch shell keeps running
        CtlOk("session", "scratch", "on");  // re-show — must reuse the existing surface

        string? tokenAfter = null;
        _host.Host.RunLocked(() => tokenAfter = _host.Host.Store.ActiveSession?.ScratchSurface?.PaneToken);
        Assert.Equal(tokenBefore, tokenAfter);

        WaitForText(marker, pane: "scratch"); // same screen content still visible
    }

    // Q2.1: `session scratch on --command` while already active is a deliberate replacement — the
    // old shell is torn down and a fresh surface/token/process takes over, and the old shell's own
    // (now stale) exit event cannot clear the new surface's ownership.
    [Fact]
    public async Task Scratch_on_with_command_while_active_replaces_surface_and_survives_stale_old_exit()
    {
        WaitForText(">"); // main shell ready
        var oldSurface = await EnableScratchAndWaitForRealizationAsync();
        WaitForText(">", pane: "scratch");
        var oldToken = oldSurface.PaneToken;

        var newSurfaceRealized = new TaskCompletionSource<XtermPaneSurface>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _host.SurfaceRealized += (request, surface) =>
        {
            if (request.Kind == LiveSurfaceKind.Scratch && surface.PaneToken != oldToken)
                newSurfaceRealized.TrySetResult(surface);
        };

        CtlOk("session", "scratch", "on", "--command", $"{Cmd} /k");
        var newSurface = await newSurfaceRealized.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.NotEqual(oldToken, newSurface.PaneToken);

        // the old surface is torn down as part of the replacement; wait for its own exit event to
        // settle (rather than a fixed delay) before checking that it did not clear the new surface.
        try
        {
            await oldSurface.Pane.Exited.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch
        {
            // a torn-down pane's exit task may fault; only that it settled matters here.
        }

        var active = false;
        string? tokenNow = null;
        _host.Host.RunLocked(() =>
        {
            var session = _host.Host.Store.ActiveSession;
            active = session?.ScratchActive ?? false;
            tokenNow = session?.ScratchSurface?.PaneToken;
        });
        Assert.True(active, "the replacement scratch must remain active after the stale old exit settles");
        Assert.Equal(newSurface.PaneToken, tokenNow);
    }

    // Q2.1: after the scratch's own shell exits naturally (closing the surface), a later
    // session.scratch on must create a fresh surface/token rather than reuse the closed one.
    [Fact]
    public async Task Scratch_on_after_natural_exit_creates_fresh_surface_and_token()
    {
        var oldSurface = await EnableScratchAndWaitForRealizationAsync();
        WaitForText(">", pane: "scratch");
        var oldToken = oldSurface.PaneToken;

        CtlOk("session", "type", "--pane", "scratch", "exit\n");

        var closedDeadline = Environment.TickCount64 + 10000;
        var closed = false;
        while (Environment.TickCount64 < closedDeadline)
        {
            _host.Host.RunLocked(() =>
            {
                var session = _host.Host.Store.ActiveSession;
                closed = session is not null && !session.ScratchActive && session.ScratchSurface is null;
            });
            if (closed) break;
            Thread.Sleep(100);
        }
        Assert.True(closed, "scratch never closed after its own shell exited");

        var newSurface = await EnableScratchAndWaitForRealizationAsync();
        Assert.NotEqual(oldToken, newSurface.PaneToken);
    }

    // Q2.2 presentation contract (expected RED until Q2.2 wires scratch into
    // LiveSelectionSnapshot/MainWindow): an active scratch must present as a full covering layer
    // over the current base layout.
    [Fact]
    public async Task Active_scratch_yields_a_full_covering_layered_layout_over_single()
    {
        WaitForText(">"); // main shell ready
        await EnableScratchAndWaitForRealizationAsync();
        WaitForText(">", pane: "scratch");

        var snapshot = await ReadActiveSelectionSnapshotAsync();
        var layout = snapshot.ToTerminalLayout(1);

        var layered = Assert.IsType<TerminalLayout.Layered>(layout);
        Assert.IsType<TerminalLayout.Single>(layered.BaseLayout);
        Assert.Null(layered.Layer.SizePercent); // full covering layer, not floating
    }

    // Q2.2 presentation contract (expected RED until Q2.2 wires scratch into
    // LiveSelectionSnapshot/MainWindow): scratch must cover Split the same way it covers Single.
    [Fact]
    public async Task Active_scratch_yields_a_full_covering_layered_layout_over_split()
    {
        WaitForText(">"); // main shell ready
        await EnableSplitAndWaitForRealizationAsync();
        WaitForText(">", pane: "right");
        await EnableScratchAndWaitForRealizationAsync();
        WaitForText(">", pane: "scratch");

        var snapshot = await ReadActiveSelectionSnapshotAsync();
        var layout = snapshot.ToTerminalLayout(1);

        var layered = Assert.IsType<TerminalLayout.Layered>(layout);
        Assert.IsType<TerminalLayout.Split>(layered.BaseLayout);
        Assert.Null(layered.Layer.SizePercent); // full covering layer, not floating
    }

    // Q2.1 acceptance: scratch state and its surface must never survive an application restart.
    [Fact]
    public async Task Scratch_is_not_persisted_across_host_restart()
    {
        WaitForText(">"); // main shell ready
        await EnableScratchAndWaitForRealizationAsync();
        WaitForText(">", pane: "scratch");

        _host.Host.RunLocked(() => _host.Host.Store.Save());

        var page2Task = _webView.CreatePageAsync();
        using var host2 = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-scratch-restart-" + Guid.NewGuid().ToString("N"),
            DefaultShell = Cmd,
            HomeDirectory = _stateDir,
            OpenSurface = async (request, cancellationToken) =>
            {
                var shared = await page2Task;
                return await _webView.RunAsync(async _ =>
                    await XtermPaneSurface.OpenAsync(shared, request.Options, cancellationToken));
            },
        });
        host2.Start();

        var active = true;
        var surfacePresent = true;
        host2.Host.RunLocked(() =>
        {
            var session = host2.Host.Store.ActiveSession;
            active = session?.ScratchActive ?? true;
            surfacePresent = session?.ScratchSurface is not null;
        });
        Assert.False(active, "scratch must never be persisted across a restart");
        Assert.False(surfacePresent, "scratch must never be persisted across a restart");
    }
}