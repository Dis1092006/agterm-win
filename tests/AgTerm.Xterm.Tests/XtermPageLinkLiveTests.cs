using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using AgTerm.Xterm;
using Microsoft.Web.WebView2.WinForms;

namespace AgTerm.Xterm.Tests;

[Collection("webview2")]
public class XtermPageLinkLiveTests(WebView2Fixture webView, Xunit.Abstractions.ITestOutputHelper output) : IAsyncLifetime
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
    [InlineData(@"Sample API  [3F2504E0-4F89-11D3-9A0C-0305E82C3301]  C:\\git\\github\\example-org\\sample-api  title: ? Issue 328", @"C:\\git\\github\\example-org\\sample-api")]
    [InlineData(@"C:\Program Files\x  title: extra", @"C:\Program Files\x")]
    [InlineData(@"C:\Program Files\x   title: extra", @"C:\Program Files\x")]
    [InlineData("C:\\Program Files\\x\ttitle: extra", @"C:\Program Files\x")]
    [InlineData("'C:\\my  files\\report.txt' extra", @"C:\my  files\report.txt")]
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

    [Fact]
    public async Task A_tab_ends_a_detected_path_before_terminal_tab_expansion()
    {
        var (_, view) = await BootAsync("");
        var result = await EvalAsync(view, """terminalLinkMatches("C:\\folder\\x\ttitle: extra").map(link => link.text)""");
        Assert.Equal(new[] { @"C:\folder\x" }, JsonSerializer.Deserialize<string[]>(result));
    }

    [Theory]
    [InlineData(@"C:\Program Files\x status", true)]
    [InlineData("\"C:\\Program Files\\x status\"", false)]
    [InlineData("'C:\\Program Files\\x status'", false)]
    [InlineData("`C:\\Program Files\\x status`", false)]
    [InlineData("file:///C:/Program%20Files/x", false)]
    [InlineData("\x1b]8;;C:\\Program Files\\x status\x1b\\C:\\Program Files\\x status\x1b]8;;\x1b\\", false)]
    public async Task Only_detected_unquoted_paths_request_prefix_fallback(string text, bool expectedFallback)
    {
        var (page, view) = await BootAsync(text);
        var seen = new ConcurrentQueue<bool>();
        page.TerminalLinkRequested += (_, _, fallback) => seen.Enqueue(fallback);
        var coordinates = await EvalAsync(view, """
            (() => {
              const term = panes.get('links').term;
              const rect = term.element.querySelector('.xterm-screen').getBoundingClientRect();
              return {x:rect.left + rect.width / term.cols * 3.5,
                y:rect.top + rect.height / term.rows * 0.5};
            })()
            """);
        using var json = JsonDocument.Parse(coordinates);
        var x = json.RootElement.GetProperty("x").GetDouble();
        var y = json.RootElement.GetProperty("y").GetDouble();
        await MouseAsync(view, "mousePressed", x, y, 2, "left");
        await MouseAsync(view, "mouseReleased", x, y, 2, "left");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (seen.IsEmpty && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Equal(expectedFallback, Assert.Single(seen));
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
        page.TerminalLinkRequested += (_, target, _) => links.Enqueue(target);
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
        var testCase = new XtermPageLinkLiveTests(webView, output);
        var lifecycle = Assert.IsAssignableFrom<IAsyncLifetime>(testCase);
        var (_, view) = await testCase.BootAsync("view lifecycle");
        await lifecycle.DisposeAsync();
        Assert.True(await webView.RunAsync(_ => Task.FromResult(view.IsDisposed)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Ctrl_click_survives_redrawing_the_link_and_other_rows(bool mouseTracking, bool jitter)
    {
        const string target = @"C:\Windows\System32";
        var (page, view) = await BootAsync("! terraform -chdir=\"" + target + "\" apply \"prod-destroy.tfplan\""
            + (mouseTracking ? "\x1b[?1000h\x1b[?1006h" : ""));
        var seen = new ConcurrentQueue<string>();
        page.TerminalLinkRequested += (_, link, _) => seen.Enqueue(link);
        await EvalAsync(view, """
            window.linkMouseInput = [];
            panes.get('links').term.onData(data => window.linkMouseInput.push(data));
            """);
        var coordinates = await EvalAsync(view, """
            (() => {
              const term = panes.get('links').term;
              const rect = term.element.querySelector('.xterm-screen').getBoundingClientRect();
              return {x:rect.left + rect.width / term.cols * 25.5,
                y:rect.top + rect.height / term.rows * 0.5};
            })()
            """);
        using var json = JsonDocument.Parse(coordinates);
        var x = json.RootElement.GetProperty("x").GetDouble();
        var y = json.RootElement.GetProperty("y").GetDouble();
        await MouseAsync(view, "mouseMoved", x, y, 2);
        await WaitAsync(view, "!!panes.get('links').hoveredLink");
        await MouseAsync(view, "mousePressed", x, y, 2, "left");
        await EvalAsync(view, """
            window.redraws = 0;
            window.redrawTimer = setInterval(() => {
              panes.get('links').term.write(
                '\x1b[1;1H\x1b[2K! terraform -chdir="C:\\Windows\\System32" apply "prod-destroy.tfplan"'
                + '\x1b[3;1H\x1b[2Kworking ' + ++window.redraws);
            }, 16);
            """);
        try
        {
            await WaitAsync(view, "window.redraws >= 5");
            output.WriteLine("Hover after redraw: " + await EvalAsync(view, "panes.get('links').hoveredLink"));
            await MouseAsync(view, "mouseReleased", x + (jitter ? 1 : 0), y, 2, "left");
            await Task.Delay(250);
            Assert.Equal(new[] { target }, seen.ToArray());
            Assert.Equal("[]", await EvalAsync(view, "window.linkMouseInput"));
        }
        finally
        {
            await EvalAsync(view, "clearInterval(window.redrawTimer)");
        }
    }

    [Fact]
    public async Task Ten_live_shells_do_not_delay_the_message_worker_or_existence_probe()
    {
        var (page, view) = await BootAsync(@"C:\Windows\System32");
        var shells = new List<TerminalPane>();
        var timer = Stopwatch.StartNew();
        var launched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var times = new ConcurrentQueue<string>();
        page.TerminalLinkRequested += (paneId, target, allowPathFallback) =>
        {
            times.Enqueue($"message={timer.Elapsed.TotalMilliseconds:F1}ms");
            _ = Task.Run(() =>
            {
                times.Enqueue($"worker={timer.Elapsed.TotalMilliseconds:F1}ms");
                new TerminalLinkOpener(info =>
                {
                    times.Enqueue($"start={timer.Elapsed.TotalMilliseconds:F1}ms, shell={info.UseShellExecute}");
                    launched.TrySetResult();
                }, path =>
                {
                    var exists = File.Exists(path) || Directory.Exists(path);
                    times.Enqueue($"exists={timer.Elapsed.TotalMilliseconds:F1}ms");
                    return exists;
                }).Open(target);
            });
        };
        try
        {
            for (var i = 0; i < 10; i++)
            {
                var id = "timing-" + i;
                shells.Add(await webView.RunAsync(_ => TerminalPane.OpenAsync(page, id, new TerminalPaneOptions
                {
                    Argv = ["pwsh.exe", "-NoLogo", "-NoProfile", "-Command",
                        "while ($true) { [Console]::Write(\"`e[3;1Hworking\"); Start-Sleep -Milliseconds 100 }"],
                })));
            }

            await Task.Delay(1000);
            await EvalAsync(view, """
                for (const [id, pane] of panes) pane.el.style.display = id === 'links' ? '' : 'none';
                """);
            var coordinates = await EvalAsync(view, """
                (() => {
                  const term = panes.get('links').term;
                  const rect = term.element.querySelector('.xterm-screen').getBoundingClientRect();
                  return {x:rect.left + rect.width / term.cols * 3.5,
                    y:rect.top + rect.height / term.rows * 0.5};
                })()
                """);
            using var json = JsonDocument.Parse(coordinates);
            var x = json.RootElement.GetProperty("x").GetDouble();
            var y = json.RootElement.GetProperty("y").GetDouble();
            await MouseAsync(view, "mouseMoved", x, y, 2);
            await WaitAsync(view, "!!panes.get('links').hoveredLink");
            timer.Restart();
            await MouseAsync(view, "mousePressed", x, y, 2, "left");
            await MouseAsync(view, "mouseReleased", x, y, 2, "left");
            await launched.Task.WaitAsync(TimeSpan.FromSeconds(5));
            output.WriteLine(string.Join("; ", times));
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), string.Join("; ", times));
        }
        finally
        {
            foreach (var shell in shells)
                await webView.RunAsync(_ => { shell.Dispose(); return Task.CompletedTask; });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ctrl_click_resolves_a_link_redrawn_under_the_pointer_after_hover_leaves(bool mouseTracking)
    {
        var (page, view) = await BootAsync(@"C:\Windows\System32"
            + (mouseTracking ? "\x1b[?1000h\x1b[?1006h" : ""));
        var seen = new ConcurrentQueue<string>();
        page.TerminalLinkRequested += (_, link, _) => seen.Enqueue(link);
        var coordinates = await EvalAsync(view, """
            (() => {
              const term = panes.get('links').term;
              const rect = term.element.querySelector('.xterm-screen').getBoundingClientRect();
              return {x:rect.left + rect.width / term.cols * 3.5,
                y:rect.top + rect.height / term.rows * 0.5};
            })()
            """);
        using var json = JsonDocument.Parse(coordinates);
        var x = json.RootElement.GetProperty("x").GetDouble();
        var y = json.RootElement.GetProperty("y").GetDouble();
        await MouseAsync(view, "mouseMoved", x, y, 2);
        await WaitAsync(view, "!!panes.get('links').hoveredLink");
        await EvalAsync(view, """
            window.linkOutputWritten = false;
            panes.get('links').term.write('\x1b[1;1H\x1b[2K', () => window.linkOutputWritten = true);
            """);
        await WaitAsync(view, "window.linkOutputWritten");
        await MouseAsync(view, "mouseMoved", x + 1, y, 2);
        await WaitAsync(view, "!panes.get('links').hoveredLink");
        await EvalAsync(view, """
            window.linkOutputWritten = false;
            panes.get('links').term.write('\x1b[1;1HC:\\Windows\\System32', () => window.linkOutputWritten = true);
            """);
        await WaitAsync(view, "window.linkOutputWritten");
        output.WriteLine("Hover after restoring link: " + await EvalAsync(view, "panes.get('links').hoveredLink"));
        await MouseAsync(view, "mousePressed", x + 1, y, 2, "left");
        await MouseAsync(view, "mouseReleased", x + 1, y, 2, "left");
        await Task.Delay(250);
        Assert.Equal(new[] { @"C:\Windows\System32" }, seen.ToArray());
    }

    [Fact]
    public async Task A_replaced_link_cannot_activate_the_old_hover_target()
    {
        var (page, view) = await BootAsync(@"C:\Windows\System32");
        var seen = new ConcurrentQueue<string>();
        page.TerminalLinkRequested += (_, link, _) => seen.Enqueue(link);
        var coordinates = await EvalAsync(view, """
            (() => {
              const term = panes.get('links').term;
              const rect = term.element.querySelector('.xterm-screen').getBoundingClientRect();
              return {x:rect.left + rect.width / term.cols * 3.5,
                y:rect.top + rect.height / term.rows * 0.5};
            })()
            """);
        using var json = JsonDocument.Parse(coordinates);
        var x = json.RootElement.GetProperty("x").GetDouble();
        var y = json.RootElement.GetProperty("y").GetDouble();
        await MouseAsync(view, "mouseMoved", x, y, 2);
        await WaitAsync(view, "!!panes.get('links').hoveredLink");
        await MouseAsync(view, "mousePressed", x, y, 2, "left");
        await EvalAsync(view, """
            window.linkOutputWritten = false;
            panes.get('links').term.write('\x1b[1;1H\x1b[2KC:\\Windows\\notepad.exe',
              () => window.linkOutputWritten = true);
            """);
        await WaitAsync(view, "window.linkOutputWritten");
        await MouseAsync(view, "mouseReleased", x, y, 2, "left");
        await Task.Delay(250);
        Assert.Empty(seen);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_live_tui_can_redraw_during_a_link_gesture(bool mouseTracking, bool osc8)
    {
        var (page, view) = await BootAsync("");
        var seen = new ConcurrentQueue<string>();
        page.TerminalLinkRequested += (_, target, _) => seen.Enqueue(target);
        var label = @"! terraform -chdir=""C:\Windows\System32"" apply ""prod-destroy.tfplan""";
        var line = osc8 ? "\x1b]8;;file:///C:/safe.txt\x1b\\" + label + "\x1b]8;;\x1b\\" : label;
        var prefix = "\x1b[?1049h" + (mouseTracking ? "\x1b[?1000h\x1b[?1006h" : "");
        var frame = "\x1b[1;1H\x1b[2K" + line + "\x1b[3;1H\x1b[2Kworking ";
        var script = $"[Console]::Write('{prefix}'); $i=0; while ($true) {{ [Console]::Write('{frame}' + (++$i)); Start-Sleep -Milliseconds 16 }}";
        using var shell = await webView.RunAsync(_ => TerminalPane.OpenAsync(page, "tui", new TerminalPaneOptions
        {
            Argv = ["pwsh.exe", "-NoLogo", "-NoProfile", "-EncodedCommand",
                Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))],
        }));
        await webView.RunAsync(_ => page.ShowPaneAsync("tui"));
        await EvalAsync(view, """
            panes.get('links').el.style.display = 'none';
            window.tuiMouseInput = [];
            panes.get('tui').term.onData(data => window.tuiMouseInput.push(data));
            """);
        await WaitAsync(view, "panes.get('tui').term.buffer.active.type === 'alternate'"
            + " && panes.get('tui').term.buffer.active.getLine(0)?.translateToString().includes('terraform')");
        var coordinates = await EvalAsync(view, """
            (() => {
              const term = panes.get('tui').term;
              const rect = term.element.querySelector('.xterm-screen').getBoundingClientRect();
              return {x:rect.left + rect.width / term.cols * 25.5,
                y:rect.top + rect.height / term.rows * 0.5};
            })()
            """);
        using var json = JsonDocument.Parse(coordinates);
        var x = json.RootElement.GetProperty("x").GetDouble();
        var y = json.RootElement.GetProperty("y").GetDouble();
        await MouseAsync(view, "mouseMoved", x, y, 2);
        await WaitAsync(view, "!!panes.get('tui').hoveredLink");
        await MouseAsync(view, "mousePressed", x, y, 2, "left");
        var before = await EvalAsync(view, "panes.get('tui').term.buffer.active.getLine(2).translateToString()");
        await WaitAsync(view, $"panes.get('tui').term.buffer.active.getLine(2).translateToString() !== {before}");
        await MouseAsync(view, "mouseReleased", x + 1, y, 2, "left");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (seen.IsEmpty && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Equal(new[] { osc8 ? "file:///C:/safe.txt" : @"C:\Windows\System32" }, seen.ToArray());
        var input = JsonSerializer.Deserialize<string[]>(await EvalAsync(view, "window.tuiMouseInput"))!;
        Assert.DoesNotContain(input, data => data.StartsWith("\x1b[<", StringComparison.Ordinal)
            || data.StartsWith("\x1b[M", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Moving_to_another_cell_cancels_the_link_without_sending_mouse_reports(bool removeLink)
    {
        var (page, view) = await BootAsync("https://example.com\x1b[?1000h\x1b[?1006h");
        var seen = new ConcurrentQueue<string>();
        page.TerminalLinkRequested += (_, link, _) => seen.Enqueue(link);
        await EvalAsync(view, """
            window.linkMouseInput = [];
            panes.get('links').term.onData(data => window.linkMouseInput.push(data));
            """);
        var coordinates = await EvalAsync(view, """
            (() => {
              const term = panes.get('links').term;
              const rect = term.element.querySelector('.xterm-screen').getBoundingClientRect();
              const width = rect.width / term.cols;
              return {x:rect.left + width * 3.5, width, y:rect.top + rect.height / term.rows * 0.5};
            })()
            """);
        using var json = JsonDocument.Parse(coordinates);
        var x = json.RootElement.GetProperty("x").GetDouble();
        var y = json.RootElement.GetProperty("y").GetDouble();
        var width = json.RootElement.GetProperty("width").GetDouble();
        await MouseAsync(view, "mouseMoved", x, y, 2);
        await WaitAsync(view, "!!panes.get('links').hoveredLink");
        await MouseAsync(view, "mousePressed", x, y, 2, "left");
        if (removeLink)
        {
            await EvalAsync(view, """
                window.linkOutputWritten = false;
                panes.get('links').term.write('\x1b[1;1H\x1b[2K', () => window.linkOutputWritten = true);
                """);
            await WaitAsync(view, "window.linkOutputWritten");
        }
        await MouseAsync(view, "mouseMoved", x + width, y, 2, "left");
        await MouseAsync(view, "mouseReleased", x + width, y, 2, "left");
        await Task.Delay(100);
        Assert.Empty(seen);
        Assert.Equal("[]", await EvalAsync(view, "window.linkMouseInput"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hit_testing_follows_wrapped_cells_in_a_scrolled_buffer(bool osc8)
    {
        const string target = "https://example.com/long/path";
        var label = "界e\u0301 " + target;
        var line = osc8 ? "\x1b]8;;file:///C:/safe.txt\x1b\\" + label + "\x1b]8;;\x1b\\" : label;
        var (page, view) = await BootAsync(string.Concat(Enumerable.Repeat("filler\r\n", 5))
            + line + string.Concat(Enumerable.Repeat("\r\nother", 30)), 20);
        var seen = new ConcurrentQueue<string>();
        page.TerminalLinkRequested += (_, link, _) => seen.Enqueue(link);
        await EvalAsync(view, "panes.get('links').term.scrollToLine(5)");
        await WaitAsync(view, "panes.get('links').term.buffer.active.viewportY === 5");
        var coordinates = await EvalAsync(view, """
            (() => {
              const term = panes.get('links').term;
              const rect = term.element.querySelector('.xterm-screen').getBoundingClientRect();
              return {x:rect.left + rect.width / term.cols * 3.5,
                y:rect.top + rect.height / term.rows * 1.5};
            })()
            """);
        using var json = JsonDocument.Parse(coordinates);
        var x = json.RootElement.GetProperty("x").GetDouble();
        var y = json.RootElement.GetProperty("y").GetDouble();
        await MouseAsync(view, "mouseMoved", x, y, 2);
        await MouseAsync(view, "mousePressed", x, y, 2, "left");
        await MouseAsync(view, "mouseReleased", x, y, 2, "left");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (seen.IsEmpty && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Equal(new[] { osc8 ? "file:///C:/safe.txt" : target }, seen.ToArray());
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
        page.TerminalLinkRequested += (_, target, _) => seen.Enqueue(target);
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
        page.TerminalLinkRequested += (_, target, _) => seen.Enqueue(target);
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
        page.TerminalLinkRequested += (_, target, fallback) =>
        {
            Assert.False(fallback);
            seen.Enqueue(target);
        };
        var result = await EvalAsync(view, """
            (() => {
              const term = panes.get('links').term;
              const handler = term.options.linkHandler;
              window.open = () => { throw new Error('browser default must not run'); };
              handler.activate(new MouseEvent('click'), 'file:///C:/safe.txt');
              handler.activate(new MouseEvent('click', {ctrlKey:true}), 'file:///C:/safe.txt',
                {start:{x:1,y:1},end:{x:19,y:1}});
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
