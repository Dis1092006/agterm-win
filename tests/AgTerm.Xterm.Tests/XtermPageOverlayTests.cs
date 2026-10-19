using System.Text;
using System.Text.Json;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class XtermPageOverlayTests
{
    private readonly FakeScriptHost _host = new();

    [Fact]
    public async Task Layered_over_single_serializes_the_exact_bridge_contract()
    {
        var page = await InitializedAsync();
        _host.ScriptResult = _ => "true";

        var accepted = await page.ApplyLayoutAsync(
            new TerminalLayout.Layered(
                5,
                new TerminalLayout.Single(3, "main", "My Shell"),
                new TerminalLayer("overlay")));

        Assert.True(accepted);
        var script = Assert.Single(_host.Scripts, item => item.Contains("agterm.applyLayout"));
        Assert.Contains(
            """{"type":"layered","generation":5,"base":{"type":"single","paneId":"main"},"layer":{"paneId":"overlay"}}""",
            script);
        Assert.DoesNotContain("title", script);
        Assert.DoesNotContain("workspaceName", script);
    }

    [Fact]
    public async Task Layered_over_split_with_size_and_color_serializes_the_exact_bridge_contract()
    {
        var page = await InitializedAsync();
        _host.ScriptResult = _ => "true";

        var accepted = await page.ApplyLayoutAsync(
            new TerminalLayout.Layered(
                7,
                new TerminalLayout.Split(2, "left", "right", 0.6, "left"),
                new TerminalLayer("overlay", sizePercent: 70, backgroundColor: "#1a2b3c")));

        Assert.True(accepted);
        var script = Assert.Single(_host.Scripts, item => item.Contains("agterm.applyLayout"));
        Assert.Contains(
            """{"type":"layered","generation":7,"base":{"type":"split","leftPaneId":"left","rightPaneId":"right","ratio":0.6,"focusedPaneId":"left"},"layer":{"paneId":"overlay","sizePercent":70,"backgroundColor":"#1a2b3c"}}""",
            script);
        Assert.DoesNotContain("title", script);
        Assert.DoesNotContain("workspaceName", script);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void TerminalLayer_rejects_blank_pane_id(string paneId)
    {
        Assert.Throws<ArgumentException>(() => new TerminalLayer(paneId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void TerminalLayer_rejects_size_out_of_range(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalLayer("p", sizePercent: size));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void TerminalLayer_accepts_boundary_size_percent(int size)
    {
        Assert.Equal(size, new TerminalLayer("p", sizePercent: size).SizePercent);
    }

    [Theory]
    [InlineData("#GGGGGG")]
    [InlineData("#12345")]
    [InlineData("red")]
    [InlineData("")]
    public void TerminalLayer_rejects_invalid_color(string color)
    {
        Assert.Throws<ArgumentException>(() => new TerminalLayer("p", backgroundColor: color));
    }

    // Q5.2: Layered must accept Empty as a base so Quick can render over a no-session window.
    [Fact]
    public void Layered_accepts_Empty_base_for_quick_terminal()
    {
        var layered = new TerminalLayout.Layered(1, new TerminalLayout.Empty(1), new TerminalLayer("quick"));
        Assert.IsType<TerminalLayout.Empty>(layered.BaseLayout);
        Assert.Equal("quick", layered.Layer.PaneId);
    }

    // Q5.2: Empty base must serialize to {type:"empty"} so the bridge can render Quick over no session.
    [Fact]
    public async Task Layered_over_empty_serializes_empty_base_contract()
    {
        var page = await InitializedAsync();
        _host.ScriptResult = _ => "true";

        var accepted = await page.ApplyLayoutAsync(
            new TerminalLayout.Layered(
                8,
                new TerminalLayout.Empty(8),
                new TerminalLayer("quick-pane", sizePercent: 90)));

        Assert.True(accepted);
        var script = Assert.Single(_host.Scripts, item => item.Contains("agterm.applyLayout"));
        Assert.Contains(
            """{"type":"layered","generation":8,"base":{"type":"empty"},"layer":{"paneId":"quick-pane","sizePercent":90}}""",
            script);
    }

    [Fact]
    public void Layered_rejects_nested_Layered_base()
    {
        var inner = new TerminalLayout.Layered(
            1,
            new TerminalLayout.Single(1, "main", "s"),
            new TerminalLayer("inner-overlay"));
        Assert.Throws<ArgumentException>(() =>
            new TerminalLayout.Layered(2, inner, new TerminalLayer("outer-overlay")));
    }

    [Fact]
    public void Layered_rejects_duplicate_layer_pane_against_Single()
    {
        Assert.Throws<ArgumentException>(() =>
            new TerminalLayout.Layered(
                1,
                new TerminalLayout.Single(1, "main", "s"),
                new TerminalLayer("main")));
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public void Layered_rejects_duplicate_layer_pane_against_Split_panes(string layerPaneId)
    {
        Assert.Throws<ArgumentException>(() =>
            new TerminalLayout.Layered(
                1,
                new TerminalLayout.Split(1, "left", "right", 0.5, "left"),
                new TerminalLayer(layerPaneId)));
    }

    [Fact]
    public void TerminalLayer_trims_pane_id()
    {
        var layer = new TerminalLayer("  my-pane  ");
        Assert.Equal("my-pane", layer.PaneId);
    }

    [Fact]
    public void TerminalLayer_preserves_color_case()
    {
        var layer = new TerminalLayer("p", backgroundColor: "#AaBbCc");
        Assert.Equal("#AaBbCc", layer.BackgroundColor);
    }

    [Fact]
    public async Task Layout_coordinator_passes_layered_object_unchanged()
    {
        TerminalLayout? applied = null;
        using var coordinator = new TerminalLayoutCoordinator(layout =>
        {
            applied = layout;
            return Task.FromResult(true);
        });

        var layered = new TerminalLayout.Layered(
            9,
            new TerminalLayout.Single(3, "main", "shell"),
            new TerminalLayer("overlay", sizePercent: 50));

        await coordinator.SubmitFromThreadPoolAsync(layered);
        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(layered, applied);
    }

    private async Task<XtermPage> InitializedAsync()
    {
        var page = new XtermPage(_host);
        var initialize = page.InitializeAsync();
        _host.RaiseMessage("""{"type":"ready"}""");
        await initialize;
        return page;
    }
}

/// <summary>Live WebView2 coverage for full and floating terminal layers over single and split
/// bases, including focus, fit, styling, validation, transitions, and buffer preservation.</summary>
[Collection("webview2")]
public sealed class XtermPageOverlayLiveTests
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private readonly WebView2Fixture _webView;

    public XtermPageOverlayLiveTests(WebView2Fixture webView)
    {
        _webView = webView;
    }

    // Q1-axis-1: full cover over Single — both panes registered, layer block/absolute/inset full,
    // base not pointer-active, exactly one click catcher, layer focused, both grids nonzero.
    [Fact]
    public async Task Layered_full_over_single_registers_both_panes_with_overlay_presentation()
    {
        var (page, view) = await BootPageAsync();
        var baseTerm = await _webView.RunAsync(_ => page.OpenTerminalAsync("lf-base"));
        var layerTerm = await _webView.RunAsync(_ => page.OpenTerminalAsync("lf-layer"));

        var marker = "LFULL-" + Guid.NewGuid().ToString("N")[..8];
        await _webView.RunAsync<object?>(_ =>
        {
            baseTerm.FeedOutput(Encoding.UTF8.GetBytes(marker + "\r\n"));
            return Task.FromResult<object?>(null);
        });

        var accepted = await ApplyAsync(page, new TerminalLayout.Layered(
            1,
            new TerminalLayout.Single(1, "lf-base", "shell"),
            new TerminalLayer("lf-layer")));
        Assert.True(accepted);

        var dom = await ReadOverlayDomAsync(view, "lf-base", "lf-layer");
        Assert.True(dom.BaseRegistered, "base pane must remain registered");
        Assert.True(dom.LayerRegistered, "layer pane must remain registered");
        Assert.Equal("block", dom.LayerDisplay);
        Assert.Equal("absolute", dom.LayerPosition);
        Assert.InRange(dom.LayerWidthRatio, 0.95, 1.05);
        Assert.InRange(dom.LayerHeightRatio, 0.95, 1.05);
        Assert.True(dom.LayerZIndex > dom.BaseZIndex,
            $"layer z={dom.LayerZIndex} must be above base z={dom.BaseZIndex}");
        Assert.Equal("none", dom.BasePointerEvents);
        Assert.Equal(1, dom.CatcherCount);
        Assert.Equal("lf-layer", dom.ActivePaneId);
        Assert.True(baseTerm.Cols > 0 && baseTerm.Rows > 0,
            $"base grid: {baseTerm.Cols}x{baseTerm.Rows}");
        Assert.True(layerTerm.Cols > 0 && layerTerm.Rows > 0,
            $"layer grid: {layerTerm.Cols}x{layerTerm.Rows}");
    }

    // Q1-axis-2: floating percent/color over Split — both base panes visible, divider remains,
    // layer centered at 60%, custom background, z-index above catcher, click catcher full width,
    // layer focused.
    [Fact]
    public async Task Layered_floating_over_split_centers_layer_with_size_percent_and_background()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("fp-left"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("fp-right"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("fp-layer"));

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Split(1, "fp-left", "fp-right", 0.5, "fp-left")));

        var accepted = await ApplyAsync(page, new TerminalLayout.Layered(
            2,
            new TerminalLayout.Split(1, "fp-left", "fp-right", 0.5, "fp-left"),
            new TerminalLayer("fp-layer", sizePercent: 60, backgroundColor: "#123456")));
        Assert.True(accepted);

        var dom = await ReadSplitOverlayDomAsync(view, "fp-left", "fp-right", "fp-layer");
        Assert.Equal("block", dom.LeftDisplay);
        Assert.Equal("block", dom.RightDisplay);
        Assert.Equal(1, dom.DividerCount);
        Assert.Equal("absolute", dom.LayerPosition);
        Assert.InRange(dom.LayerWidthRatio, 0.55, 0.65);
        Assert.InRange(dom.LayerHeightRatio, 0.55, 0.65);
        Assert.True(dom.LayerZIndex > 0, $"layer must have positive z-index, got {dom.LayerZIndex}");
        Assert.Equal(1, dom.CatcherCount);
        Assert.InRange(dom.CatcherWidthRatio, 0.95, 1.05);
        Assert.Equal("auto", dom.BasePointerEvents);
        Assert.Equal("fp-layer", dom.ActivePaneId);
        // #123456 = rgb(18, 52, 86)
        var bg = await ExecuteAsync(view,
            """getComputedStyle(document.querySelector('[data-pane-id="fp-layer"]')).backgroundColor""");
        Assert.Equal("\"rgb(18, 52, 86)\"", bg);
        Assert.Equal("\"#123456\"", await ExecuteAsync(view,
            "agterm.getRendererSettings('fp-layer').backgroundColor"));

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Split(3, "fp-left", "fp-right", 0.5, "fp-left")));
        Assert.Equal("\"#0C0C0C\"", await ExecuteAsync(view,
            "agterm.getRendererSettings('fp-layer').backgroundColor"));
    }

    // Q1-axis-3: full -> floating -> full across generations; same layer terminal object/buffer
    // survives all three; only size/style changes, no close/remount.
    [Fact]
    public async Task Higher_generation_transitions_full_floating_full_preserving_layer_buffer()
    {
        var (page, _) = await BootPageAsync();
        var baseTerm = await _webView.RunAsync(_ => page.OpenTerminalAsync("tr-base"));
        var layerTerm = await _webView.RunAsync(_ => page.OpenTerminalAsync("tr-layer"));

        var marker = "TRANS-" + Guid.NewGuid().ToString("N")[..8];
        await _webView.RunAsync<object?>(_ =>
        {
            layerTerm.FeedOutput(Encoding.UTF8.GetBytes(marker + "\r\n"));
            return Task.FromResult<object?>(null);
        });

        Assert.True(await ApplyAsync(page, new TerminalLayout.Layered(
            1,
            new TerminalLayout.Single(1, "tr-base", "shell"),
            new TerminalLayer("tr-layer"))));

        Assert.True(await ApplyAsync(page, new TerminalLayout.Layered(
            2,
            new TerminalLayout.Single(1, "tr-base", "shell"),
            new TerminalLayer("tr-layer", sizePercent: 50))));

        Assert.True(await ApplyAsync(page, new TerminalLayout.Layered(
            3,
            new TerminalLayout.Single(1, "tr-base", "shell"),
            new TerminalLayer("tr-layer"))));

        var text = await _webView.RunAsync(_ => layerTerm.ReadTextAsync(all: true, lines: null));
        Assert.Contains(marker, text);
        _ = baseTerm;
    }

    // Q1-axis-4: applying base layout with higher generation removes overlay; base visibility/
    // pointer/focus restored; layer stays registered but hidden; split divider survives for Split.
    [Fact]
    public async Task Applying_base_layout_removes_overlay_and_restores_base_pointer_and_focus()
    {
        var (page, view) = await BootPageAsync();
        var baseTerm = await _webView.RunAsync(_ => page.OpenTerminalAsync("rm-base"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("rm-layer"));

        var layeredAccepted = await ApplyAsync(page, new TerminalLayout.Layered(
            1,
            new TerminalLayout.Single(1, "rm-base", "shell"),
            new TerminalLayer("rm-layer")));
        Assert.True(layeredAccepted);

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Single(2, "rm-base", "shell")));

        var dom = await ReadOverlayDomAsync(view, "rm-base", "rm-layer");
        Assert.True(dom.BaseRegistered);
        Assert.Equal(0, dom.CatcherCount);
        Assert.NotEqual("none", dom.BasePointerEvents);
        Assert.Equal("rm-base", dom.ActivePaneId);

        // Layer pane stays registered but is hidden.
        Assert.Equal("true",
            await ExecuteAsync(view, """agterm.text("rm-layer", true, null) !== null"""));
        Assert.NotEqual("\"block\"",
            await ExecuteAsync(view,
                """getComputedStyle(document.querySelector('[data-pane-id="rm-layer"]')).display"""));
        Assert.True(baseTerm.Cols > 0 && baseTerm.Rows > 0);
    }

    [Fact]
    public async Task Applying_split_after_layered_restores_ratio_divider_and_both_base_buffers()
    {
        var (page, view) = await BootPageAsync();
        var left = await _webView.RunAsync(_ => page.OpenTerminalAsync("rs-left"));
        var right = await _webView.RunAsync(_ => page.OpenTerminalAsync("rs-right"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("rs-layer"));
        await _webView.RunAsync<object?>(_ =>
        {
            left.FeedOutput(Encoding.UTF8.GetBytes("LEFT-PRESERVED\r\n"));
            right.FeedOutput(Encoding.UTF8.GetBytes("RIGHT-PRESERVED\r\n"));
            return Task.FromResult<object?>(null);
        });

        var split = new TerminalLayout.Split(1, "rs-left", "rs-right", 0.65, "rs-right");
        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Layered(1, split, new TerminalLayer("rs-layer", 55))));
        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Split(2, "rs-left", "rs-right", 0.65, "rs-right")));

        var dom = await ReadSplitOverlayDomAsync(view, "rs-left", "rs-right", "rs-layer");
        Assert.Equal("block", dom.LeftDisplay);
        Assert.Equal("block", dom.RightDisplay);
        Assert.Equal(1, dom.DividerCount);
        Assert.Equal(0, dom.CatcherCount);
        Assert.Equal("rs-right", dom.ActivePaneId);
        Assert.Equal(
            "\"0.65fr 4px 0.35fr\"",
            await ExecuteAsync(view, "document.getElementById('panes').style.gridTemplateColumns"));
        Assert.Contains("LEFT-PRESERVED",
            await _webView.RunAsync(_ => left.ReadTextAsync(all: true, lines: null)));
        Assert.Contains("RIGHT-PRESERVED",
            await _webView.RunAsync(_ => right.ReadTextAsync(all: true, lines: null)));
    }

    // Q1-axis-5: stale generation and invalid nested payloads/unknown pane are rejected before DOM
    // mutation; prior layout state remains unchanged; then valid Layered(gen=4) must be accepted.
    [Fact]
    public async Task Stale_and_invalid_pane_layered_payloads_are_rejected_before_DOM_mutation()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("rj-base"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("rj-layer"));

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Single(2, "rj-base", "shell")));

        // Stale generation (1 < 2) — must be rejected.
        Assert.False(await ApplyAsync(page, new TerminalLayout.Layered(
            1,
            new TerminalLayout.Single(1, "rj-base", "shell"),
            new TerminalLayer("rj-layer"))));

        var invalidPayloads = new[]
        {
            "{type:'layered',generation:3,base:{type:'single',paneId:'rj-unknown'},layer:{paneId:'rj-layer'}}",
            "{type:'layered',generation:3,base:{type:'single',paneId:'rj-base'},layer:{paneId:'rj-unknown'}}",
            "{type:'layered',generation:3,base:{type:'single',paneId:'rj-base'},layer:{paneId:'rj-base'}}",
            "{type:'layered',generation:3,base:{type:'empty',paneId:'rj-base'},layer:{paneId:'rj-layer'}}",
            "{type:'layered',generation:3,base:{type:'layered'},layer:{paneId:'rj-layer'}}",
            "{type:'layered',generation:3,base:{type:'single',paneId:'rj-base'},layer:{paneId:'rj-layer',sizePercent:0}}",
            "{type:'layered',generation:3,base:{type:'single',paneId:'rj-base'},layer:{paneId:'rj-layer',sizePercent:101}}",
            "{type:'layered',generation:3,base:{type:'single',paneId:'rj-base'},layer:{paneId:'rj-layer',sizePercent:50.5}}",
            "{type:'layered',generation:3,base:{type:'single',paneId:'rj-base'},layer:{paneId:'rj-layer',backgroundColor:'red'}}",
            "{type:'layered',generation:3,base:{type:'single',paneId:'rj-base'},layer:{paneId:'rj-layer'},extra:true}",
        };
        foreach (var payload in invalidPayloads)
        {
            Assert.Equal("false", await ExecuteAsync(view, $"agterm.applyLayout({payload})"));
        }

        // DOM must still match gen=2 Single (base visible, no catcher).
        Assert.Equal("\"block\"", await ExecuteAsync(view,
            """getComputedStyle(document.querySelector('[data-pane-id="rj-base"]')).display"""));
        Assert.Equal("0", await ExecuteAsync(view,
            "document.getElementById('panes').querySelectorAll('.overlay-catcher').length"));

        var validAccepted = await ApplyAsync(page, new TerminalLayout.Layered(
            4,
            new TerminalLayout.Single(2, "rj-base", "shell"),
            new TerminalLayer("rj-layer")));
        Assert.True(validAccepted);
    }

    // Q1-axis-6: window resize while layered — layer grid and visible base grids stay positive.
    [Fact]
    public async Task Window_resize_while_layered_refits_layer_and_visible_base_panes()
    {
        var (page, view) = await BootPageAsync();
        var baseTerm = await _webView.RunAsync(_ => page.OpenTerminalAsync("wr-base"));
        var layerTerm = await _webView.RunAsync(_ => page.OpenTerminalAsync("wr-layer"));

        var layerResizes = 0;
        layerTerm.Resized += (_, _) => Interlocked.Increment(ref layerResizes);

        var accepted = await ApplyAsync(page, new TerminalLayout.Layered(
            1,
            new TerminalLayout.Single(1, "wr-base", "shell"),
            new TerminalLayer("wr-layer")));
        Assert.True(accepted);

        await ExecuteAsync(view, "window.dispatchEvent(new Event('resize'))");

        await WaitUntilAsync(
            () => Volatile.Read(ref layerResizes) > 0,
            TimeSpan.FromSeconds(10));

        Assert.True(layerTerm.Cols > 0 && layerTerm.Rows > 0,
            $"layer grid after resize: {layerTerm.Cols}x{layerTerm.Rows}");
        Assert.True(baseTerm.Cols > 0 && baseTerm.Rows > 0,
            $"base grid after resize: {baseTerm.Cols}x{baseTerm.Rows}");
    }

    [Fact]
    public async Task Renderer_settings_refit_all_layered_panes_without_changing_floating_geometry()
    {
        var (page, view) = await BootPageAsync();
        var left = await _webView.RunAsync(_ => page.OpenTerminalAsync("st-left"));
        var right = await _webView.RunAsync(_ => page.OpenTerminalAsync("st-right"));
        var layer = await _webView.RunAsync(_ => page.OpenTerminalAsync("st-layer"));
        Assert.True(await ApplyAsync(page, new TerminalLayout.Layered(
            1,
            new TerminalLayout.Split(1, "st-left", "st-right", 0.4, "st-left"),
            new TerminalLayer("st-layer", 60, "#123456"))));

        var resizeCounts = new int[3];
        left.Resized += (_, _) => Interlocked.Increment(ref resizeCounts[0]);
        right.Resized += (_, _) => Interlocked.Increment(ref resizeCounts[1]);
        layer.Resized += (_, _) => Interlocked.Increment(ref resizeCounts[2]);

        await _webView.RunAsync<object?>(async _ =>
        {
            await page.ApplyRendererSettingsAsync(
                RendererSettingsSnapshot.From(new AppSettings { FontSize = 18 }));
            return null;
        });
        await WaitUntilAsync(
            () => resizeCounts.All(count => Volatile.Read(ref count) > 0),
            TimeSpan.FromSeconds(10));

        var dom = await ReadSplitOverlayDomAsync(view, "st-left", "st-right", "st-layer");
        Assert.InRange(dom.LayerWidthRatio, 0.55, 0.65);
        Assert.InRange(dom.LayerHeightRatio, 0.55, 0.65);
        Assert.Equal(1, dom.DividerCount);
        Assert.Equal("\"#123456\"", await ExecuteAsync(view,
            "agterm.getRendererSettings('st-layer').backgroundColor"));
    }

    // Q1-axis-7: close layer pane while layered removes overlay/catcher without disposing base;
    // base buffer survives; layer pane becomes null in bridge after close.
    [Fact]
    public async Task Closing_layer_pane_removes_overlay_without_disposing_base()
    {
        var (page, view) = await BootPageAsync();
        var baseTerm = await _webView.RunAsync(_ => page.OpenTerminalAsync("cl-base"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("cl-layer"));

        var baseMarker = "CMARK-" + Guid.NewGuid().ToString("N")[..8];
        await _webView.RunAsync<object?>(_ =>
        {
            baseTerm.FeedOutput(Encoding.UTF8.GetBytes(baseMarker + "\r\n"));
            return Task.FromResult<object?>(null);
        });

        var accepted = await ApplyAsync(page, new TerminalLayout.Layered(
            1,
            new TerminalLayout.Single(1, "cl-base", "shell"),
            new TerminalLayer("cl-layer")));
        Assert.True(accepted);

        await _webView.RunAsync<object?>(async _ =>
        {
            await page.ClosePaneAsync("cl-layer");
            return null;
        });

        var baseText = await _webView.RunAsync(_ => baseTerm.ReadTextAsync(all: true, lines: null));
        Assert.Contains(baseMarker, baseText);
        Assert.Equal("0", await ExecuteAsync(view,
            "document.getElementById('panes').querySelectorAll('.overlay-catcher').length"));
        Assert.Equal("null",
            await ExecuteAsync(view, """agterm.text("cl-layer", true, null)"""));
        Assert.Equal("\"none\"", await ExecuteAsync(view,
            "getComputedStyle(document.querySelector('[data-pane-id=\"cl-base\"]')).display"));
    }

    [Fact]
    public async Task Closing_base_pane_while_layered_clears_presentation_without_disposing_layer()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("cb-base"));
        var layer = await _webView.RunAsync(_ => page.OpenTerminalAsync("cb-layer"));
        await _webView.RunAsync<object?>(_ =>
        {
            layer.FeedOutput(Encoding.UTF8.GetBytes("LAYER-PRESERVED\r\n"));
            return Task.FromResult<object?>(null);
        });

        Assert.True(await ApplyAsync(page, new TerminalLayout.Layered(
            1,
            new TerminalLayout.Single(1, "cb-base", "shell"),
            new TerminalLayer("cb-layer"))));
        await _webView.RunAsync<object?>(async _ =>
        {
            await page.ClosePaneAsync("cb-base");
            return null;
        });

        Assert.Equal("0", await ExecuteAsync(view,
            "document.getElementById('panes').querySelectorAll('.overlay-catcher').length"));
        Assert.Equal("null", await ExecuteAsync(view, "agterm.text('cb-base', true, null)"));
        Assert.Equal("\"none\"", await ExecuteAsync(view,
            "getComputedStyle(document.querySelector('[data-pane-id=\"cb-layer\"]')).display"));
        Assert.Contains("LAYER-PRESERVED",
            await _webView.RunAsync(_ => layer.ReadTextAsync(all: true, lines: null)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Layered_live_accepts_boundary_size_percent(int sizePercent)
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("bd-base"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("bd-layer"));

        Assert.True(await ApplyAsync(page, new TerminalLayout.Layered(
            1,
            new TerminalLayout.Single(1, "bd-base", "shell"),
            new TerminalLayer("bd-layer", sizePercent))));

        var dom = await ReadOverlayDomAsync(view, "bd-base", "bd-layer");
        var expectedRatio = sizePercent / 100.0;
        Assert.InRange(dom.LayerWidthRatio, expectedRatio - 0.01, expectedRatio + 0.01);
        Assert.InRange(dom.LayerHeightRatio, expectedRatio - 0.01, expectedRatio + 0.01);
    }

    [Fact]
    public async Task Catcher_pointer_sequence_restores_layer_focus_with_one_report()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("pt-base"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("pt-layer"));

        var accepted = await ApplyAsync(page, new TerminalLayout.Layered(
            1,
            new TerminalLayout.Single(1, "pt-base", "shell"),
            new TerminalLayer("pt-layer")));
        Assert.True(accepted);

        var baseFocused = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        page.PaneFocusChanged += paneId =>
        {
            if (paneId == "pt-base") baseFocused.TrySetResult();
        };
        Assert.Equal("true", await ExecuteAsync(view, "agterm.focus('pt-base')"));
        await baseFocused.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var layerFocused = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var layerFocusReports = 0;
        page.PaneFocusChanged += paneId =>
        {
            if (paneId != "pt-layer") return;
            Interlocked.Increment(ref layerFocusReports);
            layerFocused.TrySetResult();
        };

        await ExecuteAsync(view, """
            (function() {
              const catcher = document.querySelector('.overlay-catcher');
                            if (catcher) {
                                catcher.dispatchEvent(new PointerEvent('pointerdown', {bubbles:true, pointerId:1}));
                                catcher.dispatchEvent(new PointerEvent('pointerup', {bubbles:true, pointerId:1}));
                                catcher.dispatchEvent(new MouseEvent('click', {bubbles:true}));
                            }
              return true;
            })()
            """);
        await layerFocused.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Delay(100);

        var active = await ExecuteAsync(view,
            "document.activeElement && document.activeElement.closest('[data-pane-id]') ? document.activeElement.closest('[data-pane-id]').dataset.paneId : null");
        Assert.Equal("\"pt-layer\"", active);
        Assert.Equal(1, Volatile.Read(ref layerFocusReports));
    }

    // Q5.2: a registered Quick pane renders as a centered 90% layer over an Empty base.
    [Fact]
    public async Task Layered_90pct_quick_over_empty_renders_centered_layer()
    {
        var (page, view) = await BootPageAsync();
        var quickTerm = await _webView.RunAsync(_ => page.OpenTerminalAsync("qk-quick"));

        var accepted = await ApplyAsync(page, new TerminalLayout.Layered(
            1,
            new TerminalLayout.Empty(1),
            new TerminalLayer("qk-quick", sizePercent: 90)));
        Assert.True(accepted);

        var dom = await ReadOverlayDomAsync(view, "qk-quick-nonexistent-base", "qk-quick");
        Assert.True(dom.LayerRegistered, "quick pane must remain registered");
        Assert.Equal("block", dom.LayerDisplay);
        Assert.Equal("absolute", dom.LayerPosition);
        Assert.InRange(dom.LayerWidthRatio, 0.85, 0.95);
        Assert.InRange(dom.LayerHeightRatio, 0.85, 0.95);
        Assert.True(dom.LayerZIndex > 0, $"quick layer must have positive z-index, got {dom.LayerZIndex}");
        Assert.Equal(1, dom.CatcherCount);
        Assert.Equal("qk-quick", dom.ActivePaneId);
        Assert.True(quickTerm.Cols > 0 && quickTerm.Rows > 0,
            $"quick grid: {quickTerm.Cols}x{quickTerm.Rows}");
    }

    // ---- Helpers ----

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

    private Task<bool> ApplyAsync(XtermPage page, TerminalLayout layout) =>
        _webView.RunAsync(_ => page.ApplyLayoutAsync(layout));

    private Task<string> ExecuteAsync(
        Microsoft.Web.WebView2.WinForms.WebView2 view, string script) =>
        _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(script));

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Condition not met within the timeout.");
            await Task.Delay(25);
        }
    }

    private async Task<OverlayDomState> ReadOverlayDomAsync(
        Microsoft.Web.WebView2.WinForms.WebView2 view,
        string basePaneId,
        string layerPaneId)
    {
        var raw = await ExecuteAsync(view, $$"""
            (function() {
              const container = document.getElementById('panes');
              const base = document.querySelector('[data-pane-id="{{basePaneId}}"]');
              const layer = document.querySelector('[data-pane-id="{{layerPaneId}}"]');
              const bs = base ? getComputedStyle(base) : null;
              const ls = layer ? getComputedStyle(layer) : null;
              const cb = container.getBoundingClientRect();
              const lb = layer ? layer.getBoundingClientRect() : {width:0,height:0};
              const catcher = container.querySelector('.overlay-catcher');
              const catcherB = catcher ? catcher.getBoundingClientRect() : {width:0,height:0};
              return {
                baseRegistered: agterm.text("{{basePaneId}}", true, null) !== null,
                layerRegistered: agterm.text("{{layerPaneId}}", true, null) !== null,
                layerDisplay: ls ? ls.display : null,
                layerPosition: ls ? ls.position : null,
                layerWidthRatio: cb.width > 0 ? lb.width / cb.width : 0,
                layerHeightRatio: cb.height > 0 ? lb.height / cb.height : 0,
                layerZIndex: ls ? (parseInt(ls.zIndex) || 0) : 0,
                baseZIndex: bs ? (parseInt(bs.zIndex) || 0) : 0,
                basePointerEvents: bs ? bs.pointerEvents : null,
                catcherCount: container.querySelectorAll('.overlay-catcher').length,
                catcherWidthRatio: cb.width > 0 ? catcherB.width / cb.width : 0,
                activePaneId: document.activeElement && document.activeElement.closest('[data-pane-id]')
                  ? document.activeElement.closest('[data-pane-id]').dataset.paneId : null,
                leftDisplay: null,
                rightDisplay: null,
                dividerCount: 0
              };
            })()
            """);
        return JsonSerializer.Deserialize<OverlayDomState>(raw, JsonOpts)!;
    }

    private async Task<OverlayDomState> ReadSplitOverlayDomAsync(
        Microsoft.Web.WebView2.WinForms.WebView2 view,
        string leftPaneId,
        string rightPaneId,
        string layerPaneId)
    {
        var raw = await ExecuteAsync(view, $$"""
            (function() {
              const container = document.getElementById('panes');
              const left = document.querySelector('[data-pane-id="{{leftPaneId}}"]');
              const right = document.querySelector('[data-pane-id="{{rightPaneId}}"]');
              const layer = document.querySelector('[data-pane-id="{{layerPaneId}}"]');
              const ls = layer ? getComputedStyle(layer) : null;
              const cb = container.getBoundingClientRect();
              const lb = layer ? layer.getBoundingClientRect() : {width:0,height:0};
              const catcher = container.querySelector('.overlay-catcher');
              const catcherB = catcher ? catcher.getBoundingClientRect() : {width:0,height:0};
              return {
                baseRegistered: true,
                layerRegistered: agterm.text("{{layerPaneId}}", true, null) !== null,
                layerDisplay: ls ? ls.display : null,
                layerPosition: ls ? ls.position : null,
                layerWidthRatio: cb.width > 0 ? lb.width / cb.width : 0,
                layerHeightRatio: cb.height > 0 ? lb.height / cb.height : 0,
                layerZIndex: ls ? (parseInt(ls.zIndex) || 0) : 0,
                baseZIndex: 0,
                basePointerEvents: left ? getComputedStyle(left).pointerEvents : null,
                catcherCount: container.querySelectorAll('.overlay-catcher').length,
                catcherWidthRatio: cb.width > 0 ? catcherB.width / cb.width : 0,
                activePaneId: document.activeElement && document.activeElement.closest('[data-pane-id]')
                  ? document.activeElement.closest('[data-pane-id]').dataset.paneId : null,
                leftDisplay: left ? getComputedStyle(left).display : null,
                rightDisplay: right ? getComputedStyle(right).display : null,
                dividerCount: container.querySelectorAll('.split-divider').length
              };
            })()
            """);
        return JsonSerializer.Deserialize<OverlayDomState>(raw, JsonOpts)!;
    }

    private sealed record OverlayDomState(
        bool BaseRegistered,
        bool LayerRegistered,
        string? LayerDisplay,
        string? LayerPosition,
        double LayerWidthRatio,
        double LayerHeightRatio,
        int LayerZIndex,
        int BaseZIndex,
        string? BasePointerEvents,
        int CatcherCount,
        double CatcherWidthRatio,
        string? ActivePaneId,
        string? LeftDisplay,
        string? RightDisplay,
        int DividerCount);
}
