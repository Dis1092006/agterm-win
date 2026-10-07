using System.Text;
using System.Text.Json;
using AgTerm.App;
using AgTerm.Core.Keymap;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// Bridge v2 against the REAL page: one WebView2, many xterm.js instances. Verifies per-pane
/// isolation (buffers, titles), show/hide refits, and pane teardown — the live twin of the
/// FakeScriptHost routing tests.
/// </summary>
[Collection("webview2")]
public class XtermPageLiveTests
{
    private readonly WebView2Fixture _webView;

    public XtermPageLiveTests(WebView2Fixture webView)
    {
        _webView = webView;
    }

    private async Task<(XtermPage Page, Microsoft.Web.WebView2.WinForms.WebView2 View)> BootPageAsync()
    {
        var view = await _webView.CreateWebViewAsync();
        var page = await _webView.RunAsync(async _ =>
        {
            var p = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await p.InitializeAsync();
            return p;
        });
        return (page, view);
    }

    private static async Task<string> WaitForTextAsync(XtermTerminal terminal, string contains,
        Func<Func<Task<string>>, Task<string>> onUi, int timeoutMs = 30000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var text = "";
        while (Environment.TickCount64 < deadline)
        {
            text = await onUi(() => terminal.ReadTextAsync(all: true, lines: null));
            if (text.Contains(contains)) return text;
            await Task.Delay(50);
        }
        Assert.Fail($"pane text never contained \"{contains}\"; buffer:\n{text}");
        return "";
    }

        [Fact]
        public async Task Search_bar_opens_for_exact_pane_with_model_display_text_and_input_focus()
        {
                var (page, view) = await BootPageAsync();
                await _webView.RunAsync(_ => page.OpenTerminalAsync("search-owner"));

                var state = await _webView.RunAsync(async _ =>
                        await view.CoreWebView2.ExecuteScriptAsync(
                                """
                                (() => {
                                    const apiType = typeof agterm.openSearch;
                                    if (apiType !== 'function') return { apiType };
                                    const accepted = agterm.openSearch('search-owner', 'needle', '2 of 5');
                                    const bar = document.getElementById('search-bar');
                                    const input = document.getElementById('search-input');
                                    const status = document.getElementById('search-status');
                                    return {
                                        apiType,
                                        accepted,
                                        hidden: bar ? bar.hidden : null,
                                        owner: bar ? bar.dataset.paneId : null,
                                        value: input ? input.value : null,
                                        status: status ? status.textContent : null,
                                        inputLabel: input ? input.getAttribute('aria-label') : null,
                                        focused: document.activeElement === input,
                                    };
                                })()
                                """));

                using var json = JsonDocument.Parse(state);
                var root = json.RootElement;
                Assert.Equal("function", root.GetProperty("apiType").GetString());
                Assert.True(root.GetProperty("accepted").GetBoolean());
                Assert.False(root.GetProperty("hidden").GetBoolean());
                Assert.Equal("search-owner", root.GetProperty("owner").GetString());
                Assert.Equal("needle", root.GetProperty("value").GetString());
                Assert.Equal("2 of 5", root.GetProperty("status").GetString());
                Assert.Equal("Find", root.GetProperty("inputLabel").GetString());
                Assert.True(root.GetProperty("focused").GetBoolean());
        }

            [Fact]
            public async Task Search_bar_input_navigation_and_escape_report_exact_owner_and_restore_terminal_focus()
            {
                var (page, view) = await BootPageAsync();
                var terminal = await _webView.RunAsync(_ => page.OpenTerminalAsync("search-events"));
                await _webView.RunAsync<object?>(_ =>
                {
                    terminal.FeedOutput(Encoding.UTF8.GetBytes("needle one needle two\r\n"));
                    return Task.FromResult<object?>(null);
                });
                await WaitForTextAsync(terminal, "needle two", work => _webView.RunAsync(_ => work()));

                var queryUpdated = new TaskCompletionSource<(string PaneId, string Needle, int Count, int? Selected)>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var navigated = new TaskCompletionSource<(string PaneId, string Needle, int Count, int? Selected)>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var closed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                page.SearchUpdated += (paneId, needle, count, selected) =>
                {
                    var update = (paneId, needle, count, selected);
                    if (selected is null) queryUpdated.TrySetResult(update);
                    else navigated.TrySetResult(update);
                };
                page.SearchClosed += paneId => closed.TrySetResult(paneId);

                await _webView.RunAsync<object?>(async _ =>
                {
                    await view.CoreWebView2.ExecuteScriptAsync(
                        "agterm.openSearch('search-events', '', '', true)");
                    await view.CoreWebView2.ExecuteScriptAsync(
                        """
                        (() => {
                          const input = document.getElementById('search-input');
                          input.value = 'needle';
                          input.dispatchEvent(new Event('input', { bubbles: true }));
                        })()
                        """);
                    return null;
                });

                var first = await queryUpdated.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal("search-events", first.PaneId);
                Assert.Equal("needle", first.Needle);
                Assert.True(first.Count >= 2);
                Assert.Null(first.Selected);

                await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
                    "document.getElementById('search-input').dispatchEvent(new KeyboardEvent('keydown', {key:'Enter', bubbles:true}))"));
                var next = await navigated.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal("search-events", next.PaneId);
                Assert.Equal("needle", next.Needle);
                Assert.True(next.Selected > 0);

                await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
                    "document.getElementById('search-input').dispatchEvent(new KeyboardEvent('keydown', {key:'Escape', bubbles:true}))"));
                Assert.Equal("search-events", await closed.Task.WaitAsync(TimeSpan.FromSeconds(10)));

                var focusState = await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
                    """
                    (() => ({
                      hidden: document.getElementById('search-bar').hidden,
                      inputFocused: document.activeElement === document.getElementById('search-input'),
                      terminalFocused: document.activeElement && document.activeElement.classList.contains('xterm-helper-textarea'),
                    }))()
                    """));
                using var focusJson = JsonDocument.Parse(focusState);
                Assert.True(focusJson.RootElement.GetProperty("hidden").GetBoolean());
                Assert.False(focusJson.RootElement.GetProperty("inputFocused").GetBoolean());
                Assert.True(focusJson.RootElement.GetProperty("terminalFocused").GetBoolean());
            }

            [Fact]
            public async Task Search_bar_shift_enter_and_buttons_drive_navigation_and_close()
            {
                var (page, view) = await BootPageAsync();
                var terminal = await _webView.RunAsync(_ => page.OpenTerminalAsync("search-buttons"));
                await _webView.RunAsync<object?>(_ =>
                {
                    terminal.FeedOutput(Encoding.UTF8.GetBytes("hit hit hit\r\n"));
                    return Task.FromResult<object?>(null);
                });
                await WaitForTextAsync(terminal, "hit hit hit", work => _webView.RunAsync(_ => work()));

                var raw = await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
                    """
                    (() => {
                      agterm.openSearch('search-buttons', '', '', true);
                      const input = document.getElementById('search-input');
                      input.value = 'hit';
                      input.dispatchEvent(new Event('input', {bubbles:true}));
                      document.getElementById('search-next').click();
                      const afterNext = agterm.getSearchState('search-buttons').selected;
                      input.dispatchEvent(new KeyboardEvent('keydown', {key:'Enter', shiftKey:true, bubbles:true}));
                      const afterShiftEnter = agterm.getSearchState('search-buttons').selected;
                      document.getElementById('search-previous').click();
                      const afterPreviousButton = agterm.getSearchState('search-buttons').selected;
                      document.getElementById('search-close').click();
                      return {
                        afterNext,
                        afterShiftEnter,
                        afterPreviousButton,
                        hidden: document.getElementById('search-bar').hidden,
                        terminalFocused: document.activeElement && document.activeElement.classList.contains('xterm-helper-textarea'),
                      };
                    })()
                    """));

                using var json = JsonDocument.Parse(raw);
                var root = json.RootElement;
                Assert.True(root.GetProperty("afterNext").GetInt32() > 0);
                Assert.NotEqual(
                    root.GetProperty("afterNext").GetInt32(),
                    root.GetProperty("afterShiftEnter").GetInt32());
                Assert.NotEqual(
                    root.GetProperty("afterShiftEnter").GetInt32(),
                    root.GetProperty("afterPreviousButton").GetInt32());
                Assert.True(root.GetProperty("hidden").GetBoolean());
                Assert.True(root.GetProperty("terminalFocused").GetBoolean());
            }

    [Fact]
    public async Task Two_panes_have_isolated_buffers_and_title_routing()
    {
        var (page, _) = await BootPageAsync();
        var one = await _webView.RunAsync(_ => page.OpenTerminalAsync("live-p1"));
        var two = await _webView.RunAsync(_ => page.OpenTerminalAsync("live-p2"));
        Assert.True(one.Cols > 0 && one.Rows > 0, $"p1 grid: {one.Cols}x{one.Rows}");
        Assert.True(two.Cols > 0 && two.Rows > 0, $"p2 grid: {two.Cols}x{two.Rows}");

        var oneTitles = new List<string>();
        var twoTitles = new List<string>();
        one.TitleChanged += oneTitles.Add;
        two.TitleChanged += twoTitles.Add;

        await _webView.RunAsync<object?>(_ =>
        {
            one.FeedOutput(Encoding.UTF8.GetBytes("hello-from-one\r\n\x1b]0;title-one\x07"));
            two.FeedOutput(Encoding.UTF8.GetBytes("hello-from-two\r\n\x1b]0;title-two\x07"));
            return Task.FromResult<object?>(null);
        });

        Func<Func<Task<string>>, Task<string>> onUi = work => _webView.RunAsync(_ => work());
        var oneText = await WaitForTextAsync(one, "hello-from-one", onUi);
        var twoText = await WaitForTextAsync(two, "hello-from-two", onUi);
        Assert.DoesNotContain("hello-from-two", oneText);
        Assert.DoesNotContain("hello-from-one", twoText);

        // OSC title reports must land on their own pane's event only
        var deadline = Environment.TickCount64 + 30000;
        while ((oneTitles.Count == 0 || twoTitles.Count == 0) && Environment.TickCount64 < deadline)
            await Task.Delay(50);
        Assert.Contains("title-one", oneTitles);
        Assert.DoesNotContain("title-two", oneTitles);
        Assert.Contains("title-two", twoTitles);
        Assert.DoesNotContain("title-one", twoTitles);
    }

    [Fact]
    public async Task ShowPane_refits_the_shown_pane_and_reports_its_resize()
    {
        var (page, _) = await BootPageAsync();
        var one = await _webView.RunAsync(_ => page.OpenTerminalAsync("live-show1"));
        var two = await _webView.RunAsync(_ => page.OpenTerminalAsync("live-show2"));

        var resized = new TaskCompletionSource<(int, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
        two.Resized += (c, r) => resized.TrySetResult((c, r));
        await _webView.RunAsync<object?>(async _ => { await page.ShowPaneAsync("live-show2"); return null; });

        var (cols, rows) = await resized.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(cols > 0 && rows > 0, $"shown pane grid: {cols}x{rows}");
        Assert.Equal(cols, two.Cols);
        _ = one; // the hidden pane stays registered; no resize is required of it
    }

    [Fact]
    public async Task Implemented_terminal_and_palette_chords_produce_host_actions()
    {
        var (page, view) = await BootPageAsync();
        var one = await _webView.RunAsync(_ => page.OpenTerminalAsync("live-s33"));
        var data = new List<string>();
        var chords = new List<(string PaneId, PhysicalChord Chord)>();
        one.DataReceived += data.Add;
        page.ChordPressed += (paneId, chord) => chords.Add((paneId, chord));

        var hotkeys = new HotkeyRouter(Keymap.Empty);
        var interceptChords = WindowCommandRouter.ImplementedInterceptChords(hotkeys);

        await _webView.RunAsync<object?>(async _ =>
        {
            await page.SetInterceptChordsAsync(interceptChords);
            return null;
        });
        // Ctrl+N = NewSession (implemented) → intercepted, must come back as chord, not reach the shell
        await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
            "agterm.dispatchKey(\"live-s33\", {key:'n', ctrlKey:true})"));
        // Ctrl+D = ToggleSplit (implemented in P3) → intercepted as a second host chord
        await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
            "agterm.dispatchKey(\"live-s33\", {key:'d', ctrlKey:true})"));
        // Ctrl+J = ToggleScratch (implemented in Q2) → intercepted as a third host chord
        await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
            "agterm.dispatchKey(\"live-s33\", {key:'j', ctrlKey:true})"));
        // Ctrl+F = ToggleSearch (implemented in Q4) → intercepted as a fourth host chord
        await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
            "agterm.dispatchKey(\"live-s33\", {key:'f', ctrlKey:true})"));
        // Ctrl+` = QuickTerminal (implemented in Q5) → intercepted as a fifth host chord
        await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
            "agterm.dispatchKey(\"live-s33\", {key:'`', ctrlKey:true})"));
        // C2 palettes: Ctrl+P, Ctrl+Shift+P/O/I are host chords, not terminal input.
        await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
            "agterm.dispatchKey(\"live-s33\", {key:'p', ctrlKey:true})"));
        await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
            "agterm.dispatchKey(\"live-s33\", {key:'p', ctrlKey:true, shiftKey:true})"));
        await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
            "agterm.dispatchKey(\"live-s33\", {key:'o', ctrlKey:true, shiftKey:true})"));
        await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
            "agterm.dispatchKey(\"live-s33\", {key:'i', ctrlKey:true, shiftKey:true})"));

        var deadline = Environment.TickCount64 + 30000;
        while (chords.Count < 9 && Environment.TickCount64 < deadline)
            await Task.Delay(50);

        Assert.Collection(
            chords,
            hit =>
            {
                Assert.Equal("live-s33", hit.PaneId);
                Assert.Equal(new PhysicalChord(true, false, false, "n"), hit.Chord);
            },
            hit =>
            {
                Assert.Equal("live-s33", hit.PaneId);
                Assert.Equal(new PhysicalChord(true, false, false, "d"), hit.Chord);
            },
            hit =>
            {
                Assert.Equal("live-s33", hit.PaneId);
                Assert.Equal(new PhysicalChord(true, false, false, "j"), hit.Chord);
            },
            hit =>
            {
                Assert.Equal("live-s33", hit.PaneId);
                Assert.Equal(new PhysicalChord(true, false, false, "f"), hit.Chord);
            },
            hit =>
            {
                Assert.Equal("live-s33", hit.PaneId);
                Assert.Equal(new PhysicalChord(true, false, false, "`"), hit.Chord);
            },
            hit =>
            {
                Assert.Equal("live-s33", hit.PaneId);
                Assert.Equal(new PhysicalChord(true, false, false, "p"), hit.Chord);
            },
            hit =>
            {
                Assert.Equal("live-s33", hit.PaneId);
                Assert.Equal(new PhysicalChord(true, false, true, "p"), hit.Chord);
            },
            hit =>
            {
                Assert.Equal("live-s33", hit.PaneId);
                Assert.Equal(new PhysicalChord(true, false, true, "o"), hit.Chord);
            },
            hit =>
            {
                Assert.Equal("live-s33", hit.PaneId);
                Assert.Equal(new PhysicalChord(true, false, true, "i"), hit.Chord);
            });
        Assert.DoesNotContain(data, d => d.Contains('\x04')); // Ctrl+D did not reach the terminal
        Assert.DoesNotContain(data, d => d.Contains('\x0E')); // Ctrl+N did not
        Assert.DoesNotContain(data, d => d.Contains('\x0A')); // Ctrl+J did not
        Assert.DoesNotContain(data, d => d.Contains('\x06')); // Ctrl+F did not
        Assert.DoesNotContain(data, d => d.Contains('\x00')); // Ctrl+` did not
        Assert.DoesNotContain(data, d => d.Contains('\x10')); // Ctrl+P did not
    }

    [Fact]
    public async Task ClosePane_disposes_the_terminal_so_reads_report_null()
    {
        var (page, view) = await BootPageAsync();
        var one = await _webView.RunAsync(_ => page.OpenTerminalAsync("live-close1"));
        // the pane answers before the close...
        var before = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync("agterm.text(\"live-close1\", true, null)"));
        Assert.NotEqual("null", before);
        await _webView.RunAsync<object?>(async _ => { await page.ClosePaneAsync("live-close1"); return null; });
        // ...and reads report null after it (the pane and its terminal are gone)
        var raw = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync("agterm.text(\"live-close1\", true, null)"));
        Assert.Equal("null", raw);
        _ = one;
    }

    [Fact]
    public async Task ApplyLayout_generation_guard_rejects_stale_and_preserves_accepted_pane_visibility()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("apl-p1"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("apl-p2"));

        var gen2Accepted = await _webView.RunAsync(_ =>
            page.ApplyLayoutAsync(new TerminalLayout.Single(2L, "apl-p2", "shell")));
        Assert.True(gen2Accepted);

        var gen1Rejected = await _webView.RunAsync(_ =>
            page.ApplyLayoutAsync(new TerminalLayout.Single(1L, "apl-p1", "shell")));
        Assert.False(gen1Rejected);

        var p2Display = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                """document.querySelector('[data-pane-id="apl-p2"]').style.display"""));
        var p1Display = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                """document.querySelector('[data-pane-id="apl-p1"]').style.display"""));

        Assert.Equal("\"block\"", p2Display);
        Assert.Equal("\"none\"", p1Display);
    }

    [Fact]
    public async Task Twenty_rapid_layout_submissions_end_on_generation_twenty()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("rapid-p1"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("rapid-p2"));

        var accepted = new List<TerminalLayout>();

        async Task<bool> Sink(TerminalLayout layout) =>
            await _webView.RunAsync(_ => page.ApplyLayoutAsync(layout));

        using var coordinator = new TerminalLayoutCoordinator(Sink);
        coordinator.LayoutAccepted += (_, layout) => accepted.Add(layout);

        for (var i = 1; i <= 20; i++)
        {
            var paneId = i % 2 == 0 ? "rapid-p2" : "rapid-p1";
            coordinator.Submit(new TerminalLayout.Single(i, paneId, $"title-{i}"));
        }

        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotEmpty(accepted);
        var final = accepted[^1];
        Assert.Equal(20L, final.Generation);
        Assert.Equal("rapid-p2", ((TerminalLayout.Single)final).PaneId);

        var p2Display = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                """document.querySelector('[data-pane-id="rapid-p2"]').style.display"""));
        var p1Display = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                """document.querySelector('[data-pane-id="rapid-p1"]').style.display"""));

        Assert.Equal("\"block\"", p2Display);
        Assert.Equal("\"none\"", p1Display);
    }

    [Fact]
    public async Task ApplyLayout_Empty_hides_all_panes_and_stale_Single_after_Empty_is_rejected()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("eht-p1"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("eht-p2"));

        var gen1Accepted = await _webView.RunAsync(_ =>
            page.ApplyLayoutAsync(new TerminalLayout.Single(1L, "eht-p2", "shell")));
        Assert.True(gen1Accepted);

        var gen2Accepted = await _webView.RunAsync(_ =>
            page.ApplyLayoutAsync(new TerminalLayout.Empty(2L)));
        Assert.True(gen2Accepted);

        var p1AfterEmpty = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                """document.querySelector('[data-pane-id="eht-p1"]').style.display"""));
        var p2AfterEmpty = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                """document.querySelector('[data-pane-id="eht-p2"]').style.display"""));

        Assert.Equal("\"none\"", p1AfterEmpty);
        Assert.Equal("\"none\"", p2AfterEmpty);

        var staleRejected = await _webView.RunAsync(_ =>
            page.ApplyLayoutAsync(new TerminalLayout.Single(1L, "eht-p2", "shell")));
        Assert.False(staleRejected);

        var p1AfterStale = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                """document.querySelector('[data-pane-id="eht-p1"]').style.display"""));
        var p2AfterStale = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                """document.querySelector('[data-pane-id="eht-p2"]').style.display"""));

        Assert.Equal("\"none\"", p1AfterStale);
        Assert.Equal("\"none\"", p2AfterStale);
    }

    [Fact]
    public async Task Renderer_settings_applied_to_existing_pane_reflect_all_fields()
    {
        var (page, view) = await BootPageAsync();
        _ = await _webView.RunAsync(_ => page.OpenTerminalAsync("settings-existing"));

        var snapshot = new RendererSettingsSnapshot(
            FontFamily: "Fira Code",
            FontSize: 18,
            ForegroundColor: "#112233",
            BackgroundColor: "#0C0C0C",
            CursorColor: "#445566",
            SelectionBackgroundColor: "#77889980",
            BackgroundOpacity: 0.5,
            BackgroundBlur: 20,
            CursorBlink: false,
            CursorStyle: "underline",
            ScrollSensitivity: 4.5,
            Scrollback: 4321,
            RightClickPaste: false,
            ScreenReaderMode: true);

        await _webView.RunAsync<object?>(async _ =>
        {
            await page.ApplyRendererSettingsAsync(snapshot);
            return null;
        });

        var settings = await ReadRendererSettingsAsync(view, "settings-existing");
        Assert.NotNull(settings);
        Assert.Equal("Fira Code", settings.FontFamily);
        Assert.Equal(18.0, settings.FontSize);
        Assert.Equal("#112233", settings.ForegroundColor, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("rgba(12, 12, 12, 0.5)", settings.BackgroundColor);
        Assert.Equal("#445566", settings.CursorColor, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("#77889980", settings.SelectionBackgroundColor, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0.5, settings.BackgroundOpacity);
        Assert.Equal(20, settings.BackgroundBlur);
        Assert.False(settings.CursorBlink);
        Assert.Equal("underline", settings.CursorStyle);
        Assert.Equal(4.5, settings.ScrollSensitivity);
        Assert.Equal(4321, settings.Scrollback);
        Assert.False(settings.RightClickPaste);
        Assert.True(settings.ScreenReaderMode);

        var bodyBg = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync("document.body.style.background"));
        Assert.Equal("\"transparent\"", bodyBg);
    }

    [Fact]
    public async Task Renderer_settings_applied_before_open_inherited_by_future_pane()
    {
        var (page, view) = await BootPageAsync();

        var snapshot = new RendererSettingsSnapshot(
            FontFamily: "Fira Code",
            FontSize: 16,
            ForegroundColor: "#CCCCCC",
            BackgroundColor: "#0C0C0C",
            CursorColor: "#FFFFFF",
            SelectionBackgroundColor: "#FFFFFF40",
            BackgroundOpacity: 0.85,
            BackgroundBlur: 0,
            CursorBlink: true,
            CursorStyle: "block",
            ScrollSensitivity: 4.5,
            Scrollback: 5000,
            RightClickPaste: true,
            ScreenReaderMode: false);

        await _webView.RunAsync<object?>(async _ =>
        {
            await page.ApplyRendererSettingsAsync(snapshot);
            return null;
        });

        _ = await _webView.RunAsync(_ => page.OpenTerminalAsync("settings-future"));

        var settings = await ReadRendererSettingsAsync(view, "settings-future");
        Assert.NotNull(settings);
        Assert.Equal("Fira Code", settings.FontFamily);
        Assert.Equal(16.0, settings.FontSize);
        Assert.Equal(4.5, settings.ScrollSensitivity);
        Assert.Equal(5000, settings.Scrollback);
    }

    [Fact]
    public async Task Renderer_settings_fontSize_override_on_open_takes_precedence()
    {
        var (page, view) = await BootPageAsync();

        var snapshot = new RendererSettingsSnapshot(
            FontFamily: "Fira Code",
            FontSize: 24,
            ForegroundColor: "#CCCCCC",
            BackgroundColor: "#0C0C0C",
            CursorColor: "#FFFFFF",
            SelectionBackgroundColor: "#FFFFFF40",
            BackgroundOpacity: 1.0,
            BackgroundBlur: 0,
            CursorBlink: true,
            CursorStyle: "block",
            ScrollSensitivity: 3.0,
            Scrollback: 10000,
            RightClickPaste: true,
            ScreenReaderMode: false);

        await _webView.RunAsync<object?>(async _ =>
        {
            await page.ApplyRendererSettingsAsync(snapshot);
            return null;
        });

        _ = await _webView.RunAsync(_ => page.OpenTerminalAsync("settings-override", fontSize: 19));

        var settings = await ReadRendererSettingsAsync(view, "settings-override");
        Assert.NotNull(settings);
        Assert.Equal(19.0, settings.FontSize);
        Assert.Equal("Fira Code", settings.FontFamily);
    }

    private sealed record BridgeRendererSettingsDto(
        string? FontFamily,
        double FontSize,
        string? ForegroundColor,
        string? BackgroundColor,
        string? CursorColor,
        string? SelectionBackgroundColor,
        double BackgroundOpacity,
        int BackgroundBlur,
        bool CursorBlink,
        string? CursorStyle,
        double ScrollSensitivity,
        int Scrollback,
        bool RightClickPaste,
        bool ScreenReaderMode);

    private static readonly JsonSerializerOptions _caseInsensitive =
        new() { PropertyNameCaseInsensitive = true };

    private async Task<BridgeRendererSettingsDto?> ReadRendererSettingsAsync(
        Microsoft.Web.WebView2.WinForms.WebView2 view, string paneId)
    {
        var raw = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                $"agterm.getRendererSettings({XtermPage.PaneLiteral(paneId)})"));
        return raw is null or "null"
            ? null
            : JsonSerializer.Deserialize<BridgeRendererSettingsDto>(raw, _caseInsensitive);
    }

    // ── PasteRequested: right-click-paste bridge behavior ────────────────────

    [Fact]
    public async Task Right_click_paste_enabled_fires_PasteRequested_disabled_does_not()
    {
        var (page, view) = await BootPageAsync();
        _ = await _webView.RunAsync(_ => page.OpenTerminalAsync("settings-paste"));

        var pasted = new List<string>();
        page.PasteRequested += pasted.Add;

        var snapshotOn = RendererSettingsSnapshot.From(
            new AgTerm.Core.Model.AppSettings { RightClickPaste = true });
        await _webView.RunAsync<object?>(async _ =>
        {
            await page.ApplyRendererSettingsAsync(snapshotOn);
            return null;
        });

        await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                """
                (function() {
                    var el = document.querySelector('[data-pane-id="settings-paste"]');
                    el.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
                })()
                """));

        var deadline = Environment.TickCount64 + 5000;
        while (pasted.Count == 0 && Environment.TickCount64 < deadline)
            await Task.Delay(50);

        Assert.Equal("settings-paste", Assert.Single(pasted));

        var snapshotOff = RendererSettingsSnapshot.From(
            new AgTerm.Core.Model.AppSettings { RightClickPaste = false });
        await _webView.RunAsync<object?>(async _ =>
        {
            await page.ApplyRendererSettingsAsync(snapshotOff);
            return null;
        });

        await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                """
                (function() {
                    var el = document.querySelector('[data-pane-id="settings-paste"]');
                    el.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
                })()
                """));

        // ExecuteScriptAsync completes only after the JS microtask queue drains, so any
        // synchronous postMessage call inside the handler has already been dispatched; a
        // brief extra window guards against async paths.
        await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync("undefined"));
        await Task.Delay(200);

        Assert.Single(pasted);
    }

    [Fact]
    public async Task Renderer_capabilities_report_optional_WebGL_and_keep_builtin_fallback_working()
    {
        var (page, view) = await BootPageAsync();
        var terminal = await _webView.RunAsync(_ => page.OpenTerminalAsync("settings-renderer"));

        var raw = await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                $"agterm.getRendererCapabilities({XtermPage.PaneLiteral("settings-renderer")})"));
        var capabilities = raw is null or "null" or "undefined"
            ? null
            : JsonSerializer.Deserialize<BridgeRendererCapabilitiesDto>(raw, _caseInsensitive);

        Assert.NotNull(capabilities);
        Assert.True(
            capabilities.RendererBackend is "webgl" or "builtIn",
            $"unexpected rendererBackend: {capabilities.RendererBackend}");
        if (!capabilities.WebglAddonAvailable)
            Assert.Equal("builtIn", capabilities.RendererBackend);
        // pin: addon-webgl.js is absent from vendor assets; update when the manifest gains the addon
        Assert.False(capabilities.WebglAddonAvailable,
            "addon-webgl.js is not vendored; update this assertion when the manifest changes");

        await _webView.RunAsync<object?>(_ =>
        {
            terminal.FeedOutput(Encoding.UTF8.GetBytes("λ-render-ok\r\n"));
            return Task.FromResult<object?>(null);
        });
        Func<Func<Task<string>>, Task<string>> onUi = work => _webView.RunAsync(_ => work());
        await WaitForTextAsync(terminal, "λ-render-ok", onUi);
    }

    [Fact]
    public async Task Renderer_settings_reload_preserves_unicode_buffer_and_selection()
    {
        var (page, _) = await BootPageAsync();
        var terminal = await _webView.RunAsync(_ => page.OpenTerminalAsync("reload-uc"));

        // ASCII + CJK + emoji + combining sequence (e + combining acute accent)
        const string marker = "S4 \u6f22 \U0001F600 e\u0301";
        await _webView.RunAsync<object?>(_ =>
        {
            terminal.FeedOutput(Encoding.UTF8.GetBytes(marker + "\r\n"));
            return Task.FromResult<object?>(null);
        });

        Func<Func<Task<string>>, Task<string>> onUi = work => _webView.RunAsync(_ => work());
        await WaitForTextAsync(terminal, "S4", onUi);

        // Apply changed renderer settings; buffer must survive the reload
        await _webView.RunAsync<object?>(async _ =>
        {
            await page.ApplyRendererSettingsAsync(MakeSnapshot(fontSize: 16));
            return null;
        });

        var text = await _webView.RunAsync(_ => terminal.ReadTextAsync(all: true, lines: null));
        Assert.Contains("S4", text);
        Assert.Contains("\u6f22", text); // CJK survives settings reload

        // Selection must also contain the Unicode content
        await _webView.RunAsync<object?>(async _ =>
        {
            await terminal.SelectAllAsync();
            return null;
        });
        var selection = await _webView.RunAsync(_ => terminal.GetSelectionAsync());
        Assert.Contains("S4", selection);
        Assert.Contains("\u6f22", selection);
    }

    [Fact]
    public async Task Screen_reader_mode_creates_xterm_accessibility_tree()
    {
        var (page, view) = await BootPageAsync();
        _ = await _webView.RunAsync(_ => page.OpenTerminalAsync("screader-p1"));

        // ScreenReaderMode=true → xterm must add .xterm-accessibility to the pane DOM
        await _webView.RunAsync<object?>(async _ =>
        {
            await page.ApplyRendererSettingsAsync(MakeSnapshot(screenReaderMode: true));
            return null;
        });

        // Poll: xterm may add the element asynchronously after the option change
        var deadline = Environment.TickCount64 + 5000;
        var accessibilityPresent = "false";
        while (accessibilityPresent != "true" && Environment.TickCount64 < deadline)
        {
            accessibilityPresent = await _webView.RunAsync(async _ =>
                await view.CoreWebView2.ExecuteScriptAsync(
                    "document.querySelector('[data-pane-id=\"screader-p1\"] .xterm-accessibility') !== null"));
            if (accessibilityPresent != "true") await Task.Delay(50);
        }
        Assert.Equal("true", accessibilityPresent);

        // ScreenReaderMode=false → option reads back false (DOM removal is xterm-internal, not asserted)
        await _webView.RunAsync<object?>(async _ =>
        {
            await page.ApplyRendererSettingsAsync(MakeSnapshot(screenReaderMode: false));
            return null;
        });

        var settings = await ReadRendererSettingsAsync(view, "screader-p1");
        Assert.NotNull(settings);
        Assert.False(settings.ScreenReaderMode);
    }

    [Fact]
    public async Task Composition_events_reach_xterm_data_path()
    {
        var (page, view) = await BootPageAsync();
        var terminal = await _webView.RunAsync(_ => page.OpenTerminalAsync("compo-p1"));
        var data = new List<string>();
        terminal.DataReceived += data.Add;

        // Focus the xterm helper textarea and dispatch a realistic IME composition sequence for CJK text.
        // xterm.js CompositionHelper reads textarea.value in a setTimeout(0) after compositionend;
        // set the value between compositionstart and compositionupdate so the position tracking is correct.
        await _webView.RunAsync(async _ =>
            await view.CoreWebView2.ExecuteScriptAsync(
                """
                (function() {
                    var ta = document.querySelector('[data-pane-id="compo-p1"] .xterm-helper-textarea');
                    if (!ta) return 'no-textarea';
                    ta.focus();
                    ta.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true, data: '' }));
                    ta.value = '\u6f22\u5b57';
                    ta.dispatchEvent(new CompositionEvent('compositionupdate', { bubbles: true, data: '\u6f22\u5b57' }));
                    ta.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true, data: '\u6f22\u5b57' }));
                    return 'dispatched';
                })()
                """));

        var deadline = Environment.TickCount64 + 5000;
        while (data.Count == 0 && Environment.TickCount64 < deadline)
            await Task.Delay(50);

        Assert.True(data.Count > 0, "DataReceived was not raised; xterm did not emit composed text via synthetic composition events");
        Assert.Contains(data, d => d.Contains("\u6f22\u5b57") || d.Contains("\u6f22"));
    }

    /// <summary>
    /// Shift+Enter must reach the pty as the kitty CSI u sequence once the app asks for the
    /// protocol, and as plain CR before and after. Claude Code's newline-without-submit binding
    /// depends on it: with the protocol off, Enter and Shift+Enter are both CR and it cannot tell
    /// them apart. Upstream agterm gets this from Ghostty; on Windows it comes from xterm.js.
    /// </summary>
    [Fact]
    public async Task Shift_enter_reports_kitty_csi_u_while_the_keyboard_protocol_is_pushed()
    {
        var (page, view) = await BootPageAsync();
        var terminal = await _webView.RunAsync(_ => page.OpenTerminalAsync("kitty-kbd"));
        var data = new List<string>();
        terminal.DataReceived += data.Add;

        async Task DispatchAsync(bool shift) => await _webView.RunAsync(
            async _ => await view.CoreWebView2.ExecuteScriptAsync(
                $"agterm.dispatchKey(\"kitty-kbd\", {{key:'Enter', shiftKey:{(shift ? "true" : "false")}}})"));

        async Task FeedAsync(string vt) => await _webView.RunAsync(_ =>
        {
            terminal.FeedOutput(Encoding.UTF8.GetBytes(vt));
            return Task.FromResult(0);
        });

        async Task<List<string>> CollectAsync(int count)
        {
            var deadline = Environment.TickCount64 + 10000;
            while (data.Count < count && Environment.TickCount64 < deadline)
                await Task.Delay(25);
            return [.. data];
        }

        // protocol off: both are CR — this is the state Claude Code cannot work with
        await DispatchAsync(shift: false);
        await DispatchAsync(shift: true);
        Assert.Equal(["\r", "\r"], await CollectAsync(2));

        // CSI > 1 u — progressive enhancement, what an app pushes on entry
        await FeedAsync("\u001b[>1u");
        await DispatchAsync(shift: false);
        await DispatchAsync(shift: true);
        Assert.Equal(["\r", "\r", "\r", "\u001b[13;2u"], await CollectAsync(4));

        // CSI < u — popped on exit, back to CR for everything else
        await FeedAsync("\u001b[<u");
        await DispatchAsync(shift: true);
        Assert.Equal(["\r", "\r", "\r", "\u001b[13;2u", "\r"], await CollectAsync(5));
    }

    /// <summary>
    /// A real Shift+Enter carries event.code "Enter"; the engine's win32-input-mode encoder keys
    /// off code and is checked BEFORE the kitty path, so with both extensions on it decides the
    /// encoding whenever mode 9001 is active. Pins what the pty actually receives for a keydown
    /// that looks like the hardware one.
    /// </summary>
    [Fact]
    public async Task Shift_enter_with_a_real_event_code_reports_kitty_csi_u()
    {
        var (page, view) = await BootPageAsync();
        var terminal = await _webView.RunAsync(_ => page.OpenTerminalAsync("kitty-code"));
        var data = new List<string>();
        terminal.DataReceived += data.Add;

        // an app asks for the protocol, the way Claude Code does on entry
        await _webView.RunAsync(_ =>
        {
            terminal.FeedOutput(Encoding.UTF8.GetBytes("[>1u"));
            return Task.FromResult(0);
        });
        await Task.Delay(200);

        // same shape as a hardware Shift+Enter: key, code and keyCode all present
        await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
            """
            (() => {
              const ev = new KeyboardEvent("keydown", {
                key: "Enter", code: "Enter", shiftKey: true, cancelable: true });
              Object.defineProperty(ev, "keyCode", { value: 13, configurable: true });
              Object.defineProperty(ev, "which", { value: 13, configurable: true });
              agterm.textareaFor("kitty-code").dispatchEvent(ev);
              return true;
            })()
            """));

        var mode9001 = await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
            "agterm.win32InputModeActive(\"kitty-code\")"));

        var deadline = Environment.TickCount64 + 5000;
        while (data.Count < 1 && Environment.TickCount64 < deadline)
            await Task.Delay(25);

        Assert.Equal(["\u001b[13;2u"], data);
        Assert.Equal("false", mode9001);
    }

    private static RendererSettingsSnapshot MakeSnapshot(
        double fontSize = 14,
        bool screenReaderMode = false) =>
        new(
            FontFamily: "Cascadia Mono, Consolas, monospace",
            FontSize: fontSize,
            ForegroundColor: "#CCCCCC",
            BackgroundColor: "#0C0C0C",
            CursorColor: "#FFFFFF",
            SelectionBackgroundColor: "#FFFFFF40",
            BackgroundOpacity: 1.0,
            BackgroundBlur: 0,
            CursorBlink: true,
            CursorStyle: "block",
            ScrollSensitivity: 3.0,
            Scrollback: 10000,
            RightClickPaste: true,
            ScreenReaderMode: screenReaderMode);

    private sealed record BridgeRendererCapabilitiesDto(
        bool Webgl2Available,
        bool WebglAddonAvailable,
        string? RendererBackend);
}
