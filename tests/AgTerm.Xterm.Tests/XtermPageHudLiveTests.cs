using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

[Collection("webview2")]
public class XtermPageHudLiveTests
{
    private readonly WebView2Fixture _webView;

    public XtermPageHudLiveTests(WebView2Fixture webView)
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

    private async Task<string> EvalAsync(Microsoft.Web.WebView2.WinForms.WebView2 view, string script) =>
        await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(script));

    private async Task WaitForScriptAsync(Microsoft.Web.WebView2.WinForms.WebView2 view, string condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (await EvalAsync(view, $"(() => {condition})()") != "true")
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Not met within the timeout: {condition}");
            await Task.Delay(25);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Condition not met within the timeout.");
            await Task.Delay(25);
        }
    }

    // the Task 8 payload, key for key
    private static string Hud(
        string? paneId = null,
        string position = "center",
        bool sticky = false,
        bool frame = true,
        int widthPercent = 30,
        double? heightPoints = 80,
        int heightPercent = 12,
        string? background = null,
        string? foreground = null,
        double? fontSize = null,
        string[]? spinnerFrames = null,
        double spinnerInterval = 0,
        int revision = 1,
        string message = "building") =>
        JsonSerializer.Serialize(new
        {
            paneId,
            rows = new object[]
            {
                new { text = message, dim = false },
                new { text = "", dim = false },
                new { text = "step 2 of 5", dim = true },
            },
            widthPercent,
            heightPoints,
            heightPercent,
            position,
            sticky,
            frame,
            background,
            foreground,
            fontSize,
            spinnerFrames = spinnerFrames ?? Array.Empty<string>(),
            spinnerInterval,
            revision,
        });

    private const string AskOnRight = """
        {"askId":"ASK","paneId":"right","coveredPaneIds":["right"],"title":"Continue?","message":null,
         "buttons":[{"runs":[{"text":"OK","underline":false}],"destructive":false}],"highlighted":0,"align":"right","width":null}
        """;

    private static readonly (string Name, int Vertical, int Horizontal)[] Anchors =
    {
        ("top-left", -1, -1), ("top-center", -1, 0), ("top-right", -1, 1),
        ("center-left", 0, -1), ("center", 0, 0), ("center-right", 0, 1),
        ("bottom-left", 1, -1), ("bottom-center", 1, 0), ("bottom-right", 1, 1),
    };

    // upstream OverlayPanelStyle.panelFrame: centered, then moved toward the anchor's edge by half the
    // room the panel and a 10 percent edge margin leave (no margin when sticky)
    private static double Expected(double extent, double size, int band, bool sticky) =>
        (extent - size) / 2 + band * Math.Max(0, extent * ((1 - size / extent) / 2 - (sticky ? 0 : 0.1)));

    private static void AssertNear(double expected, double actual, string label) =>
        Assert.True(Math.Abs(expected - actual) <= 0.5, $"{label}: expected {expected}, got {actual}");

    private sealed record Reported(string PaneId, HudSessionMetrics Metrics, HudRect? SinglePane);

    [Fact]
    public async Task A_session_wide_panel_is_drawn_in_the_panes_container_with_its_rows_as_text()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));

        var state = await EvalAsync(view, $$"""
            (() => {
                agterm.applyHud({{Hud(message: "<b>building</b>")}});
                const layer = document.querySelector('.hud-layer');
                const rows = Array.from(layer.querySelectorAll('.hud-row'));
                return {
                    parent: layer.parentElement.id,
                    rows: rows.map(r => r.textContent),
                    dim: rows.map(r => r.classList.contains('hud-dim')),
                    markup: layer.querySelectorAll('b').length,
                };
            })()
            """);

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        Assert.Equal("panes", root.GetProperty("parent").GetString());
        Assert.Equal(new[] { "<b>building</b>", "", "step 2 of 5" }, root.GetProperty("rows").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(new[] { false, false, true }, root.GetProperty("dim").EnumerateArray().Select(e => e.GetBoolean()));
        Assert.Equal(0, root.GetProperty("markup").GetInt32());
    }

    [Fact]
    public async Task The_panel_sits_on_each_of_the_nine_anchors_with_and_without_sticky()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));
        var positions = JsonSerializer.Serialize(Anchors.Select(a => a.Name));

        var state = await EvalAsync(view, $$"""
            (() => {
                const base = {{Hud()}};
                const host = document.getElementById('panes');
                const out = [];
                let revision = 1;
                for (const position of {{positions}}) {
                    for (const sticky of [false, true]) {
                        agterm.applyHud(Object.assign({}, base, { position, sticky, revision: revision++ }));
                        const h = host.getBoundingClientRect();
                        const r = document.querySelector('.hud-layer').getBoundingClientRect();
                        out.push({ position, sticky, hostWidth: h.width, hostHeight: h.height,
                                   left: r.left - h.left, top: r.top - h.top, width: r.width, height: r.height });
                    }
                }
                return out;
            })()
            """);

        using var json = JsonDocument.Parse(state);
        var entries = json.RootElement.EnumerateArray().ToArray();
        Assert.Equal(18, entries.Length);
        foreach (var entry in entries)
        {
            var anchor = Anchors.Single(a => a.Name == entry.GetProperty("position").GetString());
            var sticky = entry.GetProperty("sticky").GetBoolean();
            var label = $"{anchor.Name} sticky={sticky}";
            var hostWidth = entry.GetProperty("hostWidth").GetDouble();
            var hostHeight = entry.GetProperty("hostHeight").GetDouble();
            var width = entry.GetProperty("width").GetDouble();
            var height = entry.GetProperty("height").GetDouble();
            AssertNear(hostWidth * 0.3, width, label + " width");
            AssertNear(80, height, label + " height");
            AssertNear(Expected(hostWidth, width, anchor.Horizontal, sticky), entry.GetProperty("left").GetDouble(), label + " left");
            AssertNear(Expected(hostHeight, height, anchor.Vertical, sticky), entry.GetProperty("top").GetDouble(), label + " top");
        }

        JsonElement Entry(string name, bool sticky) =>
            entries.Single(e => e.GetProperty("position").GetString() == name && e.GetProperty("sticky").GetBoolean() == sticky);
        var marginTopLeft = Entry("top-left", false);
        AssertNear(marginTopLeft.GetProperty("hostWidth").GetDouble() * 0.1, marginTopLeft.GetProperty("left").GetDouble(), "edge margin left");
        AssertNear(marginTopLeft.GetProperty("hostHeight").GetDouble() * 0.1, marginTopLeft.GetProperty("top").GetDouble(), "edge margin top");
        var stickyTopLeft = Entry("top-left", true);
        AssertNear(0, stickyTopLeft.GetProperty("left").GetDouble(), "sticky left");
        AssertNear(0, stickyTopLeft.GetProperty("top").GetDouble(), "sticky top");
        var stickyCenter = Entry("center", true);
        var center = Entry("center", false);
        AssertNear(center.GetProperty("left").GetDouble(), stickyCenter.GetProperty("left").GetDouble(), "center ignores sticky");
    }

    [Fact]
    public async Task A_framed_and_a_frameless_panel_differ_in_border_rounding_and_padding_rows()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));

        var state = await EvalAsync(view, $$"""
            (() => {
                const read = () => {
                    const layer = document.querySelector('.hud-layer');
                    const s = getComputedStyle(layer);
                    return {
                        border: s.borderTopWidth,
                        borderColor: s.borderTopColor,
                        radius: s.borderTopLeftRadius,
                        shadow: s.boxShadow,
                        paddingTop: parseFloat(s.paddingTop),
                        paddingLeft: parseFloat(s.paddingLeft),
                        row: layer.querySelector('.hud-row').getBoundingClientRect().height,
                        cell: document.querySelector('.hud-probe').getBoundingClientRect().width,
                    };
                };
                agterm.applyHud({{Hud(frame: true)}});
                const framed = read();
                agterm.applyHud({{Hud(frame: false, revision: 2)}});
                const frameless = read();
                return { framed, frameless };
            })()
            """);

        using var json = JsonDocument.Parse(state);
        var framed = json.RootElement.GetProperty("framed");
        var frameless = json.RootElement.GetProperty("frameless");
        Assert.Equal("1px", framed.GetProperty("border").GetString());
        Assert.Equal("rgba(255, 255, 255, 0.3)", framed.GetProperty("borderColor").GetString());
        Assert.Equal("8px", framed.GetProperty("radius").GetString());
        Assert.Equal("none", framed.GetProperty("shadow").GetString());
        Assert.Equal("0px", frameless.GetProperty("border").GetString());
        Assert.Equal("0px", frameless.GetProperty("radius").GetString());
        Assert.Equal("none", frameless.GetProperty("shadow").GetString());
        Assert.Equal(6, frameless.GetProperty("paddingTop").GetDouble(), 2);
        Assert.Equal(6 + framed.GetProperty("row").GetDouble(), framed.GetProperty("paddingTop").GetDouble(), 2);
        foreach (var panel in new[] { framed, frameless })
            Assert.Equal(8 + 2 * panel.GetProperty("cell").GetDouble(), panel.GetProperty("paddingLeft").GetDouble(), 2);
    }

    [Fact]
    public async Task The_panel_takes_its_width_from_the_percent_and_its_height_from_the_points()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));

        var state = await EvalAsync(view, $$"""
            (() => {
                const host = document.getElementById('panes').getBoundingClientRect();
                agterm.applyHud({{Hud(widthPercent: 40, heightPoints: 90)}});
                const points = document.querySelector('.hud-layer').getBoundingClientRect();
                agterm.applyHud({{Hud(widthPercent: 40, heightPoints: null, heightPercent: 25, revision: 2)}});
                const percent = document.querySelector('.hud-layer').getBoundingClientRect();
                return { hostWidth: host.width, hostHeight: host.height, pointsWidth: points.width,
                         pointsHeight: points.height, percentHeight: percent.height };
            })()
            """);

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        AssertNear(root.GetProperty("hostWidth").GetDouble() * 0.4, root.GetProperty("pointsWidth").GetDouble(), "width");
        AssertNear(90, root.GetProperty("pointsHeight").GetDouble(), "height in points");
        AssertNear(root.GetProperty("hostHeight").GetDouble() * 0.25, root.GetProperty("percentHeight").GetDouble(), "height in percent");
    }

    [Fact]
    public async Task The_panel_uses_its_own_colors_or_follows_the_terminal_colors()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));

        var state = await EvalAsync(view, $$"""
            (() => {
                const colors = () => {
                    const s = getComputedStyle(document.querySelector('.hud-layer'));
                    return [s.backgroundColor, s.color];
                };
                agterm.applyHud({{Hud()}});
                const terminal = colors();
                agterm.applyRendererSettings({ backgroundColor: '#102030', foregroundColor: '#a0b0c0' });
                const themed = colors();
                agterm.applyHud({{Hud(background: "#2a1a3a", foreground: "e0e0e0", revision: 2)}});
                const own = colors();
                return { terminal, themed, own };
            })()
            """);

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        Assert.Equal(new[] { "rgb(12, 12, 12)", "rgb(204, 204, 204)" }, root.GetProperty("terminal").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(new[] { "rgb(16, 32, 48)", "rgb(160, 176, 192)" }, root.GetProperty("themed").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(new[] { "rgb(42, 26, 58)", "rgb(224, 224, 224)" }, root.GetProperty("own").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task A_pane_panel_is_drawn_inside_its_pane_and_sized_against_it()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("left"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Split(1, "left", "right", 0.5, "left")));

        var state = await EvalAsync(view, $$"""
            (() => {
                agterm.applyHud({{Hud(paneId: "right", widthPercent: 50)}});
                const layer = document.querySelector('.hud-layer');
                const pane = document.querySelector('[data-pane-id="right"]').getBoundingClientRect();
                const r = layer.getBoundingClientRect();
                return { owner: layer.parentElement.dataset.paneId, ratio: r.width / pane.width,
                         inside: r.left >= pane.left - 0.5 && r.right <= pane.right + 0.5 };
            })()
            """);

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        Assert.Equal("right", root.GetProperty("owner").GetString());
        Assert.Equal(0.5, root.GetProperty("ratio").GetDouble(), 2);
        Assert.True(root.GetProperty("inside").GetBoolean());
    }

    // upstream draws the hud above the scratch, which the port shows as a full-size layer
    [Fact]
    public async Task A_pane_panel_shows_over_the_base_pane_a_full_size_scratch_hides()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("scratch"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Layered(
            1, new TerminalLayout.Single(1, "main", "Shell"), new TerminalLayer("scratch"))));

        var state = await EvalAsync(view, $$"""
            (() => {
                agterm.applyHud({{Hud(paneId: "main")}});
                const layer = document.querySelector('.hud-layer');
                return {
                    base: getComputedStyle(document.querySelector('[data-pane-id="main"]')).visibility,
                    panel: getComputedStyle(layer).visibility,
                    width: layer.getBoundingClientRect().width,
                };
            })()
            """);

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        Assert.Equal("hidden", root.GetProperty("base").GetString());
        Assert.Equal("visible", root.GetProperty("panel").GetString());
        Assert.True(root.GetProperty("width").GetDouble() > 0);
    }

    [Fact]
    public async Task The_panel_stacks_above_a_floating_overlay_and_under_a_question_and_the_search_bar()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("left"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("overlay"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Layered(
            1, new TerminalLayout.Split(1, "left", "right", 0.5, "left"), new TerminalLayer("overlay", 50))));

        var state = await EvalAsync(view, $$"""
            (() => {
                const z = (el) => parseInt(getComputedStyle(el).zIndex, 10);
                agterm.applyHud({{Hud()}});
                const hud = z(document.querySelector('.hud-layer'));
                const overlay = z(document.querySelector('[data-pane-id="overlay"]'));
                agterm.openSearch('right', '', '', false);
                const search = z(document.getElementById('search-bar'));
                agterm.applyAsk({{AskOnRight}});
                const question = z(document.querySelector('.ask-layer'));
                return { hud, overlay, search, question };
            })()
            """);

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        var hud = root.GetProperty("hud").GetInt32();
        Assert.True(root.GetProperty("overlay").GetInt32() < hud, state);
        Assert.True(hud < root.GetProperty("question").GetInt32(), state);
        Assert.True(hud < root.GetProperty("search").GetInt32(), state);
    }

    [Fact]
    public async Task The_panel_takes_no_focus_and_lets_a_click_through_to_the_terminal()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Single(1, "main", "Shell")));
        await _webView.RunAsync<object?>(_ => { view.Focus(); return Task.FromResult<object?>(null); });
        await Task.Delay(500); // focusing the page re-focuses the pane's terminal and reports it
        var paneFocus = new ConcurrentQueue<string>();
        page.PaneFocusChanged += paneFocus.Enqueue;

        var state = await EvalAsync(view, $$"""
            (() => {
                const before = document.activeElement;
                agterm.applyHud({{Hud()}});
                const layer = document.querySelector('.hud-layer');
                const r = layer.getBoundingClientRect();
                const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                const result = {
                    pointerEvents: getComputedStyle(layer).pointerEvents,
                    focusable: layer.querySelectorAll('[tabindex]').length + (layer.hasAttribute('tabindex') ? 1 : 0),
                    hitPane: hit && hit.closest('.pane') ? hit.closest('.pane').dataset.paneId : null,
                    hitPanel: hit !== null && layer.contains(hit),
                    keptOnOpen: document.activeElement === before,
                };
                agterm.applyHud({{Hud(message: "done", revision: 2)}});
                result.keptOnUpdate = document.activeElement === before;
                agterm.applyHud(null);
                result.keptOnClose = document.activeElement === before;
                result.removed = document.querySelector('.hud-layer') === null;
                return result;
            })()
            """);
        await Task.Delay(300);

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        Assert.Equal("none", root.GetProperty("pointerEvents").GetString());
        Assert.Equal(0, root.GetProperty("focusable").GetInt32());
        Assert.Equal("main", root.GetProperty("hitPane").GetString());
        Assert.False(root.GetProperty("hitPanel").GetBoolean());
        Assert.True(root.GetProperty("keptOnOpen").GetBoolean());
        Assert.True(root.GetProperty("keptOnUpdate").GetBoolean());
        Assert.True(root.GetProperty("keptOnClose").GetBoolean());
        Assert.True(root.GetProperty("removed").GetBoolean());
        Assert.Empty(paneFocus);
    }

    [Fact]
    public async Task The_page_reports_metrics_for_the_shown_session_once_per_change()
    {
        var (page, view) = await BootPageAsync();
        var metrics = new ConcurrentQueue<Reported>();
        page.HudMetricsReceived += (paneId, m, single) => metrics.Enqueue(new Reported(paneId, m, single));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Single(1, "main", "Shell")));
        await WaitUntilAsync(() => !metrics.IsEmpty);
        await Task.Delay(300);
        var settled = metrics.Count;
        var first = metrics.Last();
        Assert.Equal("main", first.PaneId);
        Assert.Equal(14, first.Metrics.FontSize);
        Assert.True(first.Metrics.CellWidth > 0 && first.Metrics.CellHeight > 0);
        Assert.Equal<HudRect?>(first.Metrics.Session, first.SinglePane);
        Assert.Null(first.Metrics.Left);
        Assert.Null(first.Metrics.Right);

        await EvalAsync(view, $$"""
            (() => { agterm.applyHud({{Hud()}}); agterm.applyHud({{Hud()}}); agterm.fit('main'); return 1; })()
            """);
        await Task.Delay(300);
        Assert.Equal(settled, metrics.Count);

        await EvalAsync(view, "(() => { document.getElementById('panes').style.width = '600px'; return 1; })()");
        await WaitUntilAsync(() => metrics.Count > settled);
        await Task.Delay(300);
        Assert.Equal(settled + 1, metrics.Count);
        Assert.Equal(600, metrics.Last().Metrics.Session.Width, 3);
    }

    // review focus 2: a long panel in a small window. The window answers every hudMetrics with a
    // redraw of the same revision; the panel keeps the height it was given, clips its rows, and the
    // page stops posting once the numbers settle
    [Fact]
    public async Task A_long_panel_in_a_small_window_clips_its_rows_and_its_metrics_settle()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Single(1, "main", "Shell")));
        var rows = JsonSerializer.Serialize(Enumerable.Range(1, 40)
            .Select(i => new { text = $"row {i} " + new string('x', 70), dim = false }));
        var longHud = $"Object.assign({Hud(widthPercent: 80, heightPoints: 160)}, {{ rows: {rows} }})";
        var metrics = new ConcurrentQueue<Reported>();
        page.HudMetricsReceived += (paneId, m, single) =>
        {
            metrics.Enqueue(new Reported(paneId, m, single));
            // raised on the page's thread, so the redraw goes straight back the way MainWindow sends it
            _ = view.CoreWebView2.ExecuteScriptAsync($"agterm.applyHud({longHud})");
        };

        var state = await EvalAsync(view, $$"""
            (() => {
                const host = document.getElementById('panes');
                host.style.width = '300px';
                host.style.height = '200px';
                agterm.applyHud({{longHud}});
                const layer = document.querySelector('.hud-layer');
                return {
                    height: layer.getBoundingClientRect().height,
                    overflow: getComputedStyle(layer).overflow,
                    clipped: layer.scrollHeight > layer.clientHeight,
                    rows: layer.querySelectorAll('.hud-row').length,
                };
            })()
            """);
        await WaitUntilAsync(() => metrics.Any(m => Math.Abs(m.Metrics.Session.Width - 300) < 0.5));
        await Task.Delay(500);
        var settled = metrics.Count;
        await Task.Delay(500);

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        AssertNear(160, root.GetProperty("height").GetDouble(), "height");
        Assert.Equal("hidden", root.GetProperty("overflow").GetString());
        Assert.True(root.GetProperty("clipped").GetBoolean());
        Assert.Equal(40, root.GetProperty("rows").GetInt32());
        Assert.Equal(settled, metrics.Count);
        Assert.Equal(300, metrics.Last().Metrics.Session.Width, 3);
    }

    [Fact]
    public async Task A_split_reports_both_panes_and_a_zoom_reports_nothing()
    {
        var (page, _) = await BootPageAsync();
        var metrics = new ConcurrentQueue<Reported>();
        page.HudMetricsReceived += (paneId, m, single) => metrics.Enqueue(new Reported(paneId, m, single));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("left"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("right"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Split(1, "left", "right", 0.5, "left")));
        await WaitUntilAsync(() => metrics.Any(m => m.Metrics.Right is not null));
        await Task.Delay(300);

        var reported = metrics.Last();
        Assert.Equal("left", reported.PaneId);
        Assert.Null(reported.SinglePane);
        var split = reported.Metrics;
        if (split.Left is not { } left || split.Right is not { } right)
        {
            Assert.Fail("a split reports both panes");
            return;
        }
        Assert.InRange(left.Width + right.Width + 4, split.Session.Width - 1, split.Session.Width + 1);
        Assert.Equal(split.Session.Height, left.Height, 3);
        Assert.Equal(split.Session.Height, right.Height, 3);

        var settled = metrics.Count;
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Zoomed(2, "right")));
        await Task.Delay(300);
        Assert.Equal(settled, metrics.Count);
    }

    // the font is fixed at open (upstream): the panel draws at the payload's size whatever the pane's
    // font does, and with no panel up the probe follows the renderer font. The renderer font goes
    // apart in every message, because the store gives it to the next HUD with no font of its own.
    [Fact]
    public async Task The_panel_is_drawn_and_measured_at_the_payload_font_size()
    {
        var (page, view) = await BootPageAsync();
        var metrics = new ConcurrentQueue<Reported>();
        page.HudMetricsReceived += (paneId, m, single) => metrics.Enqueue(new Reported(paneId, m, single));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));
        await WaitUntilAsync(() => !metrics.IsEmpty);
        await Task.Delay(300);
        var renderer = metrics.Last().Metrics;
        Assert.Equal(14, renderer.FontSize);
        Assert.Equal(14, renderer.BaseFontSize);

        var rowHeight = await EvalAsync(view, $$"""
            (() => {
                agterm.applyHud({{Hud(fontSize: 28)}});
                agterm.setFontSize('main', 20);
                return document.querySelector('.hud-row').getBoundingClientRect().height;
            })()
            """);
        await WaitUntilAsync(() => metrics.Last().Metrics.FontSize == 28);
        await Task.Delay(300);

        var own = metrics.Last().Metrics;
        Assert.Equal(28, own.FontSize);
        Assert.Equal(14, own.BaseFontSize);
        Assert.InRange(own.CellWidth, 2 * renderer.CellWidth - 0.5, 2 * renderer.CellWidth + 0.5);
        Assert.Equal(own.CellHeight, double.Parse(rowHeight, CultureInfo.InvariantCulture), 2);
        Assert.Equal(own.CellHeight,
            double.Parse(await EvalAsync(view, "(() => document.querySelector('.hud-row').getBoundingClientRect().height)()"), CultureInfo.InvariantCulture), 2);

        await EvalAsync(view, "(() => { agterm.applyHud(null); agterm.applyRendererSettings({ fontSize: 20 }); return 1; })()");
        await WaitUntilAsync(() => metrics.Last().Metrics.FontSize == 20);
        Assert.Equal(20, metrics.Last().Metrics.BaseFontSize);
    }

    [Fact]
    public async Task The_spinner_turns_until_a_new_revision_or_the_close()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));
        var frames = new[] { "a", "b", "c" };
        var spinning = Hud(spinnerFrames: frames, spinnerInterval: 0.05, revision: 1);
        var nextRevision = Hud(spinnerFrames: frames, spinnerInterval: 0.05, revision: 2);
        var staticPanel = Hud(revision: 3);
        var spinningAgain = Hud(spinnerFrames: frames, spinnerInterval: 0.05, revision: 4);

        var state = await EvalAsync(view, $$"""
            (() => {
                agterm.applyHud({{spinning}});
                const timer = hudSpinnerTimer;
                const firstRow = document.querySelector('.hud-row').textContent;
                agterm.applyHud({{spinning}});
                return { firstRow, started: timer !== null, kept: hudSpinnerTimer === timer };
            })()
            """);
        using (var json = JsonDocument.Parse(state))
        {
            Assert.Equal("a building", json.RootElement.GetProperty("firstRow").GetString());
            Assert.True(json.RootElement.GetProperty("started").GetBoolean());
            Assert.True(json.RootElement.GetProperty("kept").GetBoolean());
        }

        await WaitForScriptAsync(view, "document.querySelector('.hud-spinner').textContent !== 'a'");

        var restarted = await EvalAsync(view, $$"""
            (() => {
                const before = hudSpinnerTimer;
                agterm.applyHud({{nextRevision}});
                const glyph = document.querySelector('.hud-spinner').textContent;
                const changed = hudSpinnerTimer !== before && hudSpinnerTimer !== null;
                agterm.applyHud({{staticPanel}});
                const stoppedStatic = hudSpinnerTimer === null && document.querySelector('.hud-spinner') === null;
                agterm.applyHud({{spinningAgain}});
                agterm.applyHud(null);
                return glyph === 'a' && changed && stoppedStatic && hudSpinnerTimer === null
                    && document.querySelector('.hud-layer') === null;
            })()
            """);
        Assert.Equal("true", restarted);
    }

    // the revision is counted per session and one page serves every session of its window, so a
    // matching revision from another session must not keep a timer that writes the wrong frames
    [Fact]
    public async Task An_equal_revision_from_another_panel_does_not_keep_the_old_spinner()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));
        var spinning = Hud(spinnerFrames: new[] { "a", "b", "c" }, spinnerInterval: 0.05);
        var otherFrames = Hud(spinnerFrames: new[] { "x", "y" }, spinnerInterval: 0.05);
        var otherInterval = Hud(spinnerFrames: new[] { "a", "b", "c" }, spinnerInterval: 0.2);
        var otherPane = Hud(paneId: "main", spinnerFrames: new[] { "a", "b", "c" }, spinnerInterval: 0.05);
        var staticPanel = Hud();

        var state = await EvalAsync(view, $$"""
            (() => {
                const glyph = () => {
                    const el = document.querySelector('.hud-spinner');
                    return el ? el.textContent : null;
                };
                const out = {};
                agterm.applyHud({{spinning}});
                let before = hudSpinnerTimer;
                agterm.applyHud({{staticPanel}});
                out.staticStopped = before !== null && hudSpinnerTimer === null && glyph() === null;
                agterm.applyHud({{spinning}});
                out.staticToSpinnerStarted = hudSpinnerTimer !== null && glyph() === 'a';
                before = hudSpinnerTimer;
                agterm.applyHud({{otherFrames}});
                out.framesRestarted = hudSpinnerTimer !== null && hudSpinnerTimer !== before && glyph() === 'x';
                before = hudSpinnerTimer;
                agterm.applyHud({{otherInterval}});
                out.intervalRestarted = hudSpinnerTimer !== null && hudSpinnerTimer !== before && glyph() === 'a';
                before = hudSpinnerTimer;
                agterm.applyHud({{otherPane}});
                out.paneRestarted = hudSpinnerTimer !== null && hudSpinnerTimer !== before && glyph() === 'a';
                agterm.applyHud({{otherFrames}});
                return out;
            })()
            """);
        await Task.Delay(300);
        var glyphs = await EvalAsync(view, "(() => document.querySelector('.hud-spinner').textContent)()");

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        Assert.True(root.GetProperty("staticStopped").GetBoolean(), state);
        Assert.True(root.GetProperty("staticToSpinnerStarted").GetBoolean(), state);
        Assert.True(root.GetProperty("framesRestarted").GetBoolean(), state);
        Assert.True(root.GetProperty("intervalRestarted").GetBoolean(), state);
        Assert.True(root.GetProperty("paneRestarted").GetBoolean(), state);
        Assert.Contains(JsonSerializer.Deserialize<string>(glyphs), new[] { "x", "y" });
    }

    private const string MarkdownPayload = """
        {"paneId":null,"rows":[
          {"text":"see the PR now","dim":false,"runs":[
            {"text":"see ","bold":false,"italic":false,"strike":false,"dim":false,"underline":false},
            {"text":"the PR","bold":false,"italic":false,"strike":false,"dim":false,"underline":true,"link":"https://example.com/pr/7"},
            {"text":" now","bold":false,"italic":false,"strike":false,"dim":false,"underline":false}]},
          {"text":"<b>x</b> done","dim":false,"runs":[
            {"text":"<b>x</b>","bold":true,"italic":false,"strike":false,"dim":false,"underline":false},
            {"text":" done","bold":false,"italic":true,"strike":true,"dim":true,"underline":false}]}],
         "widthPercent":60,"heightPoints":120,"heightPercent":20,"position":"center","sticky":false,"frame":true,"markdown":true,
         "background":null,"foreground":null,"fontSize":null,"spinnerFrames":[],"spinnerInterval":0,"revision":1}
        """;

    // finds the link span's center and presses there the way a mouse would: the panel has
    // pointer-events none, so the event goes to whatever is under it
    private const string PressHelpers = """
        const center = (el) => { const r = el.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; };
        const fire = (type, x, y, init) => {
            const target = document.elementFromPoint(x, y);
            const ev = new PointerEvent(type, Object.assign({ bubbles: true, cancelable: true, composed: true,
                clientX: x, clientY: y, button: 0, buttons: 1, pointerType: 'mouse', isPrimary: true }, init || {}));
            return { target, canceled: !target.dispatchEvent(ev) };
        };
        const click = (x, y, init) => {
            const target = document.elementFromPoint(x, y);
            return !target.dispatchEvent(new MouseEvent('click', Object.assign({ bubbles: true, cancelable: true, clientX: x, clientY: y, button: 0 }, init || {})));
        };
        """;

    private async Task<(XtermPage Page, Microsoft.Web.WebView2.WinForms.WebView2 View)> BootMarkdownHudAsync()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Single(1, "main", "Shell")));
        await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync($"agterm.applyHud({MarkdownPayload})"));
        return (page, view);
    }

    [Fact]
    public async Task Markdown_rows_are_text_only_spans_in_one_left_aligned_block()
    {
        var (_, view) = await BootMarkdownHudAsync();

        var state = await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync("""
            (() => {
                const layer = document.querySelector('.hud-layer');
                const panel = layer; // Task 9's layer is the panel: its border, padding and background
                const block = layer.querySelector('.hud-block');
                const rows = Array.from(block.querySelectorAll('.hud-row'));
                const link = layer.querySelector('.hud-link');
                const bold = rows[1].querySelector('.hud-run-bold');
                const styled = rows[1].querySelector('.hud-run-italic');
                const p = panel.getBoundingClientRect(), b = block.getBoundingClientRect();
                return {
                    parsedHtml: layer.querySelector('b') !== null,
                    rowCount: rows.length,
                    texts: rows.map(r => r.textContent),
                    link: link ? link.textContent : null,
                    target: link ? link.dataset.link : null,
                    boldWeight: parseInt(getComputedStyle(bold).fontWeight, 10),
                    italic: getComputedStyle(styled).fontStyle,
                    strike: getComputedStyle(styled).textDecorationLine,
                    sameLeft: Math.abs(rows[0].getBoundingClientRect().left - rows[1].getBoundingClientRect().left) < 0.5,
                    centered: Math.abs((b.left + b.right) / 2 - (p.left + p.right) / 2) < 2,
                };
            })()
            """));

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        Assert.False(root.GetProperty("parsedHtml").GetBoolean());
        Assert.Equal(2, root.GetProperty("rowCount").GetInt32());
        Assert.Equal(new[] { "see the PR now", "<b>x</b> done" }, root.GetProperty("texts").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("the PR", root.GetProperty("link").GetString());
        Assert.Equal("https://example.com/pr/7", root.GetProperty("target").GetString());
        Assert.True(root.GetProperty("boldWeight").GetInt32() >= 600);
        Assert.Equal("italic", root.GetProperty("italic").GetString());
        Assert.Contains("line-through", root.GetProperty("strike").GetString());
        Assert.True(root.GetProperty("sameLeft").GetBoolean());
        Assert.True(root.GetProperty("centered").GetBoolean());
    }

    [Fact]
    public async Task Ctrl_click_on_a_link_posts_hudLink_and_is_consumed()
    {
        var (page, view) = await BootMarkdownHudAsync();
        var linked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        page.HudLinkRequested += url => linked.TrySetResult(url);

        var state = await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync($$"""
            (() => {
                {{PressHelpers}}
                const { x, y } = center(document.querySelector('.hud-link'));
                const down = fire('pointerdown', x, y, { ctrlKey: true });
                const up = fire('pointerup', x, y, { ctrlKey: true, buttons: 0 });
                return {
                    underHud: document.querySelector('.hud-layer').contains(down.target),
                    down: down.canceled, up: up.canceled, click: click(x, y, { ctrlKey: true }),
                };
            })()
            """));

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        Assert.False(root.GetProperty("underHud").GetBoolean());
        Assert.True(root.GetProperty("down").GetBoolean());
        Assert.True(root.GetProperty("up").GetBoolean());
        Assert.True(root.GetProperty("click").GetBoolean());
        Assert.Equal("https://example.com/pr/7", await linked.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Ctrl_click_off_a_link_inside_the_panel_is_consumed_and_posts_nothing()
    {
        var (page, view) = await BootMarkdownHudAsync();
        var raised = false;
        page.HudLinkRequested += _ => raised = true;

        var canceled = await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync($$"""
            (() => {
                {{PressHelpers}}
                const r = document.querySelector('.hud-layer').getBoundingClientRect();
                return fire('pointerdown', r.left + 3, r.top + 3, { ctrlKey: true }).canceled;
            })()
            """));
        await Task.Delay(300);

        Assert.Equal("true", canceled);
        Assert.False(raised);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ ctrlKey: true, shiftKey: true }")]
    [InlineData("{ ctrlKey: true, altKey: true }")]
    [InlineData("{ ctrlKey: true, button: 2, buttons: 2 }")]
    public async Task Any_other_press_on_the_panel_goes_through(string init)
    {
        var (page, view) = await BootMarkdownHudAsync();
        var raised = false;
        page.HudLinkRequested += _ => raised = true;

        var state = await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync($$"""
            (() => {
                {{PressHelpers}}
                const { x, y } = center(document.querySelector('.hud-link'));
                const target = document.elementFromPoint(x, y);
                let reached = false;
                target.addEventListener('pointerdown', () => { reached = true; }, { once: true });
                const press = fire('pointerdown', x, y, {{init}});
                return { reached, canceled: press.canceled };
            })()
            """));
        await Task.Delay(300);

        using var json = JsonDocument.Parse(state);
        Assert.True(json.RootElement.GetProperty("reached").GetBoolean());
        Assert.False(json.RootElement.GetProperty("canceled").GetBoolean());
        Assert.False(raised);
    }

    [Fact]
    public async Task Ctrl_click_on_a_question_above_the_hud_is_left_to_the_question()
    {
        var (page, view) = await BootMarkdownHudAsync();
        var raised = false;
        page.HudLinkRequested += _ => raised = true;

        var reached = await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync($$"""
            (() => {
                {{PressHelpers}}
                const { x, y } = center(document.querySelector('.hud-link'));
                agterm.applyAsk({"askId":"S","paneId":null,"coveredPaneIds":["main"],"title":"Continue?","message":null,
                    "buttons":[{"runs":[{"text":"OK","underline":false}],"destructive":false}],"highlighted":0,"align":"center","width":null});
                const layer = document.querySelector('.ask-layer');
                let reached = false;
                layer.addEventListener('pointerdown', () => { reached = true; });
                fire('pointerdown', x, y, { ctrlKey: true });
                return reached;
            })()
            """));
        await Task.Delay(300);

        Assert.Equal("true", reached);
        Assert.False(raised);
    }

    [Fact]
    public async Task Ctrl_click_on_the_search_bar_over_the_hud_is_left_to_the_search_bar()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("main"));
        await _webView.RunAsync(_ => page.ApplyLayoutAsync(new TerminalLayout.Single(1, "main", "Shell")));
        var raised = false;
        page.HudLinkRequested += _ => raised = true;

        var reached = await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync($$"""
            (() => {
                {{PressHelpers}}
                const payload = {{MarkdownPayload}};
                payload.position = 'top-right';
                payload.sticky = true;
                payload.widthPercent = 100;
                agterm.applyHud(payload);
                agterm.openSearch('main', '', '', false);
                const bar = document.getElementById('search-bar');
                const { x, y } = center(bar);
                let reached = false;
                bar.addEventListener('pointerdown', () => { reached = true; });
                const inPanel = (() => { const r = document.querySelector('.hud-layer').getBoundingClientRect();
                    return x >= r.left && x < r.right && y >= r.top && y < r.bottom; })();
                fire('pointerdown', x, y, { ctrlKey: true });
                return inPanel && reached;
            })()
            """));
        await Task.Delay(300);

        Assert.Equal("true", reached);
        Assert.False(raised);
    }

    [Fact]
    public async Task The_cursor_is_a_pointer_while_ctrl_is_held_over_a_link()
    {
        var (_, view) = await BootMarkdownHudAsync();

        var state = await _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync($$"""
            (() => {
                {{PressHelpers}}
                const { x, y } = center(document.querySelector('.hud-link'));
                const hover = () => document.documentElement.classList.contains('hud-link-hover');
                fire('pointermove', x, y, { buttons: 0 });
                const plain = hover();
                fire('pointermove', x, y, { ctrlKey: true, buttons: 0 });
                const held = hover();
                document.dispatchEvent(new KeyboardEvent('keyup', { key: 'Control', bubbles: true }));
                const released = hover();
                fire('pointermove', x, y, { ctrlKey: true, buttons: 0 });
                agterm.applyHud(null);
                return { plain, held, released, removed: hover() };
            })()
            """));

        using var json = JsonDocument.Parse(state);
        var root = json.RootElement;
        Assert.False(root.GetProperty("plain").GetBoolean());
        Assert.True(root.GetProperty("held").GetBoolean());
        Assert.False(root.GetProperty("released").GetBoolean());
        Assert.False(root.GetProperty("removed").GetBoolean());
    }
}
