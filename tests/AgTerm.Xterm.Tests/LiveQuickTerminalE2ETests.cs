using System.Text.Json.Nodes;
using AgTerm.Cli;
using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// Live quick contract over real ConPTY and a dedicated WebView2 page.
/// </summary>
[Collection("webview2")]
public sealed class LiveQuickTerminalE2ETests : IDisposable
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-live-quick-").FullName;
    private readonly LiveControlHost _host;
    private readonly Lazy<Task<XtermPage>> _page;
    private readonly Lazy<Task<XtermPage>> _quickPage;

    public LiveQuickTerminalE2ETests(WebView2Fixture webView)
    {
        _webView = webView;
        _page = new Lazy<Task<XtermPage>>(() => _webView.CreatePageAsync());
        _quickPage = new Lazy<Task<XtermPage>>(() => _webView.CreatePageAsync());
        _host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-quick-" + Guid.NewGuid().ToString("N"),
            DefaultShell = Cmd,
            HomeDirectory = _stateDir,
            OpenSurface = async (request, cancellationToken) =>
            {
                var shared = await (request.Kind == LiveSurfaceKind.Quick ? _quickPage.Value : _page.Value);
                return await _webView.RunAsync(async _ =>
                {
                    var surface = await XtermPaneSurface.OpenAsync(shared, request.Options, cancellationToken);
                    if (request.Kind == LiveSurfaceKind.Quick)
                        await shared.ApplyLayoutAsync(new TerminalLayout.Single(1, surface.PaneToken!, "Quick"));
                    return surface;
                });
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

    private T ReadLocked<T>(Func<T> read)
    {
        var result = default(T)!;
        _host.Host.RunLocked(() => result = read());
        return result;
    }

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

    private string WaitForQuickText(XtermPaneSurface surface, string contains, int timeoutMs = 15000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var text = "";
        while (Environment.TickCount64 < deadline)
        {
            text = surface.ReadScreenText(all: true, lines: null) ?? "";
            if (text.Contains(contains)) return text;
            Thread.Sleep(100);
        }
        Assert.Fail($"quick surface never contained \"{contains}\"; last:\n{text}");
        return "";
    }

    private Task<(SurfaceOpenRequest Request, XtermPaneSurface Surface)> SubscribeNextQuickRealizationAsync(
        string? excludeToken = null)
    {
        var tcs = new TaskCompletionSource<(SurfaceOpenRequest, XtermPaneSurface)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _host.SurfaceRealized += (request, surface) =>
        {
            if (request.Kind == LiveSurfaceKind.Quick && surface.PaneToken != excludeToken)
                tcs.TrySetResult((request, surface));
        };
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private JsonNode TreeJson() =>
        JsonNode.Parse(CtlOk("tree", "--json"))!["result"]!["tree"]!;

    // Q5.1(1): quick.type and quick.text before any quick show return the exact "not open" wire errors.
    [Fact]
    public void Quick_type_and_text_before_show_return_not_open_errors()
    {
        WaitForText(">"); // main shell ready; quick was never shown

        var typeResult = Ctl("quick", "type", "marker");
        Assert.NotEqual(0, typeResult.Exit);
        Assert.Contains("quick terminal not open", typeResult.Error);

        var textResult = Ctl("quick", "text", "--all");
        Assert.NotEqual(0, textResult.Exit);
        Assert.Contains("quick terminal not open", textResult.Error);
    }

    // Q5.1(2): quick show dispatch waits for realization; back-to-back type and text work immediately
    // after show returns; tree reports quickVisible=true while shown and false after hide.
    [Fact]
    public async Task Quick_show_type_and_text_work_back_to_back_and_tree_reports_quickVisible()
    {
        WaitForText(">");

        var realization = SubscribeNextQuickRealizationAsync();
        CtlOk("quick", "show"); // dispatch waits for realization before returning
        var (_, surface) = await realization;

        CtlOk("quick", "type", "echo Q5-marker\n");
        WaitForQuickText(surface, "Q5-marker");

        var textResult = Ctl("quick", "text", "--all");
        Assert.Equal(0, textResult.Exit);
        Assert.Contains("Q5-marker", textResult.Out);

        Assert.Equal(true, TreeJson()["quickVisible"]?.GetValue<bool>());

        CtlOk("quick", "hide");
        Assert.Equal(false, TreeJson()["quickVisible"]?.GetValue<bool>());
    }

    // Q5.1(3): quick hide keeps the surface alive; a second show reuses the same pane token;
    // a hidden but realized Quick remains addressable by type and text.
    [Fact]
    public async Task Quick_hide_retains_surface_show_reuses_token_and_hidden_quick_is_addressable()
    {
        WaitForText(">");

        var firstRealization = SubscribeNextQuickRealizationAsync();
        CtlOk("quick", "show");
        var (_, firstSurface) = await firstRealization;
        var firstToken = firstSurface.PaneToken;

        CtlOk("quick", "type", "echo hide-show-marker\n");
        WaitForQuickText(firstSurface, "hide-show-marker");

        CtlOk("quick", "hide");

        // Surface is retained while hidden: type and text must work without a second show.
        CtlOk("quick", "type", "echo hidden-type-marker\n");
        WaitForQuickText(firstSurface, "hidden-type-marker");
        var hiddenText = Ctl("quick", "text", "--all");
        Assert.Equal(0, hiddenText.Exit);
        Assert.Contains("hide-show-marker", hiddenText.Out);

        // Show again: the same surface must be reused (no new SurfaceRealized for Quick).
        var secondRealizationCount = 0;
        _host.SurfaceRealized += (req, _) => { if (req.Kind == LiveSurfaceKind.Quick) secondRealizationCount++; };
        CtlOk("quick", "show");
        Assert.Equal(0, secondRealizationCount);

        var tokenAfterSecondShow = ReadLocked(() =>
            (_host.Host.Actions.Quick.Surface as XtermPaneSurface)?.PaneToken);
        Assert.Equal(firstToken, tokenAfterSecondShow);

        CtlOk("quick", "hide");
    }

    // Q5.1(4): while quick is visible with a pending surface, quick.type returns the exact
    // "quick terminal not realized" error and quick.text returns "failed to read surface buffer".
    // A failed realization clears the slot — Quick.Surface is never set to the failed surface.
    [Fact]
    public async Task Quick_visible_pending_reports_exact_errors_and_failed_realization_does_not_attach()
    {
        WaitForText(">"); // ensure the shared WebView2 fixture is warmed up

        var gate = new TaskCompletionSource<IHeadlessSurface>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tempDir = Directory.CreateTempSubdirectory("agterm-quick-pending-").FullName;
        try
        {
            using var controlledHost = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = tempDir,
                ConfigDir = tempDir,
                PipeName = "agterm-quick-pending-" + Guid.NewGuid().ToString("N"),
                DefaultShell = Cmd,
                HomeDirectory = tempDir,
                RealizeBudget = TimeSpan.FromMinutes(1),
                OpenSurface = (request, ct) =>
                {
                    // Only Quick realizations block; the default main session uses an immediate fake.
                    if (request.Kind == LiveSurfaceKind.Quick)
                        return gate.Task.WaitAsync(ct);
                    return Task.FromResult<IHeadlessSurface>(new FakeSurface());
                },
            });
            controlledHost.Start();

            var pendingSnapshot = new TaskCompletionSource<LiveSelectionSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var settledSnapshot = new TaskCompletionSource<LiveSelectionSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var unrealizedSnapshotCount = 0;
            controlledHost.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
            {
                if (!snapshot.QuickVisible || snapshot.QuickPaneId is not null)
                    return;

                if (Interlocked.Increment(ref unrealizedSnapshotCount) == 1)
                    pendingSnapshot.TrySetResult(snapshot);
                else
                    settledSnapshot.TrySetResult(snapshot);
            };

            // Start quick show in background; it blocks until realization completes (or gate released).
            var showTask = Task.Run(() =>
            {
                var stdout = new StringWriter();
                var stderr = new StringWriter();
                var exit = Agtermctl.Run(new[] { "quick", "show" }, new CliEnvironment
                {
                    GetEnv = k => k == "AGTERM_CONTROL_SOCKET" ? controlledHost.PipeName : null,
                    Out = stdout,
                    Error = stderr,
                });
                return (exit, stdout.ToString(), stderr.ToString());
            });

            // Wait until the pending surface is assigned (Quick.Visible=true, Surface=PendingTerminalSurface).
            var pendingDeadline = Environment.TickCount64 + 15000;
            while (Environment.TickCount64 < pendingDeadline)
            {
                bool ready = false;
                controlledHost.Host.RunLocked(() =>
                    ready = controlledHost.Host.Actions.Quick.Visible &&
                            controlledHost.Host.Actions.Quick.Surface is not null);
                if (ready) break;
                Thread.Sleep(50);
            }
            Assert.True(
                ReadLockedOn(controlledHost,
                    () => controlledHost.Host.Actions.Quick.Visible &&
                          controlledHost.Host.Actions.Quick.Surface is not null),
                "Quick never became visible+pending");
            await pendingSnapshot.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // While pending: type returns exact "quick terminal not realized".
            var typeResponse = await controlledHost.Host.Actions.TypeQuick("text");
            Assert.False(typeResponse.Ok);
            Assert.Equal("quick terminal not realized", typeResponse.Error);

            // While pending: text returns exact "failed to read surface buffer".
            var textResponse = await controlledHost.Host.Actions.ReadQuickText(all: true, lines: null);
            Assert.False(textResponse.Ok);
            Assert.Equal("failed to read surface buffer", textResponse.Error);

            // Release gate with failure: the coordinator clears the slot (does not attach the surface).
            gate.SetException(new InvalidOperationException("test-induced failure"));
            await showTask.WaitAsync(TimeSpan.FromSeconds(15));
            var failedSnapshot = await settledSnapshot.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(failedSnapshot.QuickVisible);
            Assert.Null(failedSnapshot.QuickPaneId);

            // After failed realization Quick.Surface must NOT be a real XtermPaneSurface.
            IHeadlessSurface? surfaceAfter = null;
            controlledHost.Host.RunLocked(() => surfaceAfter = controlledHost.Host.Actions.Quick.Surface);
            Assert.False(surfaceAfter is XtermPaneSurface,
                "A failed realization must not attach a real surface to Quick.Surface");
            var failedTypeResponse = await controlledHost.Host.Actions.TypeQuick("text");
            Assert.False(failedTypeResponse.Ok);
            Assert.Equal("quick terminal not realized", failedTypeResponse.Error);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { }
        }
    }

    private static bool ReadLockedOn(LiveControlHost host, Func<bool> predicate)
    {
        var result = false;
        host.Host.RunLocked(() => result = predicate());
        return result;
    }

    [Fact]
    public async Task Quick_show_is_allowed_under_session_zoom_and_hide_clears_only_panel_zoom()
    {
        WaitForText(">");

        var realization = SubscribeNextQuickRealizationAsync();
        CtlOk("quick", "show");
        await realization;
        CtlOk("quick", "hide");

        // Zoom the active session surface.
        CtlOk("surface", "zoom", "show");
        Assert.NotNull(ReadLocked(() => _host.Host.Actions.Zoom.Target));

        CtlOk("quick", "show");
        Assert.True(ReadLocked(() => _host.Host.Actions.Quick.Visible));

        var newRealizationCount = 0;
        _host.SurfaceRealized += (req, _) => { if (req.Kind == LiveSurfaceKind.Quick) newRealizationCount++; };
        CtlOk("quick", "show");
        Assert.Equal(0, newRealizationCount);
        Assert.True(ReadLocked(() => _host.Host.Actions.Quick.Visible));

        // Zoom the quick terminal itself.
        CtlOk("surface", "zoom", "show", "--target", "quick");
        Assert.True(_host.Host.Actions.Quick.IsZoomed);
        Assert.NotNull(ReadLocked(() => _host.Host.Actions.Zoom.Target));

        // quick hide while quick is zoomed: clears quick zoom first, then hides.
        CtlOk("quick", "hide");
        Assert.False(ReadLocked(() => _host.Host.Actions.Quick.Visible));
        Assert.False(_host.Host.Actions.Quick.IsZoomed);
        Assert.NotNull(ReadLocked(() => _host.Host.Actions.Zoom.Target));
    }

    // Q5.1(6): quick visibility and surface must not be restored when a new host starts from the
    // same state directory (Quick is window-scoped and is never persisted).
    [Fact]
    public async Task Quick_visibility_and_surface_not_restored_across_restart()
    {
        WaitForText(">");

        var realization = SubscribeNextQuickRealizationAsync();
        CtlOk("quick", "show");
        await realization;

        _host.Host.RunLocked(() => _host.Host.Store.Save());

        var page2Task = _webView.CreatePageAsync();
        using var host2 = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-quick-restart-" + Guid.NewGuid().ToString("N"),
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

        bool visibleAfterRestart = true;
        bool surfacePresentAfterRestart = true;
        host2.Host.RunLocked(() =>
        {
            visibleAfterRestart = host2.Host.Actions.Quick.Visible;
            surfacePresentAfterRestart = host2.Host.Actions.Quick.Surface is not null;
        });
        Assert.False(visibleAfterRestart, "quick terminal must not be visible after restart");
        Assert.False(surfacePresentAfterRestart, "quick surface must not survive a restart");
    }

    // Q5.1: a natural shell exit must hide the quick terminal (Quick.Visible=false), clear
    // Quick.Surface (null), and the next `quick show` must create a fresh surface with a new
    // pane token.
    [Fact]
    public async Task Quick_shell_exit_hides_clears_and_next_show_realizes_fresh_surface()
    {
        WaitForText(">"); // ensure a main session exists before quick show

        // Subscribe before show so the realization cannot be missed.
        var firstRealization = SubscribeNextQuickRealizationAsync();

        CtlOk("quick", "show");

        var (firstRequest, firstSurface) = await firstRealization;
        Assert.Equal(LiveSurfaceKind.Quick, firstRequest.Kind);
        Assert.Null(firstRequest.SessionId);

        // Wait for the quick shell prompt before sending exit.
        WaitForQuickText(firstSurface, ">");

        CtlOk("quick", "type", "exit\n");

        WaitUntilLocked(
            () => !_host.Host.Actions.Quick.Visible && _host.Host.Actions.Quick.Surface is null,
            "Quick.Visible never became false and Quick.Surface never cleared after shell exit");

        // Show again; a fresh surface with a new pane token must be realized.
        var secondRealization = SubscribeNextQuickRealizationAsync(excludeToken: firstSurface.PaneToken);
        CtlOk("quick", "show");
        var (_, secondSurface) = await secondRealization;

        Assert.NotEqual(firstSurface.PaneToken, secondSurface.PaneToken);
    }

    // Q5 window scope: Quick is per-window, not per-session, and uses HomeDirectory when there
    // is no active session from which to inherit a working directory.
    [Fact]
    public async Task Quick_show_without_sessions_realizes_window_surface_from_home()
    {
        WaitForText(">"); // ensure the default main shell is ready before closing it

        // Close the only session so there is no active session.
        CtlOk("session", "close");

        WaitUntilLocked(
            () => _host.Host.Store.ActiveSession is null,
            "Active session never cleared after session close");

        var realization = SubscribeNextQuickRealizationAsync();

        CtlOk("quick", "show");

        var (request, _) = await realization;
        Assert.Equal(LiveSurfaceKind.Quick, request.Kind);
        Assert.Null(request.SessionId);
        Assert.Equal(_stateDir, request.Options.Cwd);
    }

    [Fact]
    public async Task Session_switch_keeps_the_same_quick_surface_and_initial_cwd()
    {
        WaitForText(">");

        var firstRealization = SubscribeNextQuickRealizationAsync();
        CtlOk("quick", "show");
        var (firstRequest, firstSurface) = await firstRealization;
        Assert.Equal(_stateDir, firstRequest.Options.Cwd);

        var additionalQuickRealizations = 0;
        _host.SurfaceRealized += (request, _) =>
        {
            if (request.Kind == LiveSurfaceKind.Quick)
                Interlocked.Increment(ref additionalQuickRealizations);
        };

        var secondCwd = Directory.CreateDirectory(Path.Combine(_stateDir, "second-cwd")).FullName;
        CtlOk("session", "new", "--cwd", secondCwd);

        var currentSurface = ReadLocked(() => _host.Host.Actions.Quick.Surface);
        Assert.Same(firstSurface, currentSurface);
        Assert.Equal(0, Volatile.Read(ref additionalQuickRealizations));
        Assert.Equal(_stateDir, firstRequest.Options.Cwd);
    }

    // Snapshot visibility drives chrome; quick never becomes part of the ordinary layout.
    [Fact]
    public async Task Quick_snapshot_carries_QuickVisible_and_QuickPaneId_through_show_hide()
    {
        WaitForText(">");

        LiveSelectionSnapshot? shownSnapshot = null;
        LiveSelectionSnapshot? hiddenSnapshot = null;

        var shownTcs = new TaskCompletionSource<LiveSelectionSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var hiddenTcs = new TaskCompletionSource<LiveSelectionSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
        {
            if (snapshot.QuickVisible && !string.IsNullOrEmpty(snapshot.QuickPaneId))
                shownTcs.TrySetResult(snapshot);
            else if (shownSnapshot is not null && !snapshot.QuickVisible &&
                     snapshot.QuickPaneId == shownSnapshot.QuickPaneId)
                hiddenTcs.TrySetResult(snapshot);
        };

        var realization = SubscribeNextQuickRealizationAsync();
        CtlOk("quick", "show");
        await realization;

        shownSnapshot = await shownTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(shownSnapshot.QuickVisible);
        Assert.False(string.IsNullOrEmpty(shownSnapshot.QuickPaneId),
            "QuickPaneId must be nonblank after realization");

        var shownLayout = shownSnapshot.ToTerminalLayout(1);
        Assert.IsNotType<TerminalLayout.Layered>(shownLayout);

        CtlOk("quick", "hide");

        hiddenSnapshot = await hiddenTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(hiddenSnapshot.QuickVisible);
        // Keep-alive: the same pane id must remain even after hide.
        Assert.Equal(shownSnapshot.QuickPaneId, hiddenSnapshot.QuickPaneId);

        // ToTerminalLayout must yield the non-Quick base layout when Quick is hidden.
        var hiddenLayout = hiddenSnapshot.ToTerminalLayout(2);
        Assert.IsNotType<TerminalLayout.Layered>(hiddenLayout);
    }

    [Fact]
    public async Task The_detached_quick_terminal_keeps_a_pending_terminal_question_rendered()
    {
        WaitForText(">");
        var hidden = new TaskCompletionSource<LiveSelectionSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var back = new TaskCompletionSource<LiveSelectionSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
        {
            if (snapshot.QuickVisible && !string.IsNullOrEmpty(snapshot.QuickPaneId))
            {
                if (snapshot.TerminalAsk is not null) hidden.TrySetResult(snapshot);
            }
            else if (hidden.Task.IsCompleted && !snapshot.QuickVisible && snapshot.TerminalAsk is not null)
            {
                back.TrySetResult(snapshot);
            }
        };
        var ask = new PendingAsk("ASK", "Continue?", null, new[] { new ControlAskButton { Id = "yes", Label = "Yes" } },
            null, null, ControlAskStyle.Terminal, ControlAskAlignment.Right, null);
        _host.Host.RunLocked(() => _host.Host.Store.OpenAsk(
            _host.Host.Store.SelectedSessionId!.Value, ask, null, Guid.NewGuid()));

        var realization = SubscribeNextQuickRealizationAsync();
        CtlOk("quick", "show");
        await realization;
        await hidden.Task.WaitAsync(TimeSpan.FromSeconds(10));

        CtlOk("quick", "hide");
        var restored = await back.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(ask, restored.TerminalAsk!.Ask);
    }

    // Q5.3: host API toggle show → CLI type/text → CLI hide → host API toggle show → same token.
    // Verifies ToggleQuickTerminalAsync shows when hidden, integrates with CLI I/O, and keeps
    // the surface alive across a CLI hide so the second host-API show reuses the same pane token.
    [Fact]
    public async Task ToggleQuickTerminalAsync_show_then_cli_type_text_cli_hide_show_reuses_token()
    {
        WaitForText(">");

        var firstRealization = SubscribeNextQuickRealizationAsync();
        await _host.ToggleQuickTerminalAsync(); // show via host API
        var (_, firstSurface) = await firstRealization;
        var firstToken = firstSurface.PaneToken;

        // CLI quick.type and text against the realized surface.
        CtlOk("quick", "type", "echo Q5-3-marker\n");
        WaitForQuickText(firstSurface, "Q5-3-marker");

        var textResult = Ctl("quick", "text", "--all");
        Assert.Equal(0, textResult.Exit);
        Assert.Contains("Q5-3-marker", textResult.Out);

        // CLI hide.
        CtlOk("quick", "hide");
        Assert.False(ReadLocked(() => _host.Host.Actions.Quick.Visible));

        // Host API show again: keep-alive, no new realization, same token.
        var secondRealizationCount = 0;
        _host.SurfaceRealized += (req, _) => { if (req.Kind == LiveSurfaceKind.Quick) secondRealizationCount++; };
        await _host.ToggleQuickTerminalAsync();
        Assert.Equal(0, secondRealizationCount);

        var tokenAfterSecondShow = ReadLocked(() =>
            (_host.Host.Actions.Quick.Surface as XtermPaneSurface)?.PaneToken);
        Assert.Equal(firstToken, tokenAfterSecondShow);

        CtlOk("quick", "hide");
    }

    [Fact]
    public async Task ToggleQuickTerminalAsync_works_under_session_and_panel_zoom()
    {
        WaitForText(">");

        // Establish and hide quick to ensure the surface is retained.
        var realization = SubscribeNextQuickRealizationAsync();
        CtlOk("quick", "show");
        await realization;
        CtlOk("quick", "hide");

        // Activate a session-surface zoom while Quick is hidden.
        CtlOk("surface", "zoom", "show");
        Assert.NotNull(ReadLocked(() => _host.Host.Actions.Zoom.Target));

        await _host.ToggleQuickTerminalAsync();
        Assert.True(ReadLocked(() => _host.Host.Actions.Quick.Visible));

        // Clear session zoom.
        CtlOk("surface", "zoom", "hide");
        Assert.Null(ReadLocked(() => _host.Host.Actions.Zoom.Target));

        // Show quick via CLI for the second leg.
        CtlOk("quick", "show");
        Assert.True(ReadLocked(() => _host.Host.Actions.Quick.Visible));

        // Zoom Quick itself.
        CtlOk("surface", "zoom", "show", "--target", "quick");
        Assert.True(_host.Host.Actions.Quick.IsZoomed);

        await _host.ToggleQuickTerminalAsync();
        Assert.False(ReadLocked(() => _host.Host.Actions.Quick.Visible));
        Assert.False(_host.Host.Actions.Quick.IsZoomed);

        // Cleanup.
        CtlOk("surface", "zoom", "hide");
        CtlOk("quick", "hide");
    }
}
