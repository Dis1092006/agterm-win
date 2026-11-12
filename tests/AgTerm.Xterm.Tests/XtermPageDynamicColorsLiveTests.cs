using System.Text;
using System.Text.Json;
using AgTerm.Core.Model;
using AgTerm.Xterm;
using Microsoft.Web.WebView2.WinForms;

namespace AgTerm.Xterm.Tests;

[Collection("webview2")]
public class XtermPageDynamicColorsLiveTests(WebView2Fixture webView) : IAsyncLifetime
{
    private WebView2? _view;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => webView.RunAsync(_ =>
    {
        _view?.Dispose();
        _view = null;
        return Task.CompletedTask;
    });

    private async Task<(XtermPage Page, WebView2 View, XtermTerminal Terminal, List<string> Data)> OpenAsync(
        TerminalTheme theme)
    {
        var view = await webView.CreateWebViewAsync();
        _view = view;
        var data = new List<string>();
        var (page, terminal) = await webView.RunAsync(async _ =>
        {
            var p = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await p.InitializeAsync();
            var t = await p.OpenTerminalAsync("colors", theme: theme);
            return (p, t);
        });
        terminal.DataReceived += chunk => { lock (data) data.Add(chunk); };
        return (page, view, terminal, data);
    }

    private async Task FeedAsync(WebView2 view, XtermTerminal terminal, string output)
    {
        await webView.RunAsync(_ =>
        {
            terminal.FeedOutput(Encoding.UTF8.GetBytes(output));
            return Task.CompletedTask;
        });
        await webView.RunAsync(_ => view.CoreWebView2.ExecuteScriptAsync("""
            new Promise(resolve => panes.get('colors').term.write('', resolve))
            """));
    }

    private async Task<JsonElement> ActiveColorsAsync(WebView2 view)
    {
        var raw = await webView.RunAsync(_ => view.CoreWebView2.ExecuteScriptAsync("""
            (() => {
              const colors = panes.get('colors').term._core._themeService.colors;
              return {
                foreground: colors.foreground.css.toLowerCase(),
                background: colors.background.css.toLowerCase(),
                cursor: colors.cursor.css.toLowerCase(),
                ansi0: colors.ansi[0].css.toLowerCase(),
                ansi4: colors.ansi[4].css.toLowerCase(),
              };
            })()
            """));
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Osc_color_mutations_do_not_override_the_pane_theme_while_queries_still_reply()
    {
        var cobaltNeon = ThemeCatalog.Default.Resolve("Cobalt Neon")!;
        var (_, view, terminal, data) = await OpenAsync(cobaltNeon);

        // Copilot CLI's startup and dynamic color sequence: query palette + scheme, then try to set
        // its own GitHub dark background/foreground/cursor/indexed colors via OSC 11/10/12/4.
        await FeedAsync(view, terminal,
            "\u001b]4;0;?\u001b\\"
            + "\u001b]10;?\u001b\\"
            + "\u001b]11;?\u001b\\"
            + "\u001b]12;?\u001b\\"
            + "\u001b[?996n"
            + "\u001b]11;#0D1117\u0007"
            + "\u001b]10;#F0F6FC\u0007"
            + "\u001b]12;#FF00FF\u0007"
            + "\u001b]4;0;#010203;4;#040506\u0007"
            + "\u001b]10;#112233;#445566;#778899\u001b\\");

        var colors = await ActiveColorsAsync(view);
        Assert.Equal("#8ff586", colors.GetProperty("foreground").GetString());
        Assert.Equal("#142838", colors.GetProperty("background").GetString());
        Assert.Equal("#c4206f", colors.GetProperty("cursor").GetString());
        Assert.Equal("#142631", colors.GetProperty("ansi0").GetString());
        Assert.Equal("#8ff586", colors.GetProperty("ansi4").GetString());

        string replies;
        lock (data) replies = string.Concat(data);
        Assert.Contains("\u001b]4;0;rgb:1414/2626/3131\u001b\\", replies, StringComparison.Ordinal);
        Assert.Contains("\u001b]10;rgb:8f8f/f5f5/8686\u001b\\", replies, StringComparison.Ordinal);
        Assert.Contains("\u001b]11;rgb:1414/2828/3838\u001b\\", replies, StringComparison.Ordinal);
        Assert.Contains("\u001b]12;rgb:c4c4/2020/6f6f\u001b\\", replies, StringComparison.Ordinal);
        Assert.Contains("\u001b[?997;1n", replies, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chained_osc_queries_still_reply_when_paired_with_blocked_mutations()
    {
        var manPage = ThemeCatalog.Default.Resolve("Man Page")!;
        var (_, view, terminal, data) = await OpenAsync(manPage);

        await FeedAsync(view, terminal,
            "\u001b]10;#1f2328;?\u001b\\"
            + "\u001b]4;0;#ffffff;1;?\u001b\\"
            + "\u001b[?996n");

        var colors = await ActiveColorsAsync(view);
        Assert.Equal("#000000", colors.GetProperty("foreground").GetString());
        Assert.Equal("#fef49c", colors.GetProperty("background").GetString());
        Assert.Equal("#000000", colors.GetProperty("ansi0").GetString());

        string replies;
        lock (data) replies = string.Concat(data);
        Assert.Contains("\u001b]11;rgb:fefe/f4f4/9c9c\u001b\\", replies, StringComparison.Ordinal);
        Assert.Contains("\u001b]4;1;rgb:cccc/0000/0000\u001b\\", replies, StringComparison.Ordinal);
        Assert.Contains("\u001b[?997;2n", replies, StringComparison.Ordinal);
    }
}
