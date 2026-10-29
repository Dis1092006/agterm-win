using System.Text.Json;
using AgTerm.Xterm;
using Microsoft.Web.WebView2.WinForms;

namespace AgTerm.Xterm.Tests;

[Collection("webview2")]
public sealed class InactiveTerminalScrollLiveTests(WebView2Fixture webView, Xunit.Abstractions.ITestOutputHelper output) : IAsyncLifetime
{
    private WebView2? _view;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => webView.RunAsync(_ =>
    {
        _view?.Dispose();
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(true, 0.5)]
    public async Task Wheel_scrolls_the_pane_under_the_pointer_without_focus_or_mouse_input(bool mouseTracking, double sensitivity)
    {
        var view = await webView.CreateWebViewAsync();
        _view = view;
        await webView.RunAsync(async _ =>
        {
            var page = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await page.InitializeAsync();
            await page.OpenTerminalAsync("left");
            await page.OpenTerminalAsync("right");
            await page.ApplyLayoutAsync(new TerminalLayout.Split(1, "left", "right", 0.5, "left"));
        });
        Assert.Equal("\"function\"", await EvalAsync("typeof agterm.scrollInactiveAt"));
        await EvalAsync($"agterm.applyRendererSettings({{scrollSensitivity:{JsonSerializer.Serialize(sensitivity)}}})");
        await EvalAsync($$"""
            (() => {
              window.scrollReady = false;
              window.scrollData = [];
              let remaining = 2;
              for (const pane of panes.values()) {
                pane.term.onData(data => window.scrollData.push(data));
                const modes = {{(mouseTracking ? "'\\x1b[?1000h\\x1b[?1006h'" : "''")}};
                pane.term.write(modes + Array.from({length: 300}, (_, i) => `history ${i}\r\n`).join(''), () => {
                  if (--remaining === 0) window.scrollReady = true;
                });
              }
            })()
            """);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (await EvalAsync("window.scrollReady") != "true")
        {
            Assert.True(DateTime.UtcNow < deadline, "Terminal output did not finish.");
            await Task.Delay(20);
        }

        var result = await EvalAsync("""
            (() => {
              const left = panes.get('left'), right = panes.get('right');
              const bounds = right.el.getBoundingClientRect();
              const x = (bounds.left + bounds.width / 2) / innerWidth;
              const y = (bounds.top + bounds.height / 2) / innerHeight;
              left.term.scrollToBottom();
              right.term.scrollToBottom();
              const beforeLeft = left.term.buffer.active.viewportY;
              const beforeRight = right.term.buffer.active.viewportY;
              const focused = document.activeElement;
              agterm.scrollInactiveAt(x, y, 120);
              const afterUp = right.term.buffer.active.viewportY;
              agterm.scrollInactiveAt(x, y, 120);
              const afterTwo = right.term.buffer.active.viewportY;
              agterm.scrollInactiveAt(x, y, -240);
              const afterDown = right.term.buffer.active.viewportY;
              agterm.scrollInactiveAt(-1, y, 120);
              return {
                beforeRight,
                leftUnchanged: left.term.buffer.active.viewportY === beforeLeft,
                up: beforeRight - afterUp,
                two: beforeRight - afterTwo,
                down: afterDown - afterTwo,
                outsideIgnored: right.term.buffer.active.viewportY === afterDown,
                focusUnchanged: document.activeElement === focused,
                input: window.scrollData
              };
            })()
            """);
        output.WriteLine(result);
        using var json = JsonDocument.Parse(result);
        var root = json.RootElement;
        Assert.True(root.GetProperty("beforeRight").GetInt32() > 5);
        Assert.True(root.GetProperty("leftUnchanged").GetBoolean());
        Assert.Equal((int)sensitivity, root.GetProperty("up").GetInt32());
        Assert.Equal((int)(2 * sensitivity), root.GetProperty("two").GetInt32());
        Assert.Equal((int)(2 * sensitivity), root.GetProperty("down").GetInt32());
        Assert.True(root.GetProperty("outsideIgnored").GetBoolean());
        Assert.True(root.GetProperty("focusUnchanged").GetBoolean());
        Assert.Empty(root.GetProperty("input").EnumerateArray());
    }

    private Task<string> EvalAsync(string script) =>
        webView.RunAsync(_ => _view!.CoreWebView2.ExecuteScriptAsync(script));
}
