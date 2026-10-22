using System.Collections.Concurrent;
using System.Text.Json;
using AgTerm.Xterm;
using Microsoft.Web.WebView2.WinForms;

namespace AgTerm.Xterm.Tests;

[Collection("webview2")]
public class XtermPageLinkLiveTests(WebView2Fixture webView) : IAsyncLifetime
{
    private WebView2? _view;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => webView.RunAsync(_ =>
    {
        _view?.Dispose();
        _view = null;
        return Task.CompletedTask;
    });

    private async Task<(XtermPage Page, WebView2 View)> BootAsync(string text, int cols = 100)
    {
        var view = await webView.CreateWebViewAsync();
        _view = view;
        var page = await webView.RunAsync(async _ =>
        {
            var p = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await p.InitializeAsync();
            await p.OpenTerminalAsync("links");
            return p;
        });
        await EvalAsync(view, $$"""
            (async () => {
              const term = panes.get('links').term;
              term.resize({{cols}}, 20);
              window.linkOutputWritten = false;
              await new Promise(resolve => term.write({{JsonSerializer.Serialize(text)}}, () => {
                window.linkOutputWritten = true;
                resolve();
              }));
            })()
            """);
        await WaitAsync(view, "window.linkOutputWritten");
        return (page, view);
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

    [Theory]
    [InlineData("https://example.com/a?q=1#frag", "https://example.com/a?q=1#frag")]
    [InlineData("(https://example.com).", "https://example.com")]
    [InlineData("https://en.wikipedia.org/wiki/Rust_(video_game)", "https://en.wikipedia.org/wiki/Rust_(video_game)")]
    [InlineData("mailto:someone@example.com", "mailto:someone@example.com")]
    [InlineData("ftp://host/file.txt", "ftp://host/file.txt")]
    [InlineData("file:///C:/my%20files/x.txt", "file:///C:/my%20files/x.txt")]
    [InlineData(@"C:\Users\me\scratchpad\notes-2026-10-22.txt", @"C:\Users\me\scratchpad\notes-2026-10-22.txt")]
    [InlineData("\"C:\\my files\\report.txt\" extra", @"C:\my files\report.txt")]
    [InlineData("`C:\\my files\\report.txt` extra", @"C:\my files\report.txt")]
    [InlineData(@"C:\my files\report.txt   ", @"C:\my files\report.txt")]
    public async Task The_provider_detects_urls_and_drive_paths(string text, string expected)
    {
        var (_, view) = await BootAsync(text);
        var result = await EvalAsync(view, """
            (() => {
              let found;
              panes.get('links').linkProvider.provideLinks(1, links => found = links.map(link => link.text));
              return found;
            })()
            """);
        Assert.Equal(new[] { expected }, JsonSerializer.Deserialize<string[]>(result));
    }

    [Theory]
    [InlineData(@"C:relative.txt")]
    [InlineData(@"\\server\share\x.txt")]
    [InlineData(@"\\?\C:\x.txt")]
    [InlineData(@"\\.\C:\x.txt")]
    public async Task The_provider_does_not_extract_drive_paths_from_remote_or_device_paths(string text)
    {
        var (_, view) = await BootAsync(text);
        var result = await EvalAsync(view, """
            (() => {
              let found;
              panes.get('links').linkProvider.provideLinks(1, links => found = links);
              return found.length;
            })()
            """);
        Assert.Equal("0", result);
    }

    [Fact]
    public async Task Wrapped_links_have_cell_ranges_on_each_row_including_wide_and_combining_text()
    {
        var target = "https://example.com/long/path";
        var (_, view) = await BootAsync("界e\u0301 " + target, 20);
        var result = await EvalAsync(view, """
            (() => {
              const provider = panes.get('links').linkProvider;
              const rows = [1, 2].map(y => {
                let links;
                provider.provideLinks(y, value => links = value);
                return links.map(link => ({text:link.text, range:link.range}));
              });
              return rows;
            })()
            """);
        using var json = JsonDocument.Parse(result);
        foreach (var row in json.RootElement.EnumerateArray())
        {
            var link = Assert.Single(row.EnumerateArray());
            Assert.Equal(target, link.GetProperty("text").GetString());
            Assert.Equal(5, link.GetProperty("range").GetProperty("start").GetProperty("x").GetInt32());
            Assert.Equal(1, link.GetProperty("range").GetProperty("start").GetProperty("y").GetInt32());
            Assert.Equal(2, link.GetProperty("range").GetProperty("end").GetProperty("y").GetInt32());
        }
    }

    [Fact]
    public async Task Hover_decorations_require_ctrl()
    {
        var (_, view) = await BootAsync("https://example.com");
        var result = await EvalAsync(view, """
                (() => {
                  let links;
                  panes.get('links').linkProvider.provideLinks(1, value => links = value);
                  const link = links[0];
                  link.hover(new MouseEvent('mousemove'), link.text);
                  const ordinary = {...link.decorations};
                  link.hover(new MouseEvent('mousemove', {ctrlKey:true}), link.text);
                  const ctrl = {...link.decorations};
                  link.hover(new MouseEvent('mousemove', {ctrlKey:true,altKey:true}), link.text);
                  return {ordinary, ctrl, alt:{...link.decorations}};
                })()
                """);
        using var json = JsonDocument.Parse(result);
        Assert.False(json.RootElement.GetProperty("ordinary").GetProperty("underline").GetBoolean());
        Assert.True(json.RootElement.GetProperty("ctrl").GetProperty("underline").GetBoolean());
        Assert.True(json.RootElement.GetProperty("ctrl").GetProperty("pointerCursor").GetBoolean());
        Assert.False(json.RootElement.GetProperty("alt").GetProperty("underline").GetBoolean());
    }

    private async Task MouseAsync(WebView2 view, string type, double x, double y, int modifiers, string button = "none")
    {
        await webView.RunAsync(_ => view.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
            JsonSerializer.Serialize(new { type, x, y, modifiers, button, clickCount = button == "none" ? 0 : 1 })));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_mouse_ctrl_click_prefers_osc8_and_does_not_send_mouse_reports(bool osc8)
    {
        var text = osc8 ? "\x1b]8;;file:///C:/safe.txt\x1b\\https://example.com\x1b]8;;\x1b\\" : "https://example.com";
        var (page, view) = await BootAsync(text + "\x1b[?1000h\x1b[?1006h");
        var links = new ConcurrentQueue<string>();
        var input = new ConcurrentQueue<string>();
        page.TerminalLinkRequested += (_, target) => links.Enqueue(target);
        await webView.RunAsync(async _ =>
        {
            var terminal = await page.OpenTerminalAsync("other");
            terminal.DataReceived += input.Enqueue;
        });
        // The visible pane and the second pane must have separate callbacks.
        await EvalAsync(view, """
                panes.get('links').term.onData(data => window.linkMouseInput.push(data));
                window.linkMouseInput = [];
                """);
        var coordinates = await EvalAsync(view, """
                (() => {
                  const term = panes.get('links').term;
                  const rect = term.element.querySelector('.xterm-screen').getBoundingClientRect();
                  return {x:rect.left + rect.width / term.cols * 3.5, y:rect.top + rect.height / term.rows * 0.5};
                })()
                """);
        using var json = JsonDocument.Parse(coordinates);
        var x = json.RootElement.GetProperty("x").GetDouble();
        var y = json.RootElement.GetProperty("y").GetDouble();
        await MouseAsync(view, "mouseMoved", x, y, 2);
        await WaitAsync(view, "!!panes.get('links').hoveredLink");
        await MouseAsync(view, "mousePressed", x, y, 2, "left");
        await MouseAsync(view, "mouseReleased", x, y, 2, "left");
        await WaitAsync(view, "true");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (links.IsEmpty && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Equal(new[] { osc8 ? "file:///C:/safe.txt" : "https://example.com" }, links.ToArray());
        Assert.Equal("[]", await EvalAsync(view, "window.linkMouseInput"));
        Assert.Empty(input);
    }

    [Fact]
    public async Task The_test_case_disposes_its_WebView_before_releasing_the_shared_fixture()
    {
        var testCase = new XtermPageLinkLiveTests(webView);
        var lifecycle = Assert.IsAssignableFrom<IAsyncLifetime>(testCase);
        var (_, view) = await testCase.BootAsync("view lifecycle");
        await lifecycle.DisposeAsync();
        Assert.True(await webView.RunAsync(_ => Task.FromResult(view.IsDisposed)));
    }

    [Theory]
    [InlineData(".", 1)]
    [InlineData(".", 10000)]
    [InlineData(",", 10000)]
    [InlineData(".,", 10000)]
    public async Task Punctuation_only_url_bodies_complete_within_the_script_budget(string punctuation, int count)
    {
        var (_, view) = await BootAsync("matcher regression");
        var result = await webView.RunAsync(_ => view.CoreWebView2.CallDevToolsProtocolMethodAsync(
            "Runtime.evaluate", JsonSerializer.Serialize(new
            {
                expression = $"terminalLinkMatches('https://' + {JsonSerializer.Serialize(punctuation)}.repeat({count})"
                    + $" + ' https://example.com/path' + '.'.repeat({count})).map(link => link.text)",
                timeout = 1000,
                returnByValue = true,
            }))).WaitAsync(TimeSpan.FromSeconds(10));
        using var json = JsonDocument.Parse(result);
        Assert.False(json.RootElement.TryGetProperty("exceptionDetails", out _), result);
        Assert.Equal(new[] { "https://example.com/path" },
            json.RootElement.GetProperty("result").GetProperty("value").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task Hard_newlines_do_not_join_links()
    {
        var (_, view) = await BootAsync("https://example.com/first\r\nsecond.txt");
        var result = await EvalAsync(view, """
            (() => {
              let links;
              panes.get('links').linkProvider.provideLinks(1, value => links = value);
              return links.map(link => link.text);
            })()
            """);
        Assert.Equal(new[] { "https://example.com/first" }, JsonSerializer.Deserialize<string[]>(result));
    }

    [Theory]
    [InlineData(@"C:\123\界.txt", 8)]
    [InlineData(@"C:\my files\report.txt", 10)]
    public async Task Wrapped_drive_paths_do_not_gain_padding_cells(string target, int cols)
    {
        var (_, view) = await BootAsync(target, cols);
        var result = await EvalAsync(view, """
            (() => {
              let links;
              panes.get('links').linkProvider.provideLinks(1, value => links = value);
              return links.map(link => link.text);
            })()
            """);
        Assert.Equal(new[] { target }, JsonSerializer.Deserialize<string[]>(result));
    }

    [Fact]
    public async Task Ordinary_mouse_drag_still_selects_terminal_text_and_never_activates_a_link()
    {
        var (page, view) = await BootAsync("https://example.com");
        var seen = new ConcurrentQueue<string>();
        page.TerminalLinkRequested += (_, target) => seen.Enqueue(target);
        var coordinates = await EvalAsync(view, """
            (() => {
              const term = panes.get('links').term;
              const rect = term.element.querySelector('.xterm-screen').getBoundingClientRect();
              return {x:rect.left + rect.width / term.cols * 0.2,
                endX:rect.left + rect.width / term.cols * 5.2,
                y:rect.top + rect.height / term.rows * 0.5};
            })()
            """);
        using var json = JsonDocument.Parse(coordinates);
        var x = json.RootElement.GetProperty("x").GetDouble();
        var endX = json.RootElement.GetProperty("endX").GetDouble();
        var y = json.RootElement.GetProperty("y").GetDouble();
        await MouseAsync(view, "mouseMoved", x, y, 0);
        await MouseAsync(view, "mousePressed", x, y, 0, "left");
        await MouseAsync(view, "mouseMoved", endX, y, 0, "left");
        await MouseAsync(view, "mouseReleased", endX, y, 0, "left");
        Assert.Equal("\"https\"", await EvalAsync(view, "panes.get('links').term.getSelection()"));
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Only_ctrl_left_click_activates_and_unknown_or_closed_panes_cannot_post()
    {
        var (page, view) = await BootAsync("https://example.com");
        var seen = new ConcurrentQueue<string>();
        page.TerminalLinkRequested += (_, target) => seen.Enqueue(target);
        await EvalAsync(view, """
            (() => {
              let links;
              panes.get('links').linkProvider.provideLinks(1, value => links = value);
              const link = links[0];
              for (const init of [{}, {ctrlKey:true,shiftKey:true}, {ctrlKey:true,altKey:true},
                                  {metaKey:true}, {ctrlKey:true,button:2}])
                link.activate(new MouseEvent('click', init), link.text);
              return true;
            })()
            """);
        Assert.Empty(seen);
        await EvalAsync(view, """
            (() => {
              let links;
              panes.get('links').linkProvider.provideLinks(1, value => links = value);
              window.staleLink = links[0];
              staleLink.activate(new MouseEvent('click', {ctrlKey:true,button:0}), staleLink.text);
            })()
            """);
        await WaitAsync(view, "true");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (seen.IsEmpty && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Equal(new[] { "https://example.com" }, seen.ToArray());
        await webView.RunAsync(_ => page.ClosePaneAsync("links"));
        await EvalAsync(view, "staleLink.activate(new MouseEvent('click', {ctrlKey:true}), staleLink.text)");
        Assert.Single(seen);
    }

    [Fact]
    public async Task Osc8_uses_its_target_instead_of_its_label_and_never_the_browser_default()
    {
        var (page, view) = await BootAsync("\x1b]8;;file:///C:/safe.txt\x1b\\https://example.com\x1b]8;;\x1b\\");
        var seen = new ConcurrentQueue<string>();
        page.TerminalLinkRequested += (_, target) => seen.Enqueue(target);
        var result = await EvalAsync(view, """
            (() => {
              const term = panes.get('links').term;
              const handler = term.options.linkHandler;
              window.open = () => { throw new Error('browser default must not run'); };
              handler.activate(new MouseEvent('click'), 'file:///C:/safe.txt');
              handler.activate(new MouseEvent('click', {ctrlKey:true}), 'file:///C:/safe.txt');
              return {nonHttp:handler.allowNonHttpProtocols};
            })()
            """);
        using var json = JsonDocument.Parse(result);
        Assert.True(json.RootElement.GetProperty("nonHttp").GetBoolean());
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (seen.IsEmpty && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Equal(new[] { "file:///C:/safe.txt" }, seen.ToArray());
    }
}
