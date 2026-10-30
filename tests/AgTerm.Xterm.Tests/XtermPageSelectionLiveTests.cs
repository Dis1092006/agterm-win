using System.Collections.Concurrent;
using System.Text.Json;
using AgTerm.Core.Model;
using AgTerm.Xterm;
using Microsoft.Web.WebView2.WinForms;

namespace AgTerm.Xterm.Tests;

/// <summary>Copy on select and middle-click paste against the real page with real mouse input
/// (docs/PLATFORM-NOTES.md "Selection buffer and middle-click paste").</summary>
[Collection("webview2")]
public class XtermPageSelectionLiveTests(WebView2Fixture webView) : IAsyncLifetime
{
    private const string Pane = "sel";
    private WebView2? _view;
    private readonly ConcurrentQueue<string> _copied = new();
    private readonly ConcurrentQueue<bool> _pasteRequests = new();
    private readonly ConcurrentQueue<string> _input = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => webView.RunAsync(_ =>
    {
        _view?.Dispose();
        _view = null;
        return Task.CompletedTask;
    });

    private async Task<(XtermPage Page, WebView2 View)> BootAsync(string text, string? copyOnSelect = null)
    {
        var view = await webView.CreateWebViewAsync();
        _view = view;
        var page = await webView.RunAsync(async _ =>
        {
            var p = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await p.InitializeAsync();
            await p.ApplyRendererSettingsAsync(RendererSettingsSnapshot.From(new AppSettings { CopyOnSelect = copyOnSelect }));
            var terminal = await p.OpenTerminalAsync(Pane);
            terminal.DataReceived += data => { if (data.Length > 0) _input.Enqueue(data); };
            await p.ApplyLayoutAsync(new TerminalLayout.Single(1, Pane, "Selection"));
            return p;
        });
        page.SelectionCopied += (_, copied) => _copied.Enqueue(copied);
        page.SelectionPasteRequested += (_, bracketed) => _pasteRequests.Enqueue(bracketed);
        await WriteAsync(view, text);
        return (page, view);
    }

    private async Task WriteAsync(WebView2 view, string text)
    {
        await EvalAsync(view, $$"""
            (async () => {
              const term = panes.get('{{Pane}}').term;
              term.resize(40, 10);
              window.selectionOutputWritten = false;
              await new Promise(resolve => term.write({{JsonSerializer.Serialize(text)}}, () => {
                window.selectionOutputWritten = true;
                resolve();
              }));
            })()
            """);
        await WaitAsync(view, "window.selectionOutputWritten");
    }

    private Task<string> EvalAsync(WebView2 view, string script) =>
        webView.RunAsync(_ => view.CoreWebView2.ExecuteScriptAsync(script));

    private async Task WaitAsync(WebView2 view, string condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (await EvalAsync(view, $"Boolean({condition})") != "true")
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(condition);
            await Task.Delay(20);
        }
    }

    private async Task<(double X, double Y)> CellAsync(WebView2 view, double col, double row)
    {
        var raw = await EvalAsync(view, $$"""
            (() => {
              const term = panes.get('{{Pane}}').term;
              const rect = term.element.querySelector('.xterm-screen').getBoundingClientRect();
              return {x:rect.left + rect.width / term.cols * {{col}}, y:rect.top + rect.height / term.rows * {{row}}};
            })()
            """);
        using var json = JsonDocument.Parse(raw);
        return (json.RootElement.GetProperty("x").GetDouble(), json.RootElement.GetProperty("y").GetDouble());
    }

    private Task MouseAsync(WebView2 view, string type, (double X, double Y) at, string button = "none",
        int clickCount = 0, int modifiers = 0, int buttons = 0) =>
        webView.RunAsync(_ => view.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
            JsonSerializer.Serialize(new { type, x = at.X, y = at.Y, modifiers, button, buttons, clickCount })));

    private async Task DragAsync(WebView2 view, (double X, double Y) from, (double X, double Y) to, int modifiers = 0)
    {
        await MouseAsync(view, "mouseMoved", from, modifiers: modifiers);
        await MouseAsync(view, "mousePressed", from, "left", 1, modifiers, 1);
        await MouseAsync(view, "mouseMoved", ((from.X + to.X) / 2, (from.Y + to.Y) / 2), "left", modifiers: modifiers, buttons: 1);
        await MouseAsync(view, "mouseMoved", to, "left", modifiers: modifiers, buttons: 1);
        await MouseAsync(view, "mouseReleased", to, "left", 1, modifiers);
    }

    private async Task MiddleClickAsync(WebView2 view, (double X, double Y) at, int modifiers = 0)
    {
        await MouseAsync(view, "mouseMoved", at, modifiers: modifiers);
        await MouseAsync(view, "mousePressed", at, "middle", 1, modifiers, 4);
        await MouseAsync(view, "mouseReleased", at, "middle", 1, modifiers);
    }

    // a posted message reaches the host after the script round trip; this lets any late one land
    private async Task SettleAsync(WebView2 view)
    {
        await EvalAsync(view, "new Promise(resolve => setTimeout(resolve, 50))");
        await Task.Delay(200);
    }

    private static async Task<T> NextAsync<T>(ConcurrentQueue<T> queue)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (queue.TryDequeue(out var item)) return item;
            await Task.Delay(20);
        }
        throw new TimeoutException("No message from the page.");
    }

    [Fact]
    public async Task A_drag_copies_the_selection_on_release()
    {
        var (_, view) = await BootAsync("hello world");

        await DragAsync(view, await CellAsync(view, 0.2, 0.5), await CellAsync(view, 4.8, 0.5));

        Assert.Equal("hello", await NextAsync(_copied));
    }

    [Fact]
    public async Task A_double_click_copies_the_word_and_a_triple_click_the_line()
    {
        var (_, view) = await BootAsync("hello world\r\nnext");
        var word = await CellAsync(view, 7.5, 0.5);

        await MouseAsync(view, "mouseMoved", word);
        await MouseAsync(view, "mousePressed", word, "left", 1, buttons: 1);
        await MouseAsync(view, "mouseReleased", word, "left", 1);
        await MouseAsync(view, "mousePressed", word, "left", 2, buttons: 1);
        await MouseAsync(view, "mouseReleased", word, "left", 2);
        Assert.Equal("world", await NextAsync(_copied));
        await SettleAsync(view);
        Assert.All(_copied, text => Assert.Equal("world", text));
        _copied.Clear();

        await MouseAsync(view, "mousePressed", word, "left", 3, buttons: 1);
        await MouseAsync(view, "mouseReleased", word, "left", 3);
        Assert.Equal("hello world", (await NextAsync(_copied)).TrimEnd());
    }

    [Fact]
    public async Task A_shift_click_extends_the_selection_and_copies_it()
    {
        var (_, view) = await BootAsync("hello world");
        await DragAsync(view, await CellAsync(view, 0.2, 0.5), await CellAsync(view, 1.8, 0.5));
        Assert.Equal("he", await NextAsync(_copied));

        var end = await CellAsync(view, 8.8, 0.5);
        await MouseAsync(view, "mouseMoved", end, modifiers: 8);
        await MouseAsync(view, "mousePressed", end, "left", 1, 8, 1);
        await MouseAsync(view, "mouseReleased", end, "left", 1, 8);

        Assert.Equal("hello wor", await NextAsync(_copied));
    }

    [Fact]
    public async Task A_plain_click_copies_nothing()
    {
        var (_, view) = await BootAsync("hello world");
        var at = await CellAsync(view, 2.5, 0.5);

        await MouseAsync(view, "mouseMoved", at);
        await MouseAsync(view, "mousePressed", at, "left", 1, buttons: 1);
        await MouseAsync(view, "mouseReleased", at, "left", 1);
        await SettleAsync(view);

        Assert.Empty(_copied);
    }

    [Fact]
    public async Task Off_copies_nothing_and_the_setting_applies_live()
    {
        var (page, view) = await BootAsync("hello world", copyOnSelect: "off");
        await DragAsync(view, await CellAsync(view, 0.2, 0.5), await CellAsync(view, 4.8, 0.5));
        await SettleAsync(view);
        Assert.Empty(_copied);

        await webView.RunAsync(async _ =>
        {
            await page.ApplyRendererSettingsAsync(RendererSettingsSnapshot.From(new AppSettings { CopyOnSelect = "clipboard" }));
            return true;
        });
        await DragAsync(view, await CellAsync(view, 6.2, 0.5), await CellAsync(view, 10.8, 0.5));

        Assert.Equal("world", await NextAsync(_copied));
    }

    [Fact]
    public async Task A_search_match_is_not_copied()
    {
        var (_, view) = await BootAsync("hello world");

        await EvalAsync(view, $"panes.get('{Pane}').searchAddon.findNext('world')");
        await WaitAsync(view, $"panes.get('{Pane}').term.getSelection() === 'world'");
        await SettleAsync(view);

        Assert.Empty(_copied);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("clipboard", true)]
    [InlineData("off", false)]
    public async Task Select_all_copies_the_buffer_unless_off(string? mode, bool copies)
    {
        var (_, view) = await BootAsync("hello world", copyOnSelect: mode);

        await EvalAsync(view, $"agterm.selectAll('{Pane}')");
        await SettleAsync(view);

        if (copies) Assert.Equal("hello world", (await NextAsync(_copied)).Trim());
        else Assert.Empty(_copied);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("clipboard")]
    [InlineData("off")]
    public async Task A_middle_click_asks_the_host_to_paste_with_the_bracketed_paste_mode(string? mode)
    {
        var (_, view) = await BootAsync("hello", copyOnSelect: mode);
        var at = await CellAsync(view, 2.5, 0.5);

        await MiddleClickAsync(view, at);
        Assert.False(await NextAsync(_pasteRequests));

        await WriteAsync(view, "\u001b[?2004h");
        await MiddleClickAsync(view, at);
        Assert.True(await NextAsync(_pasteRequests));
    }

    [Fact]
    public async Task A_middle_click_does_not_move_focus_or_send_input()
    {
        var (_, view) = await BootAsync("hello");
        await EvalAsync(view, $"agterm.textareaFor('{Pane}').blur()");

        await MiddleClickAsync(view, await CellAsync(view, 2.5, 0.5));
        await NextAsync(_pasteRequests);
        await SettleAsync(view);

        Assert.Equal("false", await EvalAsync(view, $"document.activeElement === agterm.textareaFor('{Pane}')"));
        Assert.Empty(_input);
    }

    [Fact]
    public async Task With_mouse_reporting_the_program_gets_the_click_and_shift_overrides()
    {
        var (_, view) = await BootAsync("hello world\u001b[?1000h\u001b[?1006h");
        var from = await CellAsync(view, 0.2, 0.5);
        var to = await CellAsync(view, 4.8, 0.5);

        await DragAsync(view, from, to);
        await MiddleClickAsync(view, to);
        await SettleAsync(view);
        Assert.Empty(_copied);
        Assert.Empty(_pasteRequests);
        Assert.Contains(_input, data => data.StartsWith("\u001b[<1;", StringComparison.Ordinal));

        await DragAsync(view, from, to, modifiers: 8);
        Assert.Equal("hello", await NextAsync(_copied));
        await MiddleClickAsync(view, to, modifiers: 8);
        Assert.False(await NextAsync(_pasteRequests));
    }
}
