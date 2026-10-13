using System.Text.Json;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class XtermPageHudTests
{
    private readonly FakeScriptHost _host = new();

    private async Task<XtermPage> InitializedAsync()
    {
        var page = new XtermPage(_host);
        var init = page.InitializeAsync();
        _host.RaiseMessage("{\"type\":\"ready\"}");
        await init;
        return page;
    }

    private static TerminalHudView SessionWide(params string[] frames) =>
        new(null,
            new[] { new TerminalHudRow("building", false), new TerminalHudRow("", false), new TerminalHudRow("3 of 12 repos", true) },
            80, null, 10, "center", false, true, null, null, null, frames, frames.Length == 0 ? 0 : 0.1, 1);

    [Fact]
    public async Task ApplyHud_serializes_the_exact_bridge_contract()
    {
        var page = await InitializedAsync();

        await page.ApplyHudAsync(SessionWide("|", "/", "-", "\\"));

        Assert.Equal(
            """agterm.applyHud({"paneId":null,"rows":[{"text":"building","dim":false},{"text":"","dim":false},{"text":"3 of 12 repos","dim":true}],"widthPercent":80,"heightPoints":null,"heightPercent":10,"position":"center","sticky":false,"frame":true,"background":null,"foreground":null,"fontSize":null,"spinnerFrames":["|","/","-","\\"],"spinnerInterval":0.1,"revision":1})""",
            Assert.Single(_host.Scripts, item => item.Contains("agterm.applyHud")));
    }

    [Fact]
    public async Task ApplyHud_serializes_every_panel_field_and_keeps_text_literal()
    {
        var page = await InitializedAsync();

        await page.ApplyHudAsync(new TerminalHudView("right-token", new[] { new TerminalHudRow("a \"quoted\" <tag>", false) },
            35, 132.5, 12, "top-center", true, false, "#2a1a3a", "#e0e0e0", 18, Array.Empty<string>(), 0, 7));

        Assert.Equal(
            """agterm.applyHud({"paneId":"right-token","rows":[{"text":"a \"quoted\" <tag>","dim":false}],"widthPercent":35,"heightPoints":132.5,"heightPercent":12,"position":"top-center","sticky":true,"frame":false,"background":"#2a1a3a","foreground":"#e0e0e0","fontSize":18,"spinnerFrames":[],"spinnerInterval":0,"revision":7})""",
            Assert.Single(_host.Scripts, item => item.Contains("agterm.applyHud")));
    }

    // the encoder may write U+00A0 as an escape; what matters is the frame the page parses
    [Fact]
    public async Task ApplyHud_keeps_the_dot_spinners_no_break_space()
    {
        var page = await InitializedAsync();

        await page.ApplyHudAsync(SessionWide("●", " "));

        var script = Assert.Single(_host.Scripts, item => item.Contains("agterm.applyHud"));
        var json = script["agterm.applyHud(".Length..^1];
        using var payload = JsonDocument.Parse(json);
        Assert.Equal(new[] { "●", " " },
            payload.RootElement.GetProperty("spinnerFrames").EnumerateArray().Select(frame => frame.GetString()));
    }

    [Fact]
    public async Task ApplyHud_with_no_view_clears_the_page()
    {
        var page = await InitializedAsync();
        await page.ApplyHudAsync(null);
        Assert.Contains("agterm.applyHud(null)", _host.Scripts);
    }

    private const string Head = """{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellWidth":8.5,"cellHeight":17,"session":{"width":1200,"height":800}""";

    private async Task<List<(string PaneId, HudSessionMetrics Metrics, HudRect? SinglePane)>> RaisedAsync(string json)
    {
        var page = await InitializedAsync();
        var seen = new List<(string, HudSessionMetrics, HudRect?)>();
        page.HudMetricsReceived += (paneId, metrics, singlePane) => seen.Add((paneId, metrics, singlePane));
        _host.RaiseMessage(json);
        return seen;
    }

    [Fact]
    public async Task Hud_metrics_of_a_shown_split_reach_the_host()
    {
        var seen = await RaisedAsync(Head + ""","left":{"width":600,"height":800},"right":{"width":596,"height":800}}""");

        Assert.Equal(("left-token", new HudSessionMetrics(14, 8.5, 17, new HudRect(1200, 800), new HudRect(600, 800), new HudRect(596, 800)), (HudRect?)null),
            Assert.Single(seen));
    }

    // a single shown pane comes as `pane`: only the caller knows which role that token has
    [Fact]
    public async Task Hud_metrics_of_a_single_shown_pane_carry_its_rect_apart()
    {
        var seen = await RaisedAsync("""{"type":"hudMetrics","paneId":"right-token","fontSize":14,"cellWidth":8.5,"cellHeight":17,"session":{"width":1200,"height":800},"pane":{"width":1200,"height":800}}""");

        Assert.Equal(("right-token", new HudSessionMetrics(14, 8.5, 17, new HudRect(1200, 800), null, null), (HudRect?)new HudRect(1200, 800)),
            Assert.Single(seen));
    }

    [Theory]
    [InlineData("}")]
    [InlineData(""","left":null,"right":null,"pane":null}""")]
    public async Task Hud_metrics_without_pane_rectangles_reach_the_host(string tail)
    {
        var seen = await RaisedAsync(Head + tail);

        Assert.Equal(("left-token", new HudSessionMetrics(14, 8.5, 17, new HudRect(1200, 800), null, null), (HudRect?)null),
            Assert.Single(seen));
    }

    // a zero extent is "not measured" for the store, so it passes; a zero cell would size the panel from padding alone
    [Fact]
    public async Task Hud_metrics_with_zero_rect_sizes_reach_the_host()
    {
        var seen = await RaisedAsync("""{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellWidth":8.5,"cellHeight":17,"session":{"width":0,"height":0},"left":{"width":0,"height":0},"right":{"width":0,"height":0}}""");

        Assert.Equal(new HudSessionMetrics(14, 8.5, 17, new HudRect(0, 0), new HudRect(0, 0), new HudRect(0, 0)), Assert.Single(seen).Metrics);
    }

    [Theory]
    [InlineData("""{"type":"hudMetrics","fontSize":14,"cellWidth":8.5,"cellHeight":17,"session":{"width":1200,"height":800}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"","fontSize":14,"cellWidth":8.5,"cellHeight":17,"session":{"width":1200,"height":800}}""")]
    [InlineData("""{"type":"hudMetrics","mainPaneId":"left-token","fontSize":14,"cellWidth":8.5,"cellHeight":17,"session":{"width":1200,"height":800}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","cellWidth":8.5,"cellHeight":17,"session":{"width":1200,"height":800}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","fontSize":0,"cellWidth":8.5,"cellHeight":17,"session":{"width":1200,"height":800}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellHeight":17,"session":{"width":1200,"height":800}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellWidth":8.5,"session":{"width":1200,"height":800}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellWidth":-1,"cellHeight":17,"session":{"width":1200,"height":800}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellWidth":0,"cellHeight":17,"session":{"width":1200,"height":800}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellWidth":8.5,"cellHeight":0,"session":{"width":1200,"height":800}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellWidth":1e999,"cellHeight":17,"session":{"width":1200,"height":800}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellWidth":8.5,"cellHeight":17,"session":{"width":1e999,"height":800}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellWidth":8.5,"cellHeight":17}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellWidth":8.5,"cellHeight":17,"session":{"width":1200}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellWidth":8.5,"cellHeight":17,"session":{"width":1200,"height":800},"left":{"height":800}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellWidth":8.5,"cellHeight":17,"session":{"width":1200,"height":800},"right":{"width":596,"height":-1}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellWidth":8.5,"cellHeight":17,"session":{"width":1200,"height":800},"pane":{"width":1200}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellWidth":8.5,"cellHeight":17,"session":{"width":1200,"height":800},"pane":{"width":1200,"height":800},"left":{"width":600,"height":800}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellWidth":8.5,"cellHeight":17,"session":{"width":1200,"height":800},"pane":{"width":1200,"height":800},"right":{"width":596,"height":800}}""")]
    [InlineData("""{"type":"hudMetrics","paneId":"left-token","fontSize":14,"cellWidth":"8.5","cellHeight":17,"session":{"width":1200,"height":800}}""")]
    public async Task Malformed_hud_metrics_are_dropped(string json) => Assert.Empty(await RaisedAsync(json));
}
