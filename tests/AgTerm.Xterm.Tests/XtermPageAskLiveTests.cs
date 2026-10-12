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
        var intents = new List<string>();
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
                    new KeyboardEvent('keydown', { key: 'n', code: 'KeyN', altKey: true, bubbles: true, cancelable: true }));
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
}
