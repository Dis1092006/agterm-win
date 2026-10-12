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
                key('Enter', 'Enter');
                key('Escape', 'Escape');
                key('н', 'KeyY');
                key('y', 'KeyY', { ctrlKey: true });
            })()
            """));
        await Task.Delay(200);

        Assert.Equal(new[] { "Forward", "Backward", "Activate", "Cancel", "Hotkey:y" }, intents);
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
}
