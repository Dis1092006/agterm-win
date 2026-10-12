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
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
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
}
