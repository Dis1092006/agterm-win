using AgTerm.Cli;
using AgTerm.Core.Model;
using AgTerm.Xterm;
using System.Text.Json;

namespace AgTerm.Xterm.Tests;

/// <summary>Live wire, ConPTY, and shared-page coverage for split I/O, focus, resize, restore,
/// natural exit, and primary-pane promotion.</summary>
[Collection("webview2")]
public sealed class LiveSplitE2ETests : IDisposable
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-live-split-").FullName;
    private readonly LiveControlHost _host;
    private readonly Lazy<Task<XtermPage>> _page;

    public LiveSplitE2ETests(WebView2Fixture webView)
    {
        _webView = webView;
        _page = new Lazy<Task<XtermPage>>(() => _webView.CreatePageAsync());
        _host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-split-" + Guid.NewGuid().ToString("N"),
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

    private string WaitForText(string contains, string? pane = null, int timeoutMs = 30000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var text = "";
        string[] args = pane is null
            ? ["session", "text", "--all"]
            : ["session", "text", "--all", "--pane", pane];
        while (Environment.TickCount64 < deadline)
        {
            text = Ctl(args).Out;
            if (text.Contains(contains)) return text;
            Thread.Sleep(100);
        }
        Assert.Fail($"session text{(pane is null ? "" : " --pane " + pane)} never contained \"{contains}\"; last:\n{text}");
        return "";
    }

    private Task ReportCwdAsync(XtermPaneSurface surface, string cwd)
    {
        var paneId = JsonSerializer.Serialize(surface.PaneToken);
        var uri = JsonSerializer.Serialize(cwd);
        return _webView.RunAsync(async _ =>
            await surface.Pane.Page.ExecuteScriptAsync(
                $"window.chrome.webview.postMessage({{type:'cwd',paneId:{paneId},uri:{uri}}})"));
    }

    /// <summary>Waits for the exact split surface created by the split command.</summary>
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

    [Fact]
    public async Task Split_pane_title_and_cwd_reports_route_to_split_fields()
    {
        WaitForText(">"); // main shell ready
        var splitSurface = await EnableSplitAndWaitForRealizationAsync();
        WaitForText(">", pane: "right"); // split shell ready

        var marker = "SPLIT-TITLE-" + Guid.NewGuid().ToString("N")[..8];
        CtlOk("session", "type", "--pane", "right", $"title {marker}\n");

        var deadline = Environment.TickCount64 + 30000;
        string? seen = null;
        while (Environment.TickCount64 < deadline)
        {
            _host.Host.RunLocked(() =>
            {
                var session = _host.Host.Store.Workspaces
                    .SelectMany(w => w.Sessions)
                    .FirstOrDefault();
                seen = session?.SplitTitle;
            });
            if (seen is not null && seen.Contains(marker)) break;
            Thread.Sleep(100);
        }
        Assert.Contains(marker, seen);

        var splitCwd = Path.Combine(_stateDir, "split-cwd");
        await ReportCwdAsync(splitSurface, splitCwd);
        deadline = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < deadline)
        {
            _host.Host.RunLocked(() =>
            {
                var session = _host.Host.Store.ActiveSession;
                seen = session?.SplitCwd;
            });
            if (seen == splitCwd) return;
            Thread.Sleep(50);
        }
        Assert.Equal(splitCwd, seen);
    }

    [Fact]
    public async Task Split_pane_exit_collapses_split()
    {
        WaitForText(">"); // main shell ready
        await EnableSplitAndWaitForRealizationAsync();
        WaitForText(">", pane: "right"); // split shell ready

        CtlOk("session", "type", "--pane", "right", "exit\n");

        var deadline = Environment.TickCount64 + 30000;
        bool collapsed = false;
        while (Environment.TickCount64 < deadline)
        {
            _host.Host.RunLocked(() =>
            {
                var session = _host.Host.Store.Workspaces
                    .SelectMany(w => w.Sessions)
                    .FirstOrDefault();
                collapsed = session is not null && !session.HasSplit && session.SplitSurface is null;
            });
            if (collapsed) return;
            Thread.Sleep(100);
        }
        Assert.Fail("split never collapsed after the split pane exited: HasSplit or SplitSurface stayed set");
    }

    // P3 behavior: left and right pane I/O are independent — typing to one pane does not appear
    // on the other pane's screen.
    [Fact]
    public async Task Left_and_right_pane_type_text_stay_independent()
    {
        WaitForText(">"); // main shell ready
        await EnableSplitAndWaitForRealizationAsync();
        WaitForText(">", pane: "right"); // split shell ready

        var leftMarker = "LEFT-" + Guid.NewGuid().ToString("N")[..6];
        var rightMarker = "RIGHT-" + Guid.NewGuid().ToString("N")[..6];
        CtlOk("session", "type", "--pane", "left", $"echo {leftMarker}\n");
        CtlOk("session", "type", "--pane", "right", $"echo {rightMarker}\n");

        WaitForText(leftMarker, pane: "left");       // left pane shows its marker
        WaitForText(rightMarker, pane: "right");      // right pane shows its marker

        // cross-check: left screen must NOT contain the right marker (and vice versa)
        var leftText = Ctl("session", "text", "--all", "--pane", "left").Out;
        Assert.DoesNotContain(rightMarker, leftText);

        var rightText = Ctl("session", "text", "--all", "--pane", "right").Out;
        Assert.DoesNotContain(leftMarker, rightText);
    }

    // P3 behavior: toggling split off leaves the right ConPTY process and its screen intact. When
    // the split is re-enabled, the same pane token and screen content are still present.
    [Fact]
    public async Task Split_hide_show_keeps_right_pane_alive_with_same_screen()
    {
        WaitForText(">"); // main shell ready
        var splitSurface = await EnableSplitAndWaitForRealizationAsync();
        WaitForText(">", pane: "right");

        var marker = "KEEP-ALIVE-" + Guid.NewGuid().ToString("N")[..6];
        CtlOk("session", "type", "--pane", "right", $"echo {marker}\n");
        WaitForText(marker, pane: "right"); // marker painted on right screen

        var tokenBefore = splitSurface.PaneToken;
        CtlOk("session", "split", "off"); // hide the split — right ConPTY keeps running
        CtlOk("session", "split", "on");  // re-show — must reuse the existing surface

        // same token
        string? tokenAfter = null;
        _host.Host.RunLocked(() =>
        {
            var session = _host.Host.Store.Workspaces.SelectMany(w => w.Sessions).FirstOrDefault();
            tokenAfter = session?.SplitSurface?.PaneToken;
        });
        Assert.Equal(tokenBefore, tokenAfter);

        // same screen content still visible
        WaitForText(marker, pane: "right");
    }

    // P3 behavior: session.focus left/right updates Session.SplitFocused in the model.
    [Fact]
    public async Task Session_focus_left_clears_SplitFocused_and_focus_right_sets_it()
    {
        WaitForText(">"); // main shell ready
        await EnableSplitAndWaitForRealizationAsync();

        CtlOk("session", "focus", "right");
        bool splitFocused = false;
        _host.Host.RunLocked(() =>
        {
            var session = _host.Host.Store.Workspaces.SelectMany(w => w.Sessions).FirstOrDefault();
            splitFocused = session?.SplitFocused ?? false;
        });
        Assert.True(splitFocused);

        CtlOk("session", "focus", "left");
        _host.Host.RunLocked(() =>
        {
            var session = _host.Host.Store.Workspaces.SelectMany(w => w.Sessions).FirstOrDefault();
            splitFocused = session?.SplitFocused ?? true;
        });
        Assert.False(splitFocused);
    }

    // P3 behavior: session.resize --split-ratio sets the persisted split ratio.
    [Fact]
    public async Task Session_resize_sets_split_ratio_in_model()
    {
        WaitForText(">"); // main shell ready
        await EnableSplitAndWaitForRealizationAsync();

        CtlOk("session", "resize", "--split-ratio", "0.3");

        double? ratio = null;
        _host.Host.RunLocked(() =>
        {
            var session = _host.Host.Store.Workspaces.SelectMany(w => w.Sessions).FirstOrDefault();
            ratio = session?.SplitRatio;
        });
        Assert.NotNull(ratio);
        Assert.InRange(ratio!.Value, 0.29, 0.31);
    }

    [Fact]
    public async Task Duplicate_bridge_focus_reports_publish_one_model_transition()
    {
        WaitForText(">");
        var splitSurface = await EnableSplitAndWaitForRealizationAsync();
        WaitForText(">", pane: "right");
        CtlOk("session", "focus", "left");

        var mainPaneId = await _host.StoreCoordinator.ReadAsync(
            store => store.ActiveSession!.Surface!.PaneToken!);
        var splitPaneId = splitSurface.PaneToken!;
        var page = await _page.Value;
        var focusTasks = new List<Task<bool>>();
        var focusTasksGate = new object();
        var rawFocusReports = 0;
        var snapshotTransitions = 0;
        var duplicateReportsReceived = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var transitioned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        page.PaneFocusChanged += paneId =>
        {
            var task = _host.ApplyPaneFocusAsync(paneId);
            lock (focusTasksGate) focusTasks.Add(task);
            if (Interlocked.Increment(ref rawFocusReports) >= 2)
                duplicateReportsReceived.TrySetResult();
        };
        _host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
        {
            if (!snapshot.SplitFocused) return;
            Interlocked.Increment(ref snapshotTransitions);
            transitioned.TrySetResult();
        };

        await _webView.RunAsync<object?>(async _ =>
        {
            Assert.True(await page.ApplyLayoutAsync(
                new TerminalLayout.Split(1, mainPaneId, splitPaneId, 0.5, splitPaneId)));
            await page.ExecuteScriptAsync(
                $"window.chrome.webview.postMessage({{type:'paneFocus',paneId:{JsonSerializer.Serialize(splitPaneId)}}})");
            return null;
        });

        await duplicateReportsReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<bool>[] completedFocusTasks;
        lock (focusTasksGate) completedFocusTasks = focusTasks.ToArray();
        await Task.WhenAll(completedFocusTasks);
        await transitioned.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(200);

        Assert.True(Volatile.Read(ref rawFocusReports) >= 2);
        Assert.Equal(1, Volatile.Read(ref snapshotTransitions));
    }

    // P3 behavior: a session saved with IsSplit=true restores both Main and Split panes.
    [Fact]
    public async Task Restored_shown_split_realizes_both_panes()
    {
        WaitForText(">"); // main shell ready
        await EnableSplitAndWaitForRealizationAsync();
        WaitForText(">", pane: "right");

        // Persist the current state (IsSplit=true) explicitly before creating the second host.
        _host.Host.RunLocked(() => _host.Host.Store.Save());

        var page2Task = _webView.CreatePageAsync();

        using var host2 = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-split-restore-" + Guid.NewGuid().ToString("N"),
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

        var deadline = Environment.TickCount64 + 30000;
        while (Environment.TickCount64 < deadline)
        {
            var realized = await host2.StoreCoordinator.ReadAsync(store =>
            {
                var session = store.ActiveSession;
                return session?.Surface is XtermPaneSurface &&
                       session.SplitSurface is XtermPaneSurface;
            });
            if (realized) return;
            Thread.Sleep(100);
        }
        Assert.Fail("Restored shown split did not realize both pane surfaces");
    }

    [Fact]
    public async Task Main_pane_exit_promotes_split_and_future_reports_write_main_fields()
    {
        WaitForText(">"); // main shell ready
        var splitSurface = await EnableSplitAndWaitForRealizationAsync();
        WaitForText(">", pane: "right"); // split shell ready

        var splitToken = splitSurface.PaneToken;
        Assert.NotNull(splitToken);

        // Exit the main (left) pane — ClosePrimaryPane promotes the split.
        CtlOk("session", "type", "exit\n");

        // Wait for promotion: session.Surface becomes the old split surface.
        var deadline = Environment.TickCount64 + 30000;
        bool promoted = false;
        while (Environment.TickCount64 < deadline)
        {
            _host.Host.RunLocked(() =>
            {
                var session = _host.Host.Store.Workspaces.SelectMany(w => w.Sessions).FirstOrDefault();
                promoted = session?.Surface?.PaneToken == splitToken && session.SplitSurface is null;
            });
            if (promoted) break;
            Thread.Sleep(100);
        }
        Assert.True(promoted, "Main exit did not promote the split surface into the primary slot");

        // Type a title command to the promoted pane (now the only pane; default target = left).
        var marker = "PROMOTED-" + Guid.NewGuid().ToString("N")[..8];
        CtlOk("session", "type", $"title {marker}\n");

        deadline = Environment.TickCount64 + 30000;
        string? seen = null;
        while (Environment.TickCount64 < deadline)
        {
            _host.Host.RunLocked(() =>
            {
                var session = _host.Host.Store.Workspaces.SelectMany(w => w.Sessions).FirstOrDefault();
                seen = session?.OscTitle;
            });
            if (seen is not null && seen.Contains(marker)) break;
            Thread.Sleep(100);
        }
        Assert.Contains(marker, seen);

        var promotedCwd = Path.Combine(_stateDir, "promoted-cwd");
        await ReportCwdAsync(splitSurface, promotedCwd);
        deadline = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < deadline)
        {
            _host.Host.RunLocked(() => seen = _host.Host.Store.ActiveSession?.CurrentCwd);
            if (seen == promotedCwd) return;
            Thread.Sleep(50);
        }
        Assert.Equal(promotedCwd, seen);
    }

    [Fact]
    public async Task Promoted_split_pane_exit_closes_session()
    {
        WaitForText(">"); // main shell ready
        var splitSurface = await EnableSplitAndWaitForRealizationAsync();
        WaitForText(">", pane: "right");

        var splitToken = splitSurface.PaneToken;
        Assert.NotNull(splitToken);
        var sessionId = Guid.Empty;
        _host.Host.RunLocked(() =>
        {
            sessionId = _host.Host.Store.Workspaces.SelectMany(w => w.Sessions).First().Id;
        });

        // Exit the main pane to trigger promotion.
        CtlOk("session", "type", "exit\n");

        var deadline = Environment.TickCount64 + 30000;
        bool promoted = false;
        while (Environment.TickCount64 < deadline)
        {
            _host.Host.RunLocked(() =>
            {
                var s = _host.Host.Store.SessionWithId(sessionId);
                promoted = s?.Surface?.PaneToken == splitToken && s.SplitSurface is null;
            });
            if (promoted) break;
            Thread.Sleep(100);
        }
        Assert.True(promoted, "Main exit did not promote the split surface");

        // Exit the promoted pane — must close the session entirely.
        CtlOk("session", "type", "exit\n");

        deadline = Environment.TickCount64 + 30000;
        while (Environment.TickCount64 < deadline)
        {
            bool gone = false;
            _host.Host.RunLocked(() => gone = _host.Host.Store.SessionWithId(sessionId) is null);
            if (gone) return;
            Thread.Sleep(100);
        }
        Assert.Fail("Session not removed from store after the promoted pane exited");
    }

    [Fact]
    public async Task Promoted_main_exit_after_resplit_promotes_the_fresh_right_pane()
    {
        WaitForText(">");
        var firstSplit = await EnableSplitAndWaitForRealizationAsync();
        WaitForText(">", pane: "right");

        CtlOk("session", "type", "--pane", "left", "exit\n");
        var deadline = Environment.TickCount64 + 30000;
        while (Environment.TickCount64 < deadline)
        {
            var promoted = false;
            _host.Host.RunLocked(() =>
            {
                var session = _host.Host.Store.ActiveSession;
                promoted = ReferenceEquals(session?.Surface, firstSplit) && session.SplitSurface is null;
            });
            if (promoted) break;
            Thread.Sleep(50);
        }

        var nextSplitReady = new TaskCompletionSource<XtermPaneSurface>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _host.SurfaceRealized += (request, surface) =>
        {
            if (request.Kind == LiveSurfaceKind.Split)
                nextSplitReady.TrySetResult(surface);
        };
        CtlOk("session", "split", "on");
        var nextSplit = await nextSplitReady.Task.WaitAsync(TimeSpan.FromSeconds(15));
        WaitForText(">", pane: "right");

        CtlOk("session", "type", "--pane", "left", "exit\n");
        deadline = Environment.TickCount64 + 30000;
        while (Environment.TickCount64 < deadline)
        {
            var promoted = false;
            _host.Host.RunLocked(() =>
            {
                var session = _host.Host.Store.ActiveSession;
                promoted = ReferenceEquals(session?.Surface, nextSplit) && session.SplitSurface is null;
            });
            if (promoted) return;
            Thread.Sleep(50);
        }

        Assert.Fail("Fresh right pane was not promoted after the promoted main pane exited");
    }
}
