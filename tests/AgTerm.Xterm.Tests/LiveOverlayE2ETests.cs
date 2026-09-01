using AgTerm.Cli;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// Q3 live command overlay: real LiveControlHost + shared WebView2 + ConPTY + agtermctl. The overlay
/// wire has no --pane selector (session.text/session.type only know left/right/scratch), so tests
/// read/observe the overlay through the live surface object (captured off LiveControlHost.SurfaceRealized)
/// and through in-process store state rather than through the CLI pane options.
/// </summary>
[Collection("webview2")]
public sealed class LiveOverlayE2ETests : IDisposable
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-live-overlay-").FullName;
    private readonly LiveControlHost _host;
    private readonly Lazy<Task<XtermPage>> _page;

    public LiveOverlayE2ETests(WebView2Fixture webView)
    {
        _webView = webView;
        _page = new Lazy<Task<XtermPage>>(() => _webView.CreatePageAsync());
        _host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-overlay-" + Guid.NewGuid().ToString("N"),
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

    /// <summary>Reads model state under the host gate; AppStore is not thread-safe.</summary>
    private T ReadLocked<T>(Func<T> read)
    {
        var result = default(T)!;
        _host.Host.RunLocked(() => result = read());
        return result;
    }

    /// <summary>Polls a gated predicate instead of a fixed sleep, matching the poll pattern already
    /// used by the scratch/split live E2E suites.</summary>
    private void WaitUntilLocked(Func<bool> predicate, string failureMessage, int timeoutMs = 15000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (ReadLocked(predicate)) return;
            Thread.Sleep(100);
        }
        Assert.Fail(failureMessage);
    }

    /// <summary>Polls the live surface's own screen buffer directly (no wire pane selector exists
    /// for the overlay).</summary>
    private string WaitForOverlayText(XtermPaneSurface surface, string contains, int timeoutMs = 15000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var text = "";
        while (Environment.TickCount64 < deadline)
        {
            text = surface.ReadScreenText(all: true, lines: null) ?? "";
            if (text.Contains(contains)) return text;
            Thread.Sleep(100);
        }
        Assert.Fail($"overlay surface never contained \"{contains}\"; last:\n{text}");
        return "";
    }

    private Task<XtermPaneSurface> WaitForOverlayRealizationAsync(Guid sessionId, string? excludeToken)
    {
        var tcs = new TaskCompletionSource<XtermPaneSurface>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _host.SurfaceRealized += (request, surface) =>
        {
            if (request.Kind == LiveSurfaceKind.Overlay &&
                request.SessionId == sessionId &&
                surface.PaneToken != excludeToken)
            {
                tcs.TrySetResult(surface);
            }
        };
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private Task<XtermPaneSurface> OpenOverlayAsync(
        Guid sessionId,
        string command,
        bool follow = false,
        bool wait = false,
        string? excludeToken = null,
        int? sizePercent = null,
        string? backgroundColor = null)
    {
        var realized = WaitForOverlayRealizationAsync(sessionId, excludeToken);
        var argv = new List<string> { "session", "overlay", "open", command, "--target", sessionId.ToString() };
        if (follow) argv.Add("--follow");
        if (wait) argv.Add("--wait");
        if (sizePercent is { } size)
        {
            argv.Add("--size-percent");
            argv.Add(size.ToString());
        }
        if (backgroundColor is not null)
        {
            argv.Add("--background-color");
            argv.Add(backgroundColor);
        }
        CtlOk(argv.ToArray());
        return realized;
    }

    private void CloseOverlayOk(Guid sessionId) =>
        CtlOk("session", "overlay", "close", "--target", sessionId.ToString());

    private Task<TerminalLayout> WaitForPublishedLayoutAsync(Func<TerminalLayout, bool> predicate)
    {
        var published = new TaskCompletionSource<TerminalLayout>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
        {
            var layout = snapshot.ToTerminalLayout(1);
            if (predicate(layout)) published.TrySetResult(layout);
        };
        return published.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // Q3.1/Q3.3: opening realizes exactly one live Overlay surface, and a duplicate open while it
    // is still up returns the exact upstream error, leaving the same surface/token in place with no
    // second realization.
    [Fact]
    public async Task Overlay_open_realizes_exact_surface_and_duplicate_open_preserves_it_with_error()
    {
        WaitForText(">"); // main shell ready
        var sessionId = ReadLocked(() => _host.Host.Store.SelectedSessionId!.Value);

        var realizedCount = 0;
        XtermPaneSurface? lastRealized = null;
        _host.SurfaceRealized += (request, surface) =>
        {
            if (request.Kind == LiveSurfaceKind.Overlay && request.SessionId == sessionId)
            {
                realizedCount++;
                lastRealized = surface;
            }
        };

        var surface = await OpenOverlayAsync(sessionId, "pause");
        Assert.Same(surface, lastRealized);
        Assert.Equal(1, realizedCount);

        var (exit, _, error) = Ctl("session", "overlay", "open", "pause", "--target", sessionId.ToString());
        Assert.NotEqual(0, exit);
        Assert.Contains("overlay already open", error);

        Assert.Equal(1, realizedCount); // the rejected duplicate never triggers a second realization
        var overlaySurfaceAfter = ReadLocked(() => _host.Host.Store.SessionWithId(sessionId)!.OverlaySurface);
        Assert.Same(surface, overlaySurfaceAfter);
        var activeAfter = ReadLocked(() => _host.Host.Store.SessionWithId(sessionId)!.OverlayActive);
        Assert.True(activeAfter);

        CloseOverlayOk(sessionId);
    }

    // Q3.1: --follow selects a background target; opening with no --follow on the same target
    // leaves the current selection unchanged (the eager deck runs the overlay in the background).
    [Fact]
    public async Task Overlay_follow_selects_background_target_while_no_follow_leaves_selection_unchanged()
    {
        WaitForText(">"); // default session's main shell ready; it stays active throughout
        var activeBefore = ReadLocked(() => _host.Host.Store.SelectedSessionId);
        var targetId = Guid.Parse(CtlOk("session", "new", "--no-select").Trim());

        await OpenOverlayAsync(targetId, "pause");
        Assert.Equal(activeBefore, ReadLocked(() => _host.Host.Store.SelectedSessionId));
        CloseOverlayOk(targetId);

        await OpenOverlayAsync(targetId, "pause", follow: true);
        Assert.Equal(targetId, ReadLocked(() => _host.Host.Store.SelectedSessionId));
        CloseOverlayOk(targetId);
    }

    // Q3.1/Q3.4: an explicit close clears the model's active/surface fields and tears down the real
    // ConPTY process rather than just hiding it (unlike split/scratch, the overlay is ephemeral).
    [Fact]
    public async Task Overlay_explicit_close_clears_active_and_surface_and_tears_down_process()
    {
        WaitForText(">");
        var sessionId = ReadLocked(() => _host.Host.Store.SelectedSessionId!.Value);

        var surface = await OpenOverlayAsync(sessionId, "pause");

        CloseOverlayOk(sessionId);

        Assert.False(ReadLocked(() => _host.Host.Store.SessionWithId(sessionId)!.OverlayActive));
        Assert.Null(ReadLocked(() => _host.Host.Store.SessionWithId(sessionId)!.OverlaySurface));

        try
        {
            await surface.Pane.Exited.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch
        {
            // a torn-down pane's exit task may fault; only that it settled matters here.
        }
    }

    // Q3.3: a natural exit with no --wait records the real ConPTY exit code, auto-closes the
    // overlay (active/surface cleared with no explicit close), and session.overlay.result reports
    // the exact recorded code.
    [Fact]
    public async Task Overlay_natural_exit_without_wait_records_exit_code_closes_surface_and_reports_it()
    {
        WaitForText(">");
        var sessionId = ReadLocked(() => _host.Host.Store.SelectedSessionId!.Value);

        var surface = await OpenOverlayAsync(sessionId, "exit 37");
        var childExitCode = await surface.Pane.Exited.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(37, childExitCode);

        WaitUntilLocked(() =>
        {
            var session = _host.Host.Store.SessionWithId(sessionId)!;
            return !session.OverlayActive && session.OverlaySurface is null;
        }, "overlay stayed active after its command exited naturally");

        Assert.Equal(37, ReadLocked(() => _host.Host.Store.SessionWithId(sessionId)!.OverlayExitCode));

        var result = CtlOk("session", "overlay", "result", "--target", sessionId.ToString());
        Assert.Contains("37", result);
    }

    // Q3.1: session.overlay.result errors "overlay still running" while a blocking overlay is up,
    // and "no overlay result" after an explicit close that happened before any status was recorded.
    [Fact]
    public async Task Overlay_result_reports_still_running_then_no_overlay_result_after_explicit_close()
    {
        WaitForText(">");
        var sessionId = ReadLocked(() => _host.Host.Store.SelectedSessionId!.Value);

        await OpenOverlayAsync(sessionId, "pause");

        var running = Ctl("session", "overlay", "result", "--target", sessionId.ToString());
        Assert.NotEqual(0, running.Exit);
        Assert.Contains("overlay still running", running.Error);

        CloseOverlayOk(sessionId);

        var afterClose = Ctl("session", "overlay", "result", "--target", sessionId.ToString());
        Assert.NotEqual(0, afterClose.Exit);
        Assert.Contains("no overlay result", afterClose.Error);
    }

    // Q3.3: --wait (per spec §"session.overlay.open": args.wait keeps the overlay up after the
    // command exits) records the real exit code but keeps active/surface retained until an explicit
    // close; only then does session.overlay.result report the recorded code.
    [Fact]
    public async Task Overlay_wait_keeps_surface_active_until_explicit_close_then_reports_recorded_code()
    {
        WaitForText(">");
        var sessionId = ReadLocked(() => _host.Host.Store.SelectedSessionId!.Value);

        var surface = await OpenOverlayAsync(sessionId, "exit 42", wait: true);
        var childExitCode = await surface.Pane.Exited.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(42, childExitCode);

        Assert.True(ReadLocked(() => _host.Host.Store.SessionWithId(sessionId)!.OverlayActive),
            "--wait must keep the overlay active after its command exits");
        Assert.Same(surface, ReadLocked(() => _host.Host.Store.SessionWithId(sessionId)!.OverlaySurface));
        Assert.False(surface.ShutdownStarted.IsCompleted,
            "process exit must not unmount a retained --wait overlay");

        var whileRetained = Ctl("session", "overlay", "result", "--target", sessionId.ToString());
        Assert.NotEqual(0, whileRetained.Exit);
        Assert.Contains("overlay still running", whileRetained.Error);

        CloseOverlayOk(sessionId);
        await surface.ShutdownStarted.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(ReadLocked(() => _host.Host.Store.SessionWithId(sessionId)!.OverlayActive));
        Assert.Equal(42, ReadLocked(() => _host.Host.Store.SessionWithId(sessionId)!.OverlayExitCode));

        var result = CtlOk("session", "overlay", "result", "--target", sessionId.ToString());
        Assert.Contains("42", result);
    }

    // Q3.4: explicitly closing overlay A while it is still running, then opening a distinct overlay
    // B on the same session, must never let A's own (now stale) natural-exit handling clobber B's
    // recorded result — the same exact-reservation-ownership discipline used for split/scratch.
    [Fact]
    public async Task Overlay_close_of_stale_surface_does_not_overwrite_result_of_new_overlay()
    {
        WaitForText(">");
        var sessionId = ReadLocked(() => _host.Host.Store.SelectedSessionId!.Value);

        var surfaceA = await OpenOverlayAsync(sessionId, "pause");
        CloseOverlayOk(sessionId);

        var surfaceB = await OpenOverlayAsync(sessionId, "exit 55", excludeToken: surfaceA.PaneToken);
        Assert.NotEqual(surfaceA.PaneToken, surfaceB.PaneToken);

        try
        {
            await surfaceA.Pane.Exited.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch
        {
            // the closed/torn-down surface's own exit task may fault; only that it settled matters.
        }

        WaitUntilLocked(() =>
        {
            var session = _host.Host.Store.SessionWithId(sessionId)!;
            return !session.OverlayActive && session.OverlaySurface is null;
        }, "overlay B stayed active after its command exited naturally");

        Assert.Equal(55, ReadLocked(() => _host.Host.Store.SessionWithId(sessionId)!.OverlayExitCode));

        var result = CtlOk("session", "overlay", "result", "--target", sessionId.ToString());
        Assert.Contains("55", result);
    }

    // Q3.2: the overlay command runs through cmd's shell semantics (COMSPEC /c), so operators like
    // && work as shell text rather than being split as literal argv; the overlay wire has no --pane
    // selector, so the output is read through the live surface object directly.
    [Fact]
    public async Task Overlay_command_shell_operators_work_and_output_is_readable_through_the_live_surface()
    {
        WaitForText(">");
        var sessionId = ReadLocked(() => _host.Host.Store.SelectedSessionId!.Value);

        var surface = await OpenOverlayAsync(sessionId, "echo FIRST && echo SECOND && pause");

        var text = WaitForOverlayText(surface, "SECOND");
        Assert.Contains("FIRST", text);
        Assert.Contains("SECOND", text);

        CloseOverlayOk(sessionId);
    }

    [Fact]
    public async Task Overlay_open_resize_full_and_close_publish_layouts_without_respawn()
    {
        WaitForText(">");
        var sessionId = ReadLocked(() => _host.Host.Store.SelectedSessionId!.Value);
        var openedLayout = WaitForPublishedLayoutAsync(layout =>
            layout is TerminalLayout.Layered
            {
                Layer.SizePercent: null,
                Layer.BackgroundColor: "#2a1a3a",
            });
        var realized = WaitForOverlayRealizationAsync(sessionId, excludeToken: null);

        CtlOk(
            "session", "overlay", "open", "pause",
            "--background-color", "#2a1a3a",
            "--target", sessionId.ToString());
        var surface = await realized;
        var full = Assert.IsType<TerminalLayout.Layered>(await openedLayout);
        Assert.Equal(surface.PaneToken, full.Layer.PaneId);
        Assert.IsType<TerminalLayout.Single>(full.BaseLayout);

        var floatingLayout = WaitForPublishedLayoutAsync(layout =>
            layout is TerminalLayout.Layered { Layer.SizePercent: 60 });
        CtlOk(
            "session", "overlay", "resize", "--size-percent", "60",
            "--target", sessionId.ToString());
        var floating = Assert.IsType<TerminalLayout.Layered>(await floatingLayout);
        Assert.Equal(surface.PaneToken, floating.Layer.PaneId);
        Assert.Equal("#2a1a3a", floating.Layer.BackgroundColor);
        Assert.Same(surface, ReadLocked(() => _host.Host.Store.SessionWithId(sessionId)!.OverlaySurface));

        var restoredFullLayout = WaitForPublishedLayoutAsync(layout =>
            layout is TerminalLayout.Layered { Layer.SizePercent: null });
        CtlOk("session", "overlay", "resize", "--full", "--target", sessionId.ToString());
        var restoredFull = Assert.IsType<TerminalLayout.Layered>(await restoredFullLayout);
        Assert.Equal(surface.PaneToken, restoredFull.Layer.PaneId);
        Assert.Same(surface, ReadLocked(() => _host.Host.Store.SessionWithId(sessionId)!.OverlaySurface));

        var closedLayout = WaitForPublishedLayoutAsync(layout => layout is TerminalLayout.Single);
        CloseOverlayOk(sessionId);
        Assert.IsType<TerminalLayout.Single>(await closedLayout);
    }

    [Fact]
    public async Task Overlay_covers_active_scratch_and_close_reveals_same_scratch_layer()
    {
        WaitForText(">");
        var sessionId = ReadLocked(() => _host.Host.Store.SelectedSessionId!.Value);
        var scratchRealized = new TaskCompletionSource<XtermPaneSurface>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _host.SurfaceRealized += (request, surface) =>
        {
            if (request.Kind == LiveSurfaceKind.Scratch && request.SessionId == sessionId)
                scratchRealized.TrySetResult(surface);
        };
        var scratchLayout = WaitForPublishedLayoutAsync(layout =>
            layout is TerminalLayout.Layered
            {
                Layer.SizePercent: null,
                Layer.BackgroundColor: null,
            });

        CtlOk("session", "scratch", "on", "--target", sessionId.ToString());
        var scratchSurface = await scratchRealized.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var shownScratch = Assert.IsType<TerminalLayout.Layered>(await scratchLayout);
        Assert.Equal(scratchSurface.PaneToken, shownScratch.Layer.PaneId);

        var overlayLayout = WaitForPublishedLayoutAsync(layout =>
            layout is TerminalLayout.Layered
            {
                Layer.SizePercent: 70,
                Layer.BackgroundColor: "#113355",
            });
        var overlaySurface = await OpenOverlayAsync(
            sessionId,
            "pause",
            sizePercent: 70,
            backgroundColor: "#113355");
        var shownOverlay = Assert.IsType<TerminalLayout.Layered>(await overlayLayout);
        Assert.Equal(overlaySurface.PaneToken, shownOverlay.Layer.PaneId);

        var revealedScratchLayout = WaitForPublishedLayoutAsync(layout =>
            layout is TerminalLayout.Layered layered && layered.Layer.PaneId == scratchSurface.PaneToken);
        CloseOverlayOk(sessionId);
        var revealedScratch = Assert.IsType<TerminalLayout.Layered>(await revealedScratchLayout);
        Assert.Equal(scratchSurface.PaneToken, revealedScratch.Layer.PaneId);

        CtlOk("session", "scratch", "off", "--target", sessionId.ToString());
    }

    [Fact]
    public void Overlay_block_waits_for_live_command_and_returns_its_exit_code()
    {
        WaitForText(">");
        var sessionId = ReadLocked(() => _host.Host.Store.SelectedSessionId!.Value);

        var blocked = Ctl(
            "session", "overlay", "open", "exit 23",
            "--block",
            "--target", sessionId.ToString());

        Assert.Equal(23, blocked.Exit);
        Assert.Equal(string.Empty, blocked.Error);
        Assert.False(ReadLocked(() => _host.Host.Store.SessionWithId(sessionId)!.OverlayActive));
        Assert.Equal(23, ReadLocked(() => _host.Host.Store.SessionWithId(sessionId)!.OverlayExitCode));
    }

    [Fact]
    public async Task Overlay_wait_close_immediately_after_exit_preserves_result()
    {
        WaitForText(">");
        var sessionId = ReadLocked(() => _host.Host.Store.SelectedSessionId!.Value);
        var surface = await OpenOverlayAsync(sessionId, "exit 67", wait: true);

        Assert.Equal(67, await surface.Pane.Exited.WaitAsync(TimeSpan.FromSeconds(15)));
        CloseOverlayOk(sessionId);

        var result = CtlOk("session", "overlay", "result", "--target", sessionId.ToString());
        Assert.Contains("67", result);
    }
}
