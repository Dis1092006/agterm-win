using System.Text.Json;
using AgTerm.Cli;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// RED tests for Task Q4 wire/addon integration and ownership. `HeadlessControlActions.SearchSession`
/// today only counts the needle in a C# buffer copy and pins `Session.SearchSurface`; it never calls
/// into the real xterm search addon or the bridge search bar that Q4.1/Q4.2 added
/// (`agterm.openSearch`/`closeSearch`). These tests pin the missing production wiring against the
/// REAL LiveControlHost/shared WebView2/ConPTY/agtermctl stack. They are expected to fail until that
/// wiring exists; some also reference `LiveSelectionSnapshot` search fields that do not exist yet and
/// are expected to fail to COMPILE.
/// </summary>
[Collection("webview2")]
public sealed class LiveSearchE2ETests : IDisposable
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-live-search-").FullName;
    private readonly LiveControlHost _host;
    private readonly Lazy<Task<XtermPage>> _page;

    public LiveSearchE2ETests(WebView2Fixture webView)
    {
        _webView = webView;
        _page = new Lazy<Task<XtermPage>>(() => _webView.CreatePageAsync());
        _host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-search-" + Guid.NewGuid().ToString("N"),
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

    private string WaitForText(string contains, string? pane = null, int timeoutMs = 30000, int occurrences = 1)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var text = "";
        string[] args = pane is null
            ? ["session", "text", "--all"]
            : ["session", "text", "--all", "--pane", pane];
        while (Environment.TickCount64 < deadline)
        {
            text = Ctl(args).Out;
            if (text.Split(contains, StringSplitOptions.None).Length > occurrences) return text;
            Thread.Sleep(100);
        }
        Assert.Fail($"session text{(pane is null ? "" : " --pane " + pane)} never contained \"{contains}\"; last:\n{text}");
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

    private string MainPaneToken()
    {
        var token = ReadSessionField(s => s.Surface?.PaneToken);
        Assert.NotNull(token);
        return token!;
    }

    private T ReadSessionField<T>(Func<Session, T> selector)
    {
        var value = default(T)!;
        _host.Host.RunLocked(() =>
        {
            var session = _host.Host.Store.Workspaces.SelectMany(w => w.Sessions).FirstOrDefault();
            value = selector(session!);
        });
        return value;
    }

    /// <summary>Reads the DOM search bar (added by Q4.1) as it stands, whichever pane parents it.</summary>
    private async Task<JsonElement> SearchBarStateAsync(XtermPage page)
    {
        var json = await _webView.RunAsync(async _ => await page.ExecuteScriptAsync(
            """
            (() => {
                const bar = document.getElementById('search-bar');
                const input = document.getElementById('search-input');
                const status = document.getElementById('search-status');
                return {
                    hidden: bar ? bar.hidden : true,
                    ownerPaneId: bar && !bar.hidden ? (bar.dataset.paneId ?? null) : null,
                    value: input ? input.value : null,
                    status: status ? status.textContent : null,
                    inputFocused: !!input && document.activeElement === input,
                };
            })()
            """));
        return JsonDocument.Parse(json).RootElement;
    }

    private async Task WaitForSearchBarHiddenAsync(XtermPage page, int timeoutMs = 10000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if ((await SearchBarStateAsync(page)).GetProperty("hidden").GetBoolean())
                return;
            await Task.Delay(50);
        }
        Assert.Fail("search bar stayed visible after its owning pane closed");
    }

    /// <summary>Requirement 1: the not-yet-existing addon inspection API. `apiType` documents the
    /// current undefined shape until production exposes `agterm.getSearchState(paneId)`.</summary>
    private async Task<JsonElement> GetSearchStateShapeAsync(XtermPage page, string paneId)
    {
        var script =
            "(() => { const apiType = typeof agterm.getSearchState; " +
            "if (apiType !== 'function') return { apiType }; " +
            $"return {{ apiType, state: agterm.getSearchState({JsonSerializer.Serialize(paneId)}) }}; }})()";
        var json = await _webView.RunAsync(async _ => await page.ExecuteScriptAsync(script));
        return JsonDocument.Parse(json).RootElement;
    }

    // Requirement 1: real wire `session search <needle>` must drive the exact main pane's existing
    // xterm search addon, not only the C# buffer count — and the model total must match the addon's
    // own count via the (not-yet-existing) `agterm.getSearchState` inspection API.
    [Fact]
    public async Task Wire_search_drives_the_owner_panes_search_addon_and_matches_model_total()
    {
        var page = await _page.Value;
        WaitForText(">");
        var mainToken = MainPaneToken();

        var marker = "NEEDLE-" + Guid.NewGuid().ToString("N")[..6];
        CtlOk("session", "type", $"echo {marker} {marker} {marker}\n");
        WaitForText(marker, occurrences: 6);

        CtlOk("session", "search", marker);
        var modelTotal = ReadSessionField(s => s.SearchTotal);
        Assert.True(modelTotal >= 3);

        var shape = await GetSearchStateShapeAsync(page, mainToken);
        Assert.Equal("function", shape.GetProperty("apiType").GetString());
        var state = shape.GetProperty("state");
        Assert.Equal(marker, state.GetProperty("needle").GetString());
        Assert.Equal(modelTotal, state.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Output_after_search_open_updates_model_from_addon_results()
    {
        var page = await _page.Value;
        page.SearchResultsChanged += (paneId, needle, count) =>
            _ = _host.ApplySearchTotalAsync(paneId, needle, count);
        WaitForText(">");
        var paneId = MainPaneToken();
        var marker = "LATE-" + Guid.NewGuid().ToString("N")[..6];

        CtlOk("session", "type", $"ping -n 8 127.0.0.1 > nul & echo {marker} {marker}\n");
        WaitForText(marker, occurrences: 2);
        CtlOk("session", "search", marker);
        Assert.Equal(2, ReadSessionField(session => session.SearchTotal));

        WaitForText(marker, occurrences: 4);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline &&
               ReadSessionField(session => session.SearchTotal) != 4)
            await Task.Delay(50);

        Assert.Equal(4, ReadSessionField(session => session.SearchTotal));
        Assert.Equal("4 matches", ReadSessionField(session => session.SearchDisplayText));
        await WaitForSearchStatusAsync(page, "4 matches");
        var state = (await GetSearchStateShapeAsync(page, paneId)).GetProperty("state");
        Assert.Equal(4, state.GetProperty("count").GetInt32());

        Assert.Equal("1 of 4", CtlOk("session", "search", "--next").Trim());
        await WaitForSearchStatusAsync(page, "1 of 4");
        CtlOk("session", "type", $"echo {marker}\n");
        var selectedDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < selectedDeadline &&
               ReadSessionField(session => session.SearchTotal) != 6)
            await Task.Delay(50);
        Assert.Equal(6, ReadSessionField(session => session.SearchTotal));
        Assert.Equal(1, ReadSessionField(session => session.SearchSelected));
        await WaitForSearchStatusAsync(page, "1 of 6");
    }

    // Ported from upstream ControlSurfaceZoomUITests.testZoomingBackgroundTargetEndsItsSearch.
    [Fact]
    public async Task Zooming_background_target_ends_its_search_before_reselection()
    {
        var page = await _page.Value;
        WaitForText(">");
        var sessionId = ReadSessionField(session => session.Id);
        var paneId = MainPaneToken();
        var presentation = new SelectionPresentationQueue(async (before, current, next) =>
        {
            await _webView.RunAsync(async _ =>
            {
                Assert.True(await page.ApplyLayoutAsync(current.ToTerminalLayout(next)));
                var transition = SearchPresentationTransition.Resolve(before, current, current.MainPaneId);
                switch (transition)
                {
                    case SearchPresentationTransition.Show show:
                        await page.ApplySearchAsync(
                            show.PaneId, show.Needle, show.DisplayText, show.Selected, show.FocusInput);
                        break;
                    case SearchPresentationTransition.Hide hide:
                        await page.HideSearchAsync(hide.ClearOwnerPaneId, hide.RestorePaneId);
                        break;
                }
            });
        });

        async Task<string> CtlAndPresentAsync(
            Func<LiveSelectionSnapshot, bool> expected,
            params string[] argv)
        {
            var presented = presentation.WaitForAsync(expected);
            var output = CtlOk(argv);
            await presented;
            return output;
        }

        _host.StoreCoordinator.SelectionSnapshotChanged += presentation.Record;
        try
        {
            await CtlAndPresentAsync(
                snapshot => snapshot.SessionId == sessionId && snapshot.SearchActive
                    && snapshot.SearchNeedle == "login" && snapshot.SearchOwnerPaneId == paneId,
                "session", "search", "login");
            Assert.True(ReadSessionField(session => session.SearchActive));
            Assert.False((await SearchBarStateAsync(page)).GetProperty("hidden").GetBoolean());
            Assert.Equal(paneId, (await SearchBarStateAsync(page)).GetProperty("ownerPaneId").GetString());

            var other = (await CtlAndPresentAsync(
                snapshot => snapshot.SessionId is { } id && id != sessionId,
                "session", "new")).Trim();
            var otherId = Guid.Parse(other);
            Assert.NotEqual(sessionId, otherId);
            Assert.True(ReadSessionField(session => session.SearchActive));
            var target = new TerminalSurfaceId(sessionId, TerminalZoomSurface.Primary).RawValue;
            await CtlAndPresentAsync(
                snapshot => snapshot.SessionId == otherId && snapshot.ZoomPaneId == paneId,
                "surface", "zoom", "show", "--target", target);
            await CtlAndPresentAsync(
                snapshot => snapshot.SessionId == otherId && snapshot.ZoomPaneId is null,
                "surface", "zoom", "hide");
            await CtlAndPresentAsync(
                snapshot => snapshot.SessionId == sessionId && !snapshot.SearchActive
                    && snapshot.SearchOwnerPaneId is null,
                "session", "select", "--target", sessionId.ToString("D"));

            Assert.False(ReadSessionField(session => session.SearchActive));
            Assert.Null(ReadSessionField(session => session.SearchSurface));
            Assert.True((await SearchBarStateAsync(page)).GetProperty("hidden").GetBoolean());
            var search = (await GetSearchStateShapeAsync(page, paneId)).GetProperty("state");
            Assert.Equal("", search.GetProperty("needle").GetString());
        }
        finally
        {
            _host.StoreCoordinator.SelectionSnapshotChanged -= presentation.Record;
            await presentation.FlushAsync();
        }
    }

    private async Task WaitForSearchStatusAsync(XtermPage page, string expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        string? last = null;
        while (DateTime.UtcNow < deadline)
        {
            var state = await SearchBarStateAsync(page);
            last = state.GetProperty("status").GetString();
            if (last == expected)
            {
                Assert.True(state.GetProperty("inputFocused").GetBoolean());
                return;
            }
            await Task.Delay(50);
        }
        Assert.Fail($"search status did not reach '{expected}'; last: '{last}'");
    }

    [Fact]
    public async Task Wire_empty_needle_clears_same_addon_state_and_model_count()
    {
        var page = await _page.Value;
        WaitForText(">");
        var mainToken = MainPaneToken();
        var marker = "CLEAR-" + Guid.NewGuid().ToString("N")[..6];
        CtlOk("session", "type", $"echo {marker}\n");
        WaitForText(marker);
        CtlOk("session", "search", marker);

        CtlOk("session", "search", "");

        Assert.Null(ReadSessionField(session => session.SearchTotal));
        Assert.Null(ReadSessionField(session => session.SearchSelected));
        var shape = await GetSearchStateShapeAsync(page, mainToken);
        var state = shape.GetProperty("state");
        Assert.Equal("", state.GetProperty("needle").GetString());
        Assert.Equal(0, state.GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("selected").ValueKind);
    }

    [Fact]
    public async Task Wire_search_addon_uses_same_ordinal_case_policy_as_model_count()
    {
        var page = await _page.Value;
        WaitForText(">");
        var mainToken = MainPaneToken();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var exact = "CaseNeedle" + suffix;
        var otherCase = exact.ToLowerInvariant();
        CtlOk("session", "type", $"echo {exact} {otherCase}\n");
        WaitForText(exact, occurrences: 2);

        CtlOk("session", "search", exact);

        var modelTotal = ReadSessionField(session => session.SearchTotal);
        var state = (await GetSearchStateShapeAsync(page, mainToken)).GetProperty("state");
        Assert.Equal(modelTotal, state.GetProperty("count").GetInt32());
    }

    // Requirement 2: next/prev must keep driving the PINNED owner even after split focus moves
    // elsewhere, and the wire response text must be exactly Session.SearchDisplayText.
    [Fact]
    public async Task Search_next_prev_keep_pinned_owner_after_split_focus_change()
    {
        var page = await _page.Value;
        WaitForText(">");
        var mainToken = MainPaneToken();
        await EnableSplitAndWaitForRealizationAsync();
        WaitForText(">", pane: "right");

        var marker = "PIN-" + Guid.NewGuid().ToString("N")[..6];
        CtlOk("session", "type", "--pane", "left", $"echo {marker} {marker}\n");
        WaitForText(marker, pane: "left");

        // opens on the main pane — the on-screen surface before any split focus change.
        CtlOk("session", "focus", "left");
        CtlOk("session", "search", marker);
        Assert.Equal(mainToken, ReadSessionField(s => s.SearchSurface?.PaneToken));

        // moving split focus must NOT move an already-open search bar to the newly focused pane.
        CtlOk("session", "focus", "right");
        var (exit, output, error) = Ctl("session", "search", "--next");
        Assert.True(exit == 0, $"session search --next failed: {error}");
        Assert.Equal(mainToken, ReadSessionField(s => s.SearchSurface?.PaneToken));
        Assert.Equal(ReadSessionField(s => s.SearchDisplayText), output.Trim());

        // production must still be driving the addon bound to the pinned owner, not the focused
        // split pane.
        var barState = await SearchBarStateAsync(page);
        Assert.False(barState.GetProperty("hidden").GetBoolean());
        Assert.Equal(mainToken, barState.GetProperty("ownerPaneId").GetString());
    }

    // Requirement 3: wire close must clear the addon decorations and the model owner, must not
    // select a background target, and must not steal focus from the visible terminal.
    [Fact]
    public async Task Search_close_clears_addon_and_owner_without_stealing_visible_focus()
    {
        var page = await _page.Value;
        WaitForText(">");
        var mainToken = MainPaneToken();

        CtlOk("session", "search", "abc");
        Assert.Equal(mainToken, ReadSessionField(s => s.SearchSurface?.PaneToken));

        // baseline: production must have actually opened the real bar on the main pane, so the
        // close assertions below test a real transition rather than a no-op.
        var openedState = await SearchBarStateAsync(page);
        Assert.False(openedState.GetProperty("hidden").GetBoolean());
        Assert.Equal(mainToken, openedState.GetProperty("ownerPaneId").GetString());

        CtlOk("session", "search", "--close");
        Assert.Null(ReadSessionField(s => s.SearchSurface));

        var closedState = await SearchBarStateAsync(page);
        Assert.True(closedState.GetProperty("hidden").GetBoolean());

        var focusJson = await _webView.RunAsync(async _ => await page.ExecuteScriptAsync(
            "(() => ({ terminalFocused: !!document.activeElement && document.activeElement.classList.contains('xterm-helper-textarea') }))()"));
        using var focusDoc = JsonDocument.Parse(focusJson);
        Assert.True(focusDoc.RootElement.GetProperty("terminalFocused").GetBoolean());
    }

    [Fact]
    public async Task Search_close_on_background_session_keeps_visible_session_selected_and_focused()
    {
        var page = await _page.Value;
        WaitForText(">");
        var backgroundSessionId = ReadSessionField(session => session.Id);
        CtlOk("session", "search", "abc");

        var visibleSessionId = Guid.Parse(CtlOk("session", "new").Trim());
        var visiblePaneId = "";
        _host.Host.RunLocked(() =>
            visiblePaneId = _host.Host.Store.SessionWithId(visibleSessionId)!.Surface!.PaneToken!);
        await _webView.RunAsync<object?>(async _ =>
        {
            Assert.True(await page.ApplyLayoutAsync(
                new TerminalLayout.Single(100, visiblePaneId, "visible")));
            return null;
        });

        CtlOk(
            "session", "search", "--close",
            "--target", backgroundSessionId.ToString());

        Guid? selected = null;
        _host.Host.RunLocked(() => selected = _host.Host.Store.SelectedSessionId);
        Assert.Equal(visibleSessionId, selected);
        var focusJson = await _webView.RunAsync(async _ => await page.ExecuteScriptAsync(
            """
            (() => ({
              barHidden: document.getElementById('search-bar').hidden,
              focusedPaneId: document.activeElement
                ? document.activeElement.closest('[data-pane-id]')?.dataset.paneId ?? null
                : null,
            }))()
            """));
        using var focusDoc = JsonDocument.Parse(focusJson);
        Assert.True(focusDoc.RootElement.GetProperty("barHidden").GetBoolean());
        Assert.Equal(visiblePaneId, focusDoc.RootElement.GetProperty("focusedPaneId").GetString());
    }

    [Fact]
    public async Task Closing_background_search_does_not_hide_selected_sessions_search_bar()
    {
        var page = await _page.Value;
        WaitForText(">");
        var backgroundSessionId = ReadSessionField(session => session.Id);
        CtlOk("session", "search", "background-needle");

        var selectedSessionId = Guid.Parse(CtlOk("session", "new").Trim());
        CtlOk(
            "session", "search", "selected-needle",
            "--target", selectedSessionId.ToString());
        string selectedOwnerPaneId = "";
        _host.Host.RunLocked(() => selectedOwnerPaneId = _host.Host.Store
            .SessionWithId(selectedSessionId)!.SearchSurface!.PaneToken!);

        CtlOk(
            "session", "search", "--close",
            "--target", backgroundSessionId.ToString());

        var bar = await SearchBarStateAsync(page);
        Assert.False(bar.GetProperty("hidden").GetBoolean());
        Assert.Equal(selectedOwnerPaneId, bar.GetProperty("ownerPaneId").GetString());
        Assert.Equal("selected-needle", bar.GetProperty("value").GetString());
    }

    // Requirement 4: the immutable selection snapshot must eventually carry the selected session's
    // search fields (owner pane token, active, needle, SearchDisplayText) so the window can display
    // a wire-opened search. `LiveSelectionSnapshot` does not have these members yet — the named
    // arguments below are expected to fail to COMPILE, and that compile failure IS the RED signal
    // for this requirement; it is intentionally localized to this one test method.
    [Fact]
    public void Selection_snapshot_carries_owner_pane_active_needle_and_display_text()
    {
        WaitForText(">");
        var mainToken = MainPaneToken();
        CtlOk("session", "search", "abc");

        var snapshot = new LiveSelectionSnapshot(
            SessionId: Guid.NewGuid(),
            DisplayName: "any",
            SearchOwnerPaneId: mainToken,
            SearchActive: true,
            SearchNeedle: "abc",
            SearchDisplayText: ReadSessionField(s => s.SearchDisplayText));

        Assert.Equal(mainToken, snapshot.SearchOwnerPaneId);
        Assert.True(snapshot.SearchActive);
        Assert.Equal("abc", snapshot.SearchNeedle);
        Assert.Equal(ReadSessionField(s => s.SearchDisplayText), snapshot.SearchDisplayText);
    }

    // Requirement 5: the active scratch (OnScreenSurface) must be chosen as the search owner when a
    // NEW search opens while scratch covers the panes; an ALREADY-open main search must not be
    // retargeted by scratch becoming active. Ownership follows existing Core rules
    // (Session.OnScreenSurface); this test pins the bridge/addon side, which is unwired today.
    [Fact]
    public async Task Active_scratch_is_chosen_as_search_owner_and_main_search_is_not_retargeted()
    {
        var page = await _page.Value;
        WaitForText(">");
        var mainToken = MainPaneToken();

        // 1) main search opens first, while nothing else is on screen.
        CtlOk("session", "search", "main-needle");
        Assert.Equal(mainToken, ReadSessionField(s => s.SearchSurface?.PaneToken));

        // 2) scratch becomes the on-screen surface; Core keeps the ALREADY-open search pinned to
        // main (SearchSurface is reassigned only when no search is active yet).
        var scratchSurface = await EnableScratchAndWaitForRealizationAsync();
        WaitForText(">", pane: "scratch");
        Assert.Equal(mainToken, ReadSessionField(s => s.SearchSurface?.PaneToken));

        // production must still be driving the addon bound to the already-open main owner — the
        // bar must not be silently reparented under scratch.
        var barAfterScratch = await SearchBarStateAsync(page);
        Assert.Equal(mainToken, barAfterScratch.GetProperty("ownerPaneId").GetString());

        // 3) close, then reopen while scratch covers the panes: the NEW owner must be the scratch
        // surface (OnScreenSurface), matching existing Core selection rules.
        CtlOk("session", "search", "--close");
        CtlOk("session", "search", "scratch-needle");
        Assert.Equal(scratchSurface.PaneToken, ReadSessionField(s => s.SearchSurface?.PaneToken));

        var barOnScratch = await SearchBarStateAsync(page);
        Assert.Equal(scratchSurface.PaneToken, barOnScratch.GetProperty("ownerPaneId").GetString());
    }

    // Requirement 6: a search owned by the split pane must survive primary-pane exit promotion,
    // still bound to the SAME pane token (Core already preserves Session.SearchSurface across
    // ClosePrimaryPane when the survivor owns the bar); the promoted owner's close must clear
    // search. This test pins the bridge/addon side surviving the same transition.
    [Fact]
    public async Task Split_owner_search_survives_primary_exit_promotion_with_same_pane_token()
    {
        var page = await _page.Value;
        WaitForText(">");
        var splitSurface = await EnableSplitAndWaitForRealizationAsync();
        WaitForText(">", pane: "right");
        var splitToken = splitSurface.PaneToken!;

        CtlOk("session", "focus", "right");
        CtlOk("session", "search", "split-needle");
        Assert.Equal(splitToken, ReadSessionField(s => s.SearchSurface?.PaneToken));

        var barBeforeExit = await SearchBarStateAsync(page);
        Assert.Equal(splitToken, barBeforeExit.GetProperty("ownerPaneId").GetString());

        // exit the main (left) pane — Core promotes the split survivor into the primary slot.
        CtlOk("session", "type", "exit\n");
        var deadline = Environment.TickCount64 + 15000;
        while (Environment.TickCount64 < deadline)
        {
            if (ReadSessionField(s => s.Surface?.PaneToken) == splitToken &&
                ReadSessionField(s => s.SplitSurface) is null)
                break;
            Thread.Sleep(100);
        }
        Assert.Equal(splitToken, ReadSessionField(s => s.Surface?.PaneToken));

        // the search must still be driving the SAME pane id/token after promotion.
        Assert.Equal(splitToken, ReadSessionField(s => s.SearchSurface?.PaneToken));
        var barAfterPromotion = await SearchBarStateAsync(page);
        Assert.Equal(splitToken, barAfterPromotion.GetProperty("ownerPaneId").GetString());

        // closing the (now-primary) owner must clear search.
        CtlOk("session", "search", "--close");
        Assert.Null(ReadSessionField(s => s.SearchSurface));
        var barAfterClose = await SearchBarStateAsync(page);
        Assert.True(barAfterClose.GetProperty("hidden").GetBoolean());
    }

    [Fact]
    public async Task Searched_split_exit_clears_owner_and_hides_bar()
    {
        var page = await _page.Value;
        WaitForText(">");
        await EnableSplitAndWaitForRealizationAsync();
        WaitForText(">", pane: "right");
        CtlOk("session", "focus", "right");
        CtlOk("session", "search", "needle");

        CtlOk("session", "type", "--pane", "right", "exit\n");

        var deadline = Environment.TickCount64 + 15000;
        while (Environment.TickCount64 < deadline && ReadSessionField(session => session.SearchActive))
            Thread.Sleep(100);
        Assert.False(ReadSessionField(session => session.SearchActive));
        Assert.Null(ReadSessionField(session => session.SearchSurface));
        await WaitForSearchBarHiddenAsync(page);
    }

    [Fact]
    public async Task Searched_scratch_exit_clears_owner_and_hides_bar()
    {
        var page = await _page.Value;
        WaitForText(">");
        await EnableScratchAndWaitForRealizationAsync();
        WaitForText(">", pane: "scratch");
        CtlOk("session", "search", "needle");

        CtlOk("session", "type", "--pane", "scratch", "exit\n");

        var deadline = Environment.TickCount64 + 15000;
        while (Environment.TickCount64 < deadline && ReadSessionField(session => session.SearchActive))
            Thread.Sleep(100);
        Assert.False(ReadSessionField(session => session.SearchActive));
        Assert.Null(ReadSessionField(session => session.SearchSurface));
        await WaitForSearchBarHiddenAsync(page);
    }
}
