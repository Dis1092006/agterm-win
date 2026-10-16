using System.Text.Json;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

internal static class TestPaneThemes
{
    // the ANSI colors differ per theme, so a test can tell whose palette a pane has
    public static TerminalTheme Theme(string name, string background, int ansiBase) => new(
        name, "#e5e9f0", background, "#d8dee9", background, "#434c5e", "#eceff4",
        Enumerable.Range(ansiBase, 16).Select(i => $"#{i:x2}{i:x2}{i:x2}").ToArray());

    public static readonly TerminalTheme Nord = Theme("Nord", "#2e3440", ansiBase: 0x00);

    public static readonly TerminalTheme Global = Theme("Global", "#101010", ansiBase: 0x20);
}

public sealed class XtermPagePaneThemeTests
{
    private readonly FakeScriptHost _host = new();

    private async Task<XtermPage> InitializedAsync()
    {
        var page = new XtermPage(_host);
        var initialize = page.InitializeAsync();
        _host.RaiseMessage("""{"type":"ready"}""");
        await initialize;
        return page;
    }

    [Fact]
    public async Task Pane_themes_serialize_every_color_and_a_null_for_the_global_theme()
    {
        var page = await InitializedAsync();

        await page.ApplyPaneThemesAsync(new Dictionary<string, TerminalTheme?>
        {
            ["p1"] = TestPaneThemes.Nord,
            ["p2"] = null,
        });

        var script = Assert.Single(_host.Scripts, item => item.Contains("agterm.applyPaneThemes"));
        Assert.Contains(
            "\"p1\":{\"foregroundColor\":\"#e5e9f0\",\"backgroundColor\":\"#2e3440\",\"cursorColor\":\"#d8dee9\"," +
            "\"cursorTextColor\":\"#2e3440\",\"selectionBackgroundColor\":\"#434c5e\",\"selectionForegroundColor\":\"#eceff4\"," +
            "\"ansiColors\":[\"#000000\"",
            script);
        Assert.Contains("\"p2\":null", script);
    }

    [Fact]
    public async Task Open_with_a_theme_passes_it_to_the_page_beside_the_font_size()
    {
        var page = await InitializedAsync();

        await page.OpenTerminalAsync("p1", fontSize: 16, theme: TestPaneThemes.Nord);

        Assert.Contains("agterm.openPane(\"p1\", {\"fontSize\":16,\"theme\":{\"foregroundColor\":\"#e5e9f0\"", _host.Scripts[^1]);
    }

    [Fact]
    public async Task Open_without_a_theme_keeps_the_old_script()
    {
        var page = await InitializedAsync();

        await page.OpenTerminalAsync("p1");

        Assert.Contains("agterm.openPane(\"p1\", {})", _host.Scripts[^1]);
    }

    [Fact]
    public async Task The_terminal_background_serializes_a_color_or_null()
    {
        var page = await InitializedAsync();

        await page.SetTerminalBackgroundAsync("#2e3440");
        await page.SetTerminalBackgroundAsync(null);

        Assert.Contains("agterm.setTerminalBackground(\"#2e3440\")", _host.Scripts);
        Assert.Contains("agterm.setTerminalBackground(null)", _host.Scripts);
    }
}

/// <summary>Live WebView2 coverage for themes assigned per pane.</summary>
[Collection("webview2")]
public sealed class XtermPagePaneThemeLiveTests
{
    private static readonly string Cmd = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;

    public XtermPagePaneThemeLiveTests(WebView2Fixture webView)
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

    private Task<string> EvalAsync(Microsoft.Web.WebView2.WinForms.WebView2 view, string script) =>
        _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(script));

    private async Task<JsonElement> SettingsAsync(Microsoft.Web.WebView2.WinForms.WebView2 view, string paneId)
    {
        using var doc = JsonDocument.Parse(await EvalAsync(view, $"agterm.getRendererSettings('{paneId}')"));
        return doc.RootElement.Clone();
    }

    private Task ApplyGlobalAsync(XtermPage page, double fontSize = 14) =>
        _webView.RunAsync(_ => page.ApplyRendererSettingsAsync(
            RendererSettingsSnapshot.From(new AppSettings { FontSize = fontSize }, TestPaneThemes.Global)));

    private Task AssignAsync(XtermPage page, string paneId, TerminalTheme? theme) =>
        _webView.RunAsync(_ => page.ApplyPaneThemesAsync(new Dictionary<string, TerminalTheme?> { [paneId] = theme }));

    [Fact]
    public async Task An_assigned_pane_keeps_its_colors_when_the_global_settings_change()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("pt-a"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("pt-b"));
        await AssignAsync(page, "pt-a", TestPaneThemes.Nord);

        await ApplyGlobalAsync(page, fontSize: 20);

        var assigned = await SettingsAsync(view, "pt-a");
        var plain = await SettingsAsync(view, "pt-b");
        Assert.Equal("#2e3440", assigned.GetProperty("backgroundColor").GetString());
        Assert.Equal("#000000", assigned.GetProperty("ansiColors")[0].GetString());
        Assert.Equal("#101010", plain.GetProperty("backgroundColor").GetString());
        Assert.Equal("#202020", plain.GetProperty("ansiColors")[0].GetString());
        // only the colors are the pane's own: the font follows the global settings
        Assert.Equal(20, assigned.GetProperty("fontSize").GetDouble());
    }

    [Fact]
    public async Task Clearing_an_assignment_returns_the_pane_to_the_global_theme()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("pt-c"));
        await _webView.RunAsync(_ => page.ApplyRendererSettingsAsync(
            RendererSettingsSnapshot.From(new AppSettings(), theme: null)));
        await AssignAsync(page, "pt-c", TestPaneThemes.Nord);

        await AssignAsync(page, "pt-c", null);

        var settings = await SettingsAsync(view, "pt-c");
        Assert.Equal("#0C0C0C", settings.GetProperty("backgroundColor").GetString());
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("ansiColors").ValueKind);
    }

    [Fact]
    public async Task A_pane_opened_with_a_theme_shows_it_from_the_start()
    {
        var (page, view) = await BootPageAsync();

        await _webView.RunAsync(_ => page.OpenTerminalAsync("pt-d", theme: TestPaneThemes.Nord));

        Assert.Equal("#2e3440", (await SettingsAsync(view, "pt-d")).GetProperty("backgroundColor").GetString());
    }

    [Fact]
    public async Task An_unknown_pane_id_is_ignored()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("pt-e"));

        var applied = await EvalAsync(view, "agterm.applyPaneThemes({ 'nope': null, 'pt-e': null })");

        Assert.Equal("1", applied);
    }

    [Fact]
    public async Task The_overlay_background_still_wins_over_the_pane_theme()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("pt-base"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("pt-layer"));
        await AssignAsync(page, "pt-layer", TestPaneThemes.Nord);

        Assert.True(await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Layered(
            1,
            new TerminalLayout.Single(1, "pt-base", "shell"),
            new TerminalLayer("pt-layer", sizePercent: 60, backgroundColor: "#123456")))));
        Assert.Equal("#123456", (await SettingsAsync(view, "pt-layer")).GetProperty("backgroundColor").GetString());

        Assert.True(await _webView.RunAsync(_ => page.ApplyLayoutAsync(
            new TerminalLayout.Single(2, "pt-base", "shell"))));
        Assert.Equal("#2e3440", (await SettingsAsync(view, "pt-layer")).GetProperty("backgroundColor").GetString());
    }

    [Fact]
    public async Task A_surface_opened_with_a_theme_reaches_the_page_with_it()
    {
        var (page, view) = await BootPageAsync();
        var surface = await _webView.RunAsync(_ => XtermPaneSurface.OpenAsync(
            page,
            new XtermSurfaceOptions { Argv = [Cmd] },
            CancellationToken.None,
            TestPaneThemes.Nord));
        try
        {
            Assert.Equal("#2e3440", (await SettingsAsync(view, surface.PaneToken!)).GetProperty("backgroundColor").GetString());
        }
        finally
        {
            surface.Teardown();
        }
    }

    private Task<string> BodyBackgroundAsync(Microsoft.Web.WebView2.WinForms.WebView2 view) =>
        EvalAsync(view, "getComputedStyle(document.body).backgroundColor + '|' + getComputedStyle(document.documentElement).backgroundColor");

    [Fact]
    public async Task The_terminal_background_follows_the_call_and_survives_global_settings()
    {
        var (page, view) = await BootPageAsync();
        await ApplyGlobalAsync(page);

        await _webView.RunAsync(_ => page.SetTerminalBackgroundAsync("#2e3440"));
        Assert.Equal("\"rgb(46, 52, 64)|rgb(46, 52, 64)\"", await BodyBackgroundAsync(view));

        await ApplyGlobalAsync(page, fontSize: 18);
        Assert.Equal("\"rgb(46, 52, 64)|rgb(46, 52, 64)\"", await BodyBackgroundAsync(view));

        await _webView.RunAsync(_ => page.SetTerminalBackgroundAsync(null));
        Assert.Equal("\"rgb(16, 16, 16)|rgb(16, 16, 16)\"", await BodyBackgroundAsync(view));
    }

    [Fact]
    public async Task A_translucent_window_keeps_the_page_transparent_under_a_theme_background()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.ApplyRendererSettingsAsync(
            RendererSettingsSnapshot.From(new AppSettings { BackgroundOpacity = 0.5 }, TestPaneThemes.Global)));

        await _webView.RunAsync(_ => page.SetTerminalBackgroundAsync("#2e3440"));

        Assert.Equal("\"rgba(0, 0, 0, 0)|rgba(0, 0, 0, 0)\"", await BodyBackgroundAsync(view));
    }

    private const string HudOnPane = """
        {"paneId":"pt-hud","rows":[{"text":"building","dim":false}],"widthPercent":30,"heightPoints":80,
         "heightPercent":12,"position":"center","sticky":false,"frame":true,"background":null,"foreground":null,
         "fontSize":null,"spinnerFrames":[],"spinnerInterval":0,"revision":1}
        """;

    [Fact]
    public async Task A_hud_takes_its_default_colors_from_its_pane_theme()
    {
        var (page, view) = await BootPageAsync();
        await ApplyGlobalAsync(page);
        await _webView.RunAsync(_ => page.OpenTerminalAsync("pt-hud"));
        await AssignAsync(page, "pt-hud", TestPaneThemes.Nord);

        await EvalAsync(view, $"agterm.applyHud({HudOnPane})");

        Assert.Equal("\"rgb(46, 52, 64)\"",
            await EvalAsync(view, "getComputedStyle(document.querySelector('.hud-layer')).backgroundColor"));
    }

    [Fact]
    public async Task A_session_hud_takes_the_theme_of_the_focused_pane()
    {
        var (page, view) = await BootPageAsync();
        await ApplyGlobalAsync(page);
        await _webView.RunAsync(_ => page.OpenTerminalAsync("pt-session"));
        await AssignAsync(page, "pt-session", TestPaneThemes.Nord);
        Assert.True(await _webView.RunAsync(_ => page.ApplyLayoutAsync(
            new TerminalLayout.Single(1, "pt-session", "shell"))));

        await EvalAsync(view, $"agterm.applyHud({HudOnPane.Replace("\"pt-hud\"", "null")})");

        Assert.Equal("\"rgb(46, 52, 64)\"",
            await EvalAsync(view, "getComputedStyle(document.querySelector('.hud-layer')).backgroundColor"));
    }

    [Fact]
    public async Task A_terminal_question_takes_its_colors_from_its_pane_theme()
    {
        var (page, view) = await BootPageAsync();
        await ApplyGlobalAsync(page);
        await _webView.RunAsync(_ => page.OpenTerminalAsync("pt-ask"));
        await AssignAsync(page, "pt-ask", TestPaneThemes.Nord);
        Assert.True(await _webView.RunAsync(_ => page.ApplyLayoutAsync(
            new TerminalLayout.Single(1, "pt-ask", "shell"))));

        await EvalAsync(view, """
            agterm.applyAsk({"askId":"ASK","paneId":"pt-ask","coveredPaneIds":["pt-ask"],"title":"Continue?",
              "message":null,"buttons":[{"runs":[{"text":"OK","underline":false}],"destructive":false}],
              "highlighted":0,"align":"right","width":null})
            """);

        Assert.Equal("\"rgb(46, 52, 64)\"",
            await EvalAsync(view, "getComputedStyle(document.querySelector('.ask-panel')).backgroundColor"));
    }

    private Task<string> PaneBackgroundAsync(Microsoft.Web.WebView2.WinForms.WebView2 view, string paneId) =>
        EvalAsync(view, $"getComputedStyle(document.querySelector('[data-pane-id=\"{paneId}\"]')).backgroundColor");

    [Fact]
    public async Task A_floating_layer_fills_its_element_with_its_own_theme_background()
    {
        var (page, view) = await BootPageAsync();
        await ApplyGlobalAsync(page);
        await _webView.RunAsync(_ => page.OpenTerminalAsync("lb-base"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("lb-quick"));
        await AssignAsync(page, "lb-base", TestPaneThemes.Nord);

        Assert.True(await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Layered(
            1,
            new TerminalLayout.Single(1, "lb-base", "shell"),
            new TerminalLayer("lb-quick", sizePercent: 90)))));

        // the global theme (#101010), not the Nord session under the layer
        Assert.Equal("\"rgb(16, 16, 16)\"", await PaneBackgroundAsync(view, "lb-quick"));

        await AssignAsync(page, "lb-quick", TestPaneThemes.Nord);
        Assert.Equal("\"rgb(46, 52, 64)\"", await PaneBackgroundAsync(view, "lb-quick"));

        Assert.True(await _webView.RunAsync(_ => page.ApplyLayoutAsync(
            new TerminalLayout.Single(2, "lb-base", "shell"))));
        Assert.Equal("\"rgba(0, 0, 0, 0)\"", await PaneBackgroundAsync(view, "lb-quick"));
    }

    [Fact]
    public async Task A_floating_layer_follows_the_global_settings_while_shown()
    {
        var (page, view) = await BootPageAsync();
        await ApplyGlobalAsync(page);
        await _webView.RunAsync(_ => page.OpenTerminalAsync("lg-base"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("lg-quick"));
        Assert.True(await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Layered(
            1,
            new TerminalLayout.Single(1, "lg-base", "shell"),
            new TerminalLayer("lg-quick", sizePercent: 90)))));

        await _webView.RunAsync(_ => page.ApplyRendererSettingsAsync(
            RendererSettingsSnapshot.From(new AppSettings(), TestPaneThemes.Nord)));

        Assert.Equal("\"rgb(46, 52, 64)\"", await PaneBackgroundAsync(view, "lg-quick"));
    }

    [Fact]
    public async Task An_overlay_color_still_fills_the_layer_element()
    {
        var (page, view) = await BootPageAsync();
        await ApplyGlobalAsync(page);
        await _webView.RunAsync(_ => page.OpenTerminalAsync("lo-base"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("lo-layer"));
        await AssignAsync(page, "lo-layer", TestPaneThemes.Nord);

        Assert.True(await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Layered(
            1,
            new TerminalLayout.Single(1, "lo-base", "shell"),
            new TerminalLayer("lo-layer", sizePercent: 60, backgroundColor: "#123456")))));

        Assert.Equal("\"rgb(18, 52, 86)\"", await PaneBackgroundAsync(view, "lo-layer"));
    }
}
