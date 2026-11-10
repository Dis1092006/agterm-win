using System.Text;
using System.Text.Json;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// Ctrl+Shift+minus (Ctrl+_) against the REAL page. A Windows console gives it the character
/// 0x1f on the US and Russian layouts, and programs read 0x1f as Ctrl+_ (Claude Code's undo).
/// xterm's win32-input-mode record carries the typed '_' instead, so the program read a plain '_'.
/// </summary>
[Collection("webview2")]
public class XtermPageCtrlUnderscoreLiveTests
{
    private const string Win32Mode = "\u001b[?9001h";
    private const string CtrlUnderscoreDown = "\u001b[189;12;31;1;24;1_";
    private const string CtrlUnderscoreUp = "\u001b[189;12;31;0;24;1_";

    private readonly WebView2Fixture _webView;

    public XtermPageCtrlUnderscoreLiveTests(WebView2Fixture webView)
    {
        _webView = webView;
    }

    private sealed record Pane(Microsoft.Web.WebView2.WinForms.WebView2 View, XtermTerminal Terminal, string Id, List<string> Data);

    private sealed record Key(string Type, string KeyName, bool Shift, bool Alt = false);

    private static Key Down(string key, bool shift, bool alt = false) => new("keydown", key, shift, alt);

    private static Key Up(string key, bool shift, bool alt = false) => new("keyup", key, shift, alt);

    private async Task<Pane> OpenAsync(string paneId, bool readlineMarker = false)
    {
        var view = await _webView.CreateWebViewAsync();
        var page = await _webView.RunAsync(async _ =>
        {
            var p = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await p.InitializeAsync();
            return p;
        });
        var terminal = await _webView.RunAsync(_ => page.OpenTerminalAsync(paneId, readlineMarker: readlineMarker));
        var pane = new Pane(view, terminal, paneId, []);
        terminal.DataReceived += d => { lock (pane.Data) pane.Data.Add(d); };
        // ConPTY turns win32-input-mode on as soon as the program starts
        await FeedAsync(pane, Win32Mode);
        return pane;
    }

    private async Task FeedAsync(Pane pane, string output)
    {
        await _webView.RunAsync(_ =>
        {
            pane.Terminal.FeedOutput(Encoding.UTF8.GetBytes(output));
            return Task.FromResult(0);
        });
        await Task.Delay(150);
    }

    /// <summary>Dispatches Ctrl and the minus key on the pane's textarea and returns the data they produced.</summary>
    private async Task<string> KeysAsync(Pane pane, params Key[] keys)
    {
        lock (pane.Data) pane.Data.Clear();
        var events = JsonSerializer.Serialize(keys.Select(k => new
        {
            type = k.Type, key = k.KeyName, shift = k.Shift, alt = k.Alt,
        }));
        await _webView.RunAsync(async _ => await pane.View.CoreWebView2.ExecuteScriptAsync(
            $$"""
            (() => {
              const ta = agterm.textareaFor({{JsonSerializer.Serialize(pane.Id)}});
              for (const e of {{events}}) {
                const ev = new KeyboardEvent(e.type, {
                  key: e.key, code: "Minus", ctrlKey: true, shiftKey: e.shift, altKey: e.alt,
                  cancelable: true, bubbles: true
                });
                Object.defineProperty(ev, "keyCode", { value: 189 });
                Object.defineProperty(ev, "which", { value: 189 });
                ta.dispatchEvent(ev);
              }
            })()
            """));
        await Task.Delay(200);
        lock (pane.Data) return string.Concat(pane.Data);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ctrl_shift_minus_sends_the_record_with_the_unit_separator(bool readline)
    {
        var pane = await OpenAsync("cu-record", readlineMarker: readline);
        if (readline) await FeedAsync(pane, "\u001b]7777;agterm-readline=1\u0007");

        Assert.Equal(CtrlUnderscoreDown, await KeysAsync(pane, Down("_", shift: true)));
        Assert.Equal(CtrlUnderscoreUp, await KeysAsync(pane, Up("_", shift: true)));
    }

    // A Windows console gives Ctrl+minus no character, and xterm's record with the typed '-' stays.
    [Fact]
    public async Task Ctrl_minus_keeps_xterms_record()
    {
        var pane = await OpenAsync("cu-ctrl-minus");

        Assert.Equal("\u001b[189;12;45;1;8;1_", await KeysAsync(pane, Down("-", shift: false)));
    }

    [Fact]
    public async Task With_kitty_flags_pushed_ctrl_shift_minus_keeps_the_kitty_encoding()
    {
        var pane = await OpenAsync("cu-kitty");
        await FeedAsync(pane, "\u001b[>1u");

        Assert.DoesNotContain("189;12;31", await KeysAsync(pane, Down("_", shift: true)));
    }
}
