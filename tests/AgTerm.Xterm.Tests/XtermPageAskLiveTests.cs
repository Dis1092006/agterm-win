using System.Text.Json;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

[Collection("webview2")]
public class XtermPageAskLiveTests
{
    private readonly WebView2Fixture _webView;

    public XtermPageAskLiveTests(WebView2Fixture webView)
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

    private const string Payload = """
        {"askId":"ASK","paneId":"right","coveredPaneIds":["right"],"title":"Delete branch?","message":"It has unmerged commits.",
         "buttons":[{"runs":[{"text":"K","underline":true},{"text":"eep","underline":false}],"destructive":false},
                    {"runs":[{"text":"! ","underline":false},{"text":"Delete","underline":false}],"destructive":true}],
         "highlighted":0,"align":"right","width":null}
        """;

    [Fact]
    public async Task Detached_quick_keeps_the_question_drawn_without_moving_keyboard_focus()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Single(1, "right", "Shell")));
        var state = await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync($$"""
            (() => {
                agterm.setExternalInputOwner(true);
                agterm.applyAsk({{Payload}});
                const layer = document.querySelector('.ask-layer');
                return { drawn: layer !== null, focused: layer?.contains(document.activeElement) };
            })()
            """));
        using var json = JsonDocument.Parse(state);
        Assert.True(json.RootElement.GetProperty("drawn").GetBoolean());
        Assert.False(json.RootElement.GetProperty("focused").GetBoolean());
    }

    [Fact]
    public async Task A_pane_question_is_drawn_inside_its_pane_and_takes_its_focus()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("left"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Split(1, "left", "right", 0.5, "right")));

        var state = await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync($$"""
            (() => {
                agterm.applyAsk({{Payload}});
                const layer = document.querySelector('.ask-layer');
                const owner = layer ? layer.closest('.pane') : null;
                const buttons = layer ? Array.from(layer.querySelectorAll('.ask-button')) : [];
                return {
                    owner: owner ? owner.dataset.paneId : null,
                    focused: layer !== null && layer.contains(document.activeElement),
                    title: layer ? layer.querySelector('.ask-title').textContent : null,
                    labels: buttons.map(b => b.textContent),
                    underlined: buttons.map(b => b.querySelector('u') ? b.querySelector('u').textContent : null),
                    highlighted: buttons.map(b => b.classList.contains('ask-highlighted')),
                    widths: buttons.map(b => Math.round(b.getBoundingClientRect().width)),
                };
            })()
            """));

        using var json = System.Text.Json.JsonDocument.Parse(state);
        var root = json.RootElement;
        Assert.Equal("right", root.GetProperty("owner").GetString());
        Assert.True(root.GetProperty("focused").GetBoolean());
        Assert.Equal("Delete branch?", root.GetProperty("title").GetString());
        Assert.Equal(new[] { "Keep", "! Delete" }, root.GetProperty("labels").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("K", root.GetProperty("underlined")[0].GetString());
        Assert.Equal(new[] { true, false }, root.GetProperty("highlighted").EnumerateArray().Select(e => e.GetBoolean()));
        var widths = root.GetProperty("widths").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        Assert.Equal(widths[0], widths[1]);
    }

    [Fact]
    public async Task Keys_on_the_question_become_intents_and_never_reach_the_terminal()
    {
        var (page, view) = await BootPageAsync();
        var terminal = await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        var data = new System.Collections.Concurrent.ConcurrentQueue<string>();
        terminal.DataReceived += data.Enqueue;
        var intents = new EventRecorder<string>();
        page.AskIntentReceived += (askId, intent) => intents.Add(intent.GetType().Name + (intent is AskIntent.Hotkey h ? ":" + h.Letter : ""));
        await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync($$"""
            (() => {
                agterm.applyAsk({{Payload}});
                const layer = document.querySelector('.ask-layer');
                const key = (key, code, extra) => layer.dispatchEvent(new KeyboardEvent('keydown', Object.assign({ key, code, bubbles: true, cancelable: true }, extra || {})));
                key('Tab', 'Tab');
                key('Tab', 'Tab', { shiftKey: true });
                key('ArrowRight', 'ArrowRight');
                key('ArrowDown', 'ArrowDown');
                key('ArrowLeft', 'ArrowLeft');
                key('ArrowUp', 'ArrowUp');
                key('Enter', 'Enter');
                key('Escape', 'Escape');
                key('н', 'KeyY');
                key('y', 'KeyY', { ctrlKey: true });
            })()
            """));
        await Task.Delay(200);

        Assert.Equal(new[] { "Forward", "Backward", "Forward", "Forward", "Backward", "Backward", "Activate", "Cancel", "Hotkey:y" }, intents);
        Assert.Empty(data);
    }

    [Fact]
    public async Task Clearing_the_question_returns_focus_to_its_pane()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Single(1, "right", "Shell")));
        var focused = await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync($$"""
            (() => {
                agterm.applyAsk({{Payload}});
                agterm.applyAsk(null);
                return document.querySelector('.ask-layer') === null
                    && document.querySelector('[data-pane-id="right"]').contains(document.activeElement);
            })()
            """));
        Assert.Equal("true", focused);
    }

    [Fact]
    public async Task A_session_wide_question_covers_the_whole_page()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));
        var state = await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync("""
            (() => {
                agterm.applyAsk({"askId":"S","paneId":null,"coveredPaneIds":["main"],"title":"Continue?","message":null,
                    "buttons":[{"runs":[{"text":"OK","underline":false}],"destructive":false}],"highlighted":0,"align":"center","width":null});
                const layer = document.querySelector('.ask-layer');
                const r = layer.getBoundingClientRect();
                return r.width === window.innerWidth && r.height === window.innerHeight && layer.parentElement === document.body;
            })()
            """));
        Assert.Equal("true", state);
    }

    private const string SessionPayloadTemplate = """
        {"askId":"S","paneId":null,"coveredPaneIds":COVERED,"title":"Continue?","message":null,
         "buttons":[{"runs":[{"text":"OK","underline":false}],"destructive":false}],"highlighted":0,"align":"center","width":null}
        """;

    private static string SessionPayload(string covered) => SessionPayloadTemplate.Replace("COVERED", covered);

    private async Task<string> EvalAsync(Microsoft.Web.WebView2.WinForms.WebView2 view, string script) =>
        await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(script));

    [Fact]
    public async Task A_republished_question_takes_the_new_covered_panes()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("left"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Split(1, "left", "right", 0.5, "left")));
        var result = await EvalAsync(view, $$"""
            (() => {
                agterm.applyAsk({{SessionPayload("[\"left\"]")}});
                agterm.applyAsk({{SessionPayload("[\"left\",\"right\"]")}});
                agterm.focus('right');
                return document.activeElement === document.querySelector('.ask-layer');
            })()
            """);
        Assert.Equal("true", result);
    }

    [Fact]
    public async Task Focusing_a_covered_pane_programmatically_reports_no_pane_focus_and_a_click_reports_one()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Single(1, "right", "Shell")));
        await _webView.RunAsync<object?>(_ => { view.Focus(); return Task.FromResult<object?>(null); });
        await Task.Delay(500); // focusing the page re-focuses the pane's terminal and reports it
        var paneFocus = new System.Collections.Concurrent.ConcurrentQueue<string>();
        page.PaneFocusChanged += paneFocus.Enqueue;

        await EvalAsync(view, $$"""
            (() => { agterm.applyAsk({{Payload}}); focusPane('right', false); return 1; })()
            """);
        await Task.Delay(300);
        Assert.Empty(paneFocus);

        await EvalAsync(view, """
            (() => { document.querySelector('.ask-layer').dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true })); return 1; })()
            """);
        await Task.Delay(300);
        Assert.Equal(new[] { "right" }, paneFocus);
    }

    [Fact]
    public async Task The_question_keeps_focus_against_a_covered_pane_and_reports_focus_changes()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Single(1, "main", "Shell")));
        await _webView.RunAsync<object?>(_ => { view.Focus(); return Task.FromResult<object?>(null); });
        await Task.Delay(500); // focusing the page re-focuses the pane's terminal and reports it
        var focus = new System.Collections.Concurrent.ConcurrentQueue<string?>();
        page.AskFocusChanged += focus.Enqueue;

        var kept = await EvalAsync(view, $$"""
            (() => {
                agterm.applyAsk({{SessionPayload("[\"main\"]")}});
                const pane = document.querySelector('[data-pane-id="main"]');
                pane.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true }));
                agterm.focus('main');
                return document.activeElement === document.querySelector('.ask-layer');
            })()
            """);
        Assert.Equal("true", kept);
        await Task.Delay(300);
        Assert.Equal(new string?[] { "S" }, focus.ToArray());

        await EvalAsync(view, "(() => { agterm.applyAsk(null); return 1; })()");
        await Task.Delay(300);
        Assert.Equal(new string?[] { "S", null }, focus.ToArray());

        await EvalAsync(view, $$"""
            (() => { agterm.applyAsk({{SessionPayload("[\"main\"]")}}); document.querySelector('.ask-layer').blur(); return 1; })()
            """);
        await Task.Delay(300);
        Assert.Equal(new string?[] { "S", null, "S", null }, focus.ToArray());
    }

    [Fact]
    public async Task The_panel_width_follows_its_pane_without_a_window_resize()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Single(1, "right", "Shell")));
        await EvalAsync(view, $$"""
            (() => { agterm.applyAsk({{Payload.Replace("\"width\":null", "\"width\":50")}}); return 1; })()
            """);
        string MaxWidth() => "(() => document.querySelector('.ask-panel').style.maxWidth)()";
        var before = await EvalAsync(view, MaxWidth());

        await EvalAsync(view, "(() => { document.querySelector('[data-pane-id=\"right\"]').style.width = '300px'; return 1; })()");
        await Task.Delay(500);
        var after = await EvalAsync(view, MaxWidth());

        Assert.Equal("\"150px\"", after);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task Answering_a_session_wide_question_focuses_the_pane_the_split_focuses()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("left"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Split(1, "left", "right", 0.5, "right")));
        await _webView.RunAsync<object?>(async _ =>
        {
            await page.SetInterceptChordsAsync(new[] { new AgTerm.Core.Keymap.PhysicalChord(false, true, false, "n") });
            return null;
        });
        var chords = new System.Collections.Concurrent.ConcurrentQueue<string>();
        page.ChordPressed += (paneId, _) => chords.Enqueue(paneId);

        var focused = await EvalAsync(view, $$"""
            (() => {
                agterm.applyAsk({{SessionPayload("[\"left\",\"right\"]")}});
                document.querySelector('.ask-layer').dispatchEvent(
                    new KeyboardEvent('keydown', { key: 'т', code: 'KeyN', altKey: true, bubbles: true, cancelable: true }));
                agterm.applyAsk(null);
                return document.querySelector('[data-pane-id="right"]').contains(document.activeElement);
            })()
            """);
        await Task.Delay(200);

        Assert.Equal("true", focused);
        Assert.Equal(new[] { "right" }, chords);
    }

    [Fact]
    public async Task Answering_a_session_wide_question_focuses_the_layer_over_the_main_pane()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("scratch"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Layered(
            1, new TerminalLayout.Single(1, "main", "Shell"), new TerminalLayer("scratch"))));

        var focused = await EvalAsync(view, $$"""
            (() => {
                agterm.applyAsk({{SessionPayload("[\"main\",\"scratch\"]")}});
                agterm.applyAsk(null);
                return document.querySelector('[data-pane-id="scratch"]').contains(document.activeElement);
            })()
            """);

        Assert.Equal("true", focused);
    }

    [Fact]
    public async Task A_pane_question_draws_and_takes_clicks_above_a_full_size_program_overlay()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("overlay"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Layered(
            1, new TerminalLayout.Single(1, "right", "Shell"), new TerminalLayer("overlay"))));

        var state = await EvalAsync(view, $$"""
            (() => {
                agterm.applyAsk({{Payload}});
                const layer = document.querySelector('.ask-layer');
                const r = layer.querySelector('.ask-panel').getBoundingClientRect();
                const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                let opacity = 1;
                for (let node = layer; node; node = node.parentElement) opacity *= parseFloat(getComputedStyle(node).opacity);
                return {
                    visibility: getComputedStyle(layer).visibility,
                    opacity,
                    width: r.width,
                    height: r.height,
                    hit: hit !== null && layer.contains(hit),
                };
            })()
            """);

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        Assert.Equal("visible", root.GetProperty("visibility").GetString());
        Assert.Equal(1.0, root.GetProperty("opacity").GetDouble());
        Assert.True(root.GetProperty("width").GetDouble() > 0);
        Assert.True(root.GetProperty("height").GetDouble() > 0);
        Assert.True(root.GetProperty("hit").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(50)]
    public async Task Answering_a_question_on_a_base_pane_under_an_overlay_focuses_the_overlay(int? sizePercent)
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("overlay"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Layered(
            1, new TerminalLayout.Single(1, "right", "Shell"), new TerminalLayer("overlay", sizePercent))));

        var focused = await EvalAsync(view, $$"""
            (() => {
                agterm.applyAsk({{Payload}});
                const layer = document.querySelector('.ask-layer');
                layer.focus();
                const layerFocused = document.activeElement === layer;
                agterm.applyAsk(null);
                return layerFocused && document.querySelector('[data-pane-id="overlay"]').contains(document.activeElement);
            })()
            """);

        Assert.Equal("true", focused);
    }

    private static string PanePayload(string askId, string paneId, string covered) =>
        Payload.Replace("\"askId\":\"ASK\"", $"\"askId\":\"{askId}\"")
            .Replace("\"paneId\":\"right\",\"coveredPaneIds\":[\"right\"]", $"\"paneId\":\"{paneId}\",\"coveredPaneIds\":{covered}");

    private async Task<(XtermPage Page, Microsoft.Web.WebView2.WinForms.WebView2 View)> BootOverlayAsync(bool split, int? sizePercent)
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("left"));
        if (split) await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("overlay"));
        TerminalLayout baseLayout = split
            ? new TerminalLayout.Split(1, "left", "right", 0.5, "left")
            : new TerminalLayout.Single(1, "left", "Shell");
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Layered(
            1, baseLayout, new TerminalLayer("overlay", sizePercent))));
        await _webView.RunAsync<object?>(_ => { view.Focus(); return Task.FromResult<object?>(null); });
        await WaitForScriptAsync(view, $"document.hasFocus() && {OverlayFocused}");
        return (page, view);
    }

    private async Task WaitForScriptAsync(Microsoft.Web.WebView2.WinForms.WebView2 view, string condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (await EvalAsync(view, $"(() => {condition})()") != "true")
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Not met within the timeout: {condition}");
            await Task.Delay(25);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Condition not met within the timeout.");
            await Task.Delay(25);
        }
    }

    private const string OverlayFocused =
        "document.querySelector('[data-pane-id=\"overlay\"]').contains(document.activeElement)";

    // a mouse click on a terminal: a pointerdown, then xterm focuses its helper textarea
    private const string ClickOverlay =
        "document.querySelector('[data-pane-id=\"overlay\"] .xterm').dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true }));"
        + "document.querySelector('[data-pane-id=\"overlay\"] .xterm-helper-textarea').focus();";

    // a WebView2 focus restore or a script focus: focusin on the terminal without a pointer
    private const string FocusOverlayWithoutPointer = "panes.get('overlay').term.focus();";

    private const string QuestionFocused = "document.activeElement === document.querySelector('.ask-layer')";

    [Theory]
    [InlineData(null)]
    [InlineData(50)]
    public async Task A_pane_question_on_the_focused_side_takes_the_overlay_focus(int? sizePercent)
    {
        var (_, view) = await BootOverlayAsync(split: true, sizePercent);

        var state = await EvalAsync(view, $$"""
            (() => {
                const before = {{OverlayFocused}};
                agterm.applyAsk({{PanePayload("ASK", "left", "[\"left\",\"overlay\"]")}});
                const layer = document.querySelector('.ask-layer');
                const taken = document.activeElement === layer;
                agterm.focus('overlay');
                const refocused = document.activeElement === layer;
                agterm.applyAsk(null);
                return { before, taken, refocused, answered: {{OverlayFocused}} };
            })()
            """);

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        Assert.True(root.GetProperty("before").GetBoolean(), state);
        Assert.True(root.GetProperty("taken").GetBoolean(), state);
        Assert.True(root.GetProperty("refocused").GetBoolean(), state);
        Assert.True(root.GetProperty("answered").GetBoolean(), state);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(50)]
    public async Task A_pane_question_on_the_other_side_leaves_the_overlay_focused(int? sizePercent)
    {
        var (_, view) = await BootOverlayAsync(split: true, sizePercent);

        var focused = await EvalAsync(view, $$"""
            (() => {
                agterm.applyAsk({{PanePayload("ASK", "right", "[\"right\"]")}});
                const kept = {{OverlayFocused}};
                agterm.focus('overlay');
                return kept && {{OverlayFocused}};
            })()
            """);

        Assert.Equal("true", focused);
    }

    // upstream deferMouseToAsk: the program keeps its keys because the session's focus moves away
    [Theory]
    [InlineData(null)]
    [InlineData(50)]
    public async Task A_click_on_the_overlay_over_a_split_moves_the_session_focus_to_the_other_side(int? sizePercent)
    {
        var (page, view) = await BootOverlayAsync(split: true, sizePercent);
        await EvalAsync(view, $$"""
            (() => { agterm.applyAsk({{PanePayload("ASK", "left", "[\"left\",\"overlay\"]")}}); return 1; })()
            """);
        var paneFocus = new System.Collections.Concurrent.ConcurrentQueue<string>();
        page.PaneFocusChanged += paneFocus.Enqueue;

        var beforeRepublish = await EvalAsync(view, $$"""
            (() => { {{ClickOverlay}} agterm.focus('overlay'); return {{OverlayFocused}}; })()
            """);
        await WaitUntilAsync(() => paneFocus.Contains("right") && paneFocus.Contains("overlay"));

        var state = await EvalAsync(view, $$"""
            (() => {
                agterm.applyAsk({{PanePayload("ASK", "left", "[\"left\"]")}});
                agterm.focus('overlay');
                const republished = {{OverlayFocused}};
                agterm.applyAsk({{PanePayload("ASK", "left", "[\"left\",\"overlay\"]")}});
                return { republished, focusBack: {{QuestionFocused}} };
            })()
            """);

        Assert.Equal("true", beforeRepublish);
        Assert.DoesNotContain("left", paneFocus);
        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        Assert.True(root.GetProperty("republished").GetBoolean(), state);
        Assert.True(root.GetProperty("focusBack").GetBoolean(), state);
    }

    // only a pointer releases the overlay: an Alt+Tab focus restore must not move the session's focus
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_focus_of_the_overlay_without_a_pointer_keeps_it_covered(bool split)
    {
        var (page, view) = await BootOverlayAsync(split, sizePercent: null);
        var paneFocus = new System.Collections.Concurrent.ConcurrentQueue<string>();
        page.PaneFocusChanged += paneFocus.Enqueue;

        var state = await EvalAsync(view, $$"""
            (() => {
                agterm.applyAsk({{PanePayload("ASK", "left", "[\"left\",\"overlay\"]")}});
                {{FocusOverlayWithoutPointer}}
                const moved = {{OverlayFocused}};
                agterm.focus('overlay');
                const covered = {{QuestionFocused}};
                agterm.focus('left');
                return { moved, covered };
            })()
            """);
        // the page reports in order, so the last focus above arriving means any report before it has too
        await WaitUntilAsync(() => paneFocus.Contains("left"));

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        Assert.True(root.GetProperty("moved").GetBoolean(), state);
        Assert.True(root.GetProperty("covered").GetBoolean(), state);
        Assert.DoesNotContain("right", paneFocus);
    }

    // upstream wantsFocus turns true once the overlay on the question's side is shown
    [Theory]
    [InlineData(null)]
    [InlineData(50)]
    public async Task An_overlay_opened_over_a_pending_pane_question_gives_the_question_the_keys(int? sizePercent)
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("left"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("overlay"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Single(1, "left", "Shell")));
        await EvalAsync(view, $$"""
            (() => { agterm.applyAsk({{PanePayload("ASK", "left", "[\"left\"]")}}); return 1; })()
            """);
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Layered(
            2, new TerminalLayout.Single(2, "left", "Shell"), new TerminalLayer("overlay", sizePercent))));

        var state = await EvalAsync(view, $$"""
            (() => {
                const layoutFocusedOverlay = {{OverlayFocused}};
                agterm.applyAsk({{PanePayload("ASK", "left", "[\"left\",\"overlay\"]")}});
                return { layoutFocusedOverlay, taken: {{QuestionFocused}} };
            })()
            """);

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        Assert.True(root.GetProperty("layoutFocusedOverlay").GetBoolean(), state);
        Assert.True(root.GetProperty("taken").GetBoolean(), state);
    }

    [Fact]
    public async Task A_click_on_the_overlay_over_a_single_pane_keeps_the_keys_until_the_next_question()
    {
        var (_, view) = await BootOverlayAsync(split: false, sizePercent: null);
        var covered = "[\"left\",\"overlay\"]";

        var state = await EvalAsync(view, $$"""
            (() => {
                agterm.applyAsk({{PanePayload("ASK", "left", covered)}});
                {{ClickOverlay}}
                agterm.focus('overlay');
                const afterRefocus = {{OverlayFocused}};
                agterm.applyAsk({{PanePayload("ASK", "left", covered)}});
                agterm.focus('overlay');
                const afterRedraw = {{OverlayFocused}};
                document.querySelector('.ask-layer').dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true }));
                agterm.focus('overlay');
                const questionClicked = document.activeElement === document.querySelector('.ask-layer');
                {{ClickOverlay}}
                agterm.applyAsk({{PanePayload("NEXT", "left", covered)}});
                const next = document.activeElement === document.querySelector('.ask-layer');
                return { afterRefocus, afterRedraw, questionClicked, next };
            })()
            """);

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        Assert.True(root.GetProperty("afterRefocus").GetBoolean(), state);
        Assert.True(root.GetProperty("afterRedraw").GetBoolean(), state);
        Assert.True(root.GetProperty("questionClicked").GetBoolean(), state);
        Assert.True(root.GetProperty("next").GetBoolean(), state);
    }

    // ruling: a pane question leaves the search bar above it, a session-wide question covers it
    [Fact]
    public async Task The_search_bar_stacks_above_a_pane_question_and_below_a_session_wide_one()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("left"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Split(1, "left", "right", 0.5, "left")));

        var state = await EvalAsync(view, $$"""
            (() => {
                agterm.openSearch('left', '', '', false);
                const z = (el) => parseInt(getComputedStyle(el).zIndex, 10);
                const search = z(document.getElementById('search-bar'));
                agterm.applyAsk({{Payload}});
                const pane = z(document.querySelector('.ask-layer'));
                agterm.applyAsk({{SessionPayload("[\"left\",\"right\"]")}});
                const session = z(document.querySelector('.ask-layer'));
                return { search, pane, session };
            })()
            """);

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        var search = root.GetProperty("search").GetInt32();
        Assert.True(root.GetProperty("pane").GetInt32() < search, state);
        Assert.True(search < root.GetProperty("session").GetInt32(), state);
    }

    private const string ThreeButtonPayload = """
        {"askId":"ROW","paneId":"right","coveredPaneIds":["right"],"title":"Files exist","message":null,
         "buttons":[{"runs":[{"text":"Keep both","underline":false}],"destructive":false},
                    {"runs":[{"text":"Replace all","underline":false}],"destructive":false},
                    {"runs":[{"text":"Skip these","underline":false}],"destructive":false}],
         "highlighted":0,"align":"right","width":null}
        """;

    private const string ButtonArrangement = """
        (() => {
            const tops = Array.from(document.querySelectorAll('.ask-button')).map(b => Math.round(b.getBoundingClientRect().top));
            return {
                column: document.querySelector('.ask-buttons').classList.contains('ask-column'),
                sameRow: tops.every(t => t === tops[0]),
                stacked: tops.every((t, i) => i === 0 || t > tops[i - 1]),
            };
        })()
        """;

    [Fact]
    public async Task The_buttons_sit_in_a_row_in_a_wide_pane_and_stack_when_the_row_does_not_fit()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Single(1, "right", "Shell")));
        await EvalAsync(view, $$"""(() => { agterm.applyAsk({{ThreeButtonPayload}}); return 1; })()""");

        using (var wide = JsonDocument.Parse(await EvalAsync(view, ButtonArrangement)))
        {
            Assert.False(wide.RootElement.GetProperty("column").GetBoolean());
            Assert.True(wide.RootElement.GetProperty("sameRow").GetBoolean());
        }

        await EvalAsync(view, "(() => { document.querySelector('[data-pane-id=\"right\"]').style.width = '200px'; return 1; })()");
        await Task.Delay(500);

        using var narrow = JsonDocument.Parse(await EvalAsync(view, ButtonArrangement));
        Assert.True(narrow.RootElement.GetProperty("column").GetBoolean());
        Assert.True(narrow.RootElement.GetProperty("stacked").GetBoolean());
    }

    private static string InputPayload(string askId, string mode, string value) => $$$"""
        {"askId":"{{{askId}}}","paneId":"right","coveredPaneIds":["right"],"title":"Name","message":null,
         "buttons":[{"runs":[{"text":"OK","underline":false}],"destructive":false},{"runs":[{"text":"Cancel","underline":false}],"destructive":false}],
         "highlighted":0,"align":"right","width":null,"input":{"mode":"{{{mode}}}","value":"{{{value}}}","placeholder":"type here"}}
        """;

    private async Task<(XtermPage Page, Microsoft.Web.WebView2.WinForms.WebView2 View)> BootInputAsync(string mode, string value = "draft")
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Single(1, "right", "Shell")));
        await _webView.RunAsync<object?>(_ => { view.Focus(); return Task.FromResult<object?>(null); });
        await Task.Delay(500);
        await EvalAsync(view, $$"""(() => { agterm.applyAsk({{InputPayload("IN", mode, value)}}); return 1; })()""");
        return (page, view);
    }

    private const string Field = "document.querySelector('.ask-input')";

    [Theory]
    [InlineData("line", "INPUT")]
    [InlineData("multiline", "TEXTAREA")]
    public async Task An_input_question_draws_its_field_full_width_and_focuses_it(string mode, string tag)
    {
        var (_, view) = await BootInputAsync(mode);

        using var json = JsonDocument.Parse(await EvalAsync(view, $$"""
            (() => {
                const field = {{Field}};
                const panel = document.querySelector('.ask-panel');
                const area = document.querySelector('.ask-layer').getBoundingClientRect();
                return {
                    tag: field.tagName, value: field.value, placeholder: field.placeholder,
                    focused: document.activeElement === field,
                    beforeButtons: field.compareDocumentPosition(document.querySelector('.ask-buttons')) === Node.DOCUMENT_POSITION_FOLLOWING,
                    fullWidth: Math.abs(panel.getBoundingClientRect().width - Math.min(area.width * 0.9, parseFloat(panel.style.maxWidth))) < 1,
                    rows: field.rows ?? null,
                };
            })()
            """));
        var root = json.RootElement;
        Assert.Equal(tag, root.GetProperty("tag").GetString());
        Assert.Equal("draft", root.GetProperty("value").GetString());
        Assert.Equal("type here", root.GetProperty("placeholder").GetString());
        Assert.True(root.GetProperty("focused").GetBoolean());
        Assert.True(root.GetProperty("beforeButtons").GetBoolean());
        Assert.True(root.GetProperty("fullWidth").GetBoolean());
        if (mode == "multiline") Assert.True(root.GetProperty("rows").GetInt32() >= 5);
    }

    [Fact]
    public async Task Typing_in_the_field_posts_the_draft_and_never_reaches_the_terminal()
    {
        var (page, view) = await BootInputAsync("line", "");
        var drafts = new EventRecorder<string>();
        page.AskDraftReceived += (askId, text) => drafts.Add(askId + "/" + text);
        var intents = new EventRecorder<string>();
        page.AskIntentReceived += (_, intent) => intents.Add(intent.GetType().Name);

        await EvalAsync(view, "(() => { document.execCommand('insertText', false, 'ab'); document.execCommand('insertText', false, 'y'); return 1; })()");
        await WaitUntilAsync(() => drafts.Count >= 2);

        Assert.Equal(new[] { "IN/ab", "IN/aby" }, drafts);
        Assert.Empty(intents);
    }

    private const string KeyHelper = """
        const field = document.querySelector('.ask-input');
        const key = (key, code, extra) => field.dispatchEvent(new KeyboardEvent('keydown', Object.assign({ key, code, bubbles: true, cancelable: true }, extra || {})));
        """;

    [Fact]
    public async Task A_line_field_answers_on_enter_and_moves_the_highlight_on_tab()
    {
        var (page, view) = await BootInputAsync("line");
        var intents = new EventRecorder<string>();
        page.AskIntentReceived += (_, intent) => intents.Add(intent.GetType().Name);

        var prevented = await EvalAsync(view, $$"""
            (() => {
                {{KeyHelper}}
                return [key('Tab', 'Tab'), key('Tab', 'Tab', { shiftKey: true }), key('Enter', 'Enter'), key('Escape', 'Escape'),
                        key('ArrowLeft', 'ArrowLeft'), key('y', 'KeyY'), key('Enter', 'Enter', { isComposing: true })].map(r => !r);
            })()
            """);
        await Task.Delay(200);

        Assert.Equal("[true,true,true,true,false,false,false]", prevented);
        Assert.Equal(new[] { "Forward", "Backward", "Activate", "Cancel" }, intents);
    }

    [Fact]
    public async Task A_multiline_field_takes_enter_as_a_newline_and_answers_on_ctrl_enter()
    {
        var (page, view) = await BootInputAsync("multiline");
        var intents = new EventRecorder<string>();
        page.AskIntentReceived += (_, intent) => intents.Add(intent.GetType().Name);

        var prevented = await EvalAsync(view, $$"""
            (() => {
                {{KeyHelper}}
                return [key('Enter', 'Enter'), key('Enter', 'Enter', { shiftKey: true }), key('Enter', 'Enter', { ctrlKey: true, shiftKey: true }),
                        key('Enter', 'Enter', { ctrlKey: true })].map(r => !r);
            })()
            """);
        await Task.Delay(200);

        Assert.Equal("[false,false,false,true]", prevented);
        Assert.Equal(new[] { "Activate" }, intents);
    }

    [Fact]
    public async Task Native_edit_chords_stay_in_the_field_and_window_chords_reach_the_host()
    {
        var (page, view) = await BootInputAsync("line");
        await _webView.RunAsync(_ => page.SetInterceptChordsAsync(new[] { "c", "v", "x", "z", "y", "a", "w" }
            .Select(key => new Core.Keymap.PhysicalChord(true, false, false, key))));
        var chords = new EventRecorder<string>();
        page.ChordPressed += (paneId, chord) => chords.Add(paneId + ":" + chord.Key);

        var prevented = await EvalAsync(view, $$"""
            (() => {
                {{KeyHelper}}
                return ['c', 'v', 'x', 'z', 'y', 'a', 'w'].map(k => !key(k, 'Key' + k.toUpperCase(), { ctrlKey: true }));
            })()
            """);
        await Task.Delay(200);

        Assert.Equal("[false,false,false,false,false,false,true]", prevented);
        Assert.Equal(new[] { "right:w" }, chords);
    }

    [Theory]
    [InlineData("line", "a\\tb", "kept")]
    [InlineData("line", "a\\nb", "kept")]
    [InlineData("multiline", "a\\u0001b", "kept")]
    [InlineData("multiline", "a\\u007f", "kept")]
    [InlineData("multiline", "a\\tb\\nc", "kepta\\tb\\nc")]
    [InlineData("multiline", "a\\r\\nb", "kepta\\nb")]
    public async Task An_insert_with_a_forbidden_control_is_refused_whole(string mode, string inserted, string expected)
    {
        var (_, view) = await BootInputAsync(mode, "kept");

        var value = await EvalAsync(view, $$"""
            (() => {
                const field = {{Field}};
                field.setSelectionRange(field.value.length, field.value.length);
                const data = new DataTransfer();
                data.setData('text/plain', "{{inserted}}");
                field.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
                return field.value;
            })()
            """);

        Assert.Equal(JsonSerializer.Deserialize<string>("\"" + expected + "\""), JsonSerializer.Deserialize<string>(value));
    }

    [Fact]
    public async Task An_edit_past_the_cap_is_refused_and_one_that_fits_is_taken()
    {
        var (_, view) = await BootInputAsync("line", "");

        var lengths = await EvalAsync(view, $$"""
            (() => {
                const field = {{Field}};
                document.execCommand('insertText', false, 'x'.repeat(65535));
                const first = field.value.length;
                document.execCommand('insertText', false, 'é');
                const second = field.value.length;
                document.execCommand('insertText', false, 'y');
                return [first, second, field.value.length];
            })()
            """);

        Assert.Equal("[65535,65535,65536]", lengths);
    }

    [Fact]
    public async Task A_redraw_of_the_same_question_keeps_what_the_user_typed()
    {
        var (_, view) = await BootInputAsync("line", "");

        var value = await EvalAsync(view, $$"""
            (() => {
                document.execCommand('insertText', false, 'typed');
                agterm.applyAsk({{InputPayload("IN", "line", "stale")}});
                return {{Field}}.value;
            })()
            """);

        Assert.Equal("\"typed\"", value);
    }

    [Fact]
    public async Task Focus_moving_between_the_layer_and_the_field_stays_inside_the_question()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Single(1, "right", "Shell")));
        await _webView.RunAsync<object?>(_ => { view.Focus(); return Task.FromResult<object?>(null); });
        await Task.Delay(500);
        var focus = new System.Collections.Concurrent.ConcurrentQueue<string?>();
        page.AskFocusChanged += focus.Enqueue;

        var refocused = await EvalAsync(view, $$"""
            (() => {
                agterm.applyAsk({{InputPayload("IN", "line", "")}});
                document.querySelector('.ask-layer').focus();
                document.querySelector('.ask-layer').dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true }));
                return document.activeElement === {{Field}};
            })()
            """);
        await Task.Delay(300);

        Assert.Equal("true", refocused);
        Assert.Equal(new string?[] { "IN" }, focus.ToArray());
    }
}
