using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.ControlServer;
using System.Text;
using System.Text.Json;

namespace AgTerm.Xterm.Tests;

public sealed class XtermPageSplitTests
{
    private readonly FakeScriptHost _host = new();

    [Fact]
    public async Task Split_layout_serializes_the_exact_bridge_contract()
    {
        var page = await InitializedAsync();
        _host.ScriptResult = _ => "true";

        var accepted = await page.ApplyLayoutAsync(
            new TerminalLayout.Split(5, "left-pane", "right-pane", 0.65, "right-pane"));

        Assert.True(accepted);
        var script = Assert.Single(_host.Scripts, item => item.Contains("agterm.applyLayout"));
        Assert.Contains(
            """{"type":"split","generation":5,"leftPaneId":"left-pane","rightPaneId":"right-pane","ratio":0.65,"focusedPaneId":"right-pane"}""",
            script);
        Assert.DoesNotContain("title", script);
        Assert.DoesNotContain("workspaceName", script);
    }

    [Theory]
    [InlineData(-1, AppStore.SplitRatioMin)]
    [InlineData(0.75, 0.75)]
    [InlineData(2, AppStore.SplitRatioMax)]
    public void Split_layout_clamps_ratio_to_core_bounds(double ratio, double expected)
    {
        var layout = new TerminalLayout.Split(1, "left", "right", ratio, "left");

        Assert.Equal(expected, layout.Ratio);
    }

    [Theory]
    [InlineData("", "right", "right", 0.5)]
    [InlineData("left", "", "left", 0.5)]
    [InlineData("same", "same", "same", 0.5)]
    [InlineData("left", "right", "other", 0.5)]
    [InlineData("left", "right", "left", double.NaN)]
    [InlineData("left", "right", "left", double.PositiveInfinity)]
    public void Invalid_split_layout_is_rejected_at_the_CSharp_boundary(
        string leftPaneId,
        string rightPaneId,
        string focusedPaneId,
        double ratio)
    {
        Assert.Throws<ArgumentException>(() =>
            new TerminalLayout.Split(1, leftPaneId, rightPaneId, ratio, focusedPaneId));
    }

    [Fact]
    public void Split_ratio_cannot_bypass_constructor_clamping_through_an_init_setter()
    {
        var property = typeof(TerminalLayout.Split).GetProperty(nameof(TerminalLayout.Split.Ratio));

        Assert.NotNull(property);
        Assert.Null(property.SetMethod);
    }

    [Fact]
    public async Task Layout_coordinator_preserves_all_split_fields()
    {
        TerminalLayout? applied = null;
        using var coordinator = new TerminalLayoutCoordinator(layout =>
        {
            applied = layout;
            return Task.FromResult(true);
        });
        var split = new TerminalLayout.Split(8, "left", "right", 0.4, "left");

        coordinator.Submit(split);
        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(split, applied);
        Assert.Equal(split, Assert.IsType<TerminalLayout.Split>(applied));
    }

    [Fact]
    public async Task Pane_focus_message_for_a_known_pane_raises_the_page_event()
    {
        var page = await InitializedAsync();
        await page.OpenTerminalAsync("left");
        await page.OpenTerminalAsync("right");
        var focused = new List<string>();
        page.PaneFocusChanged += focused.Add;

        _host.RaiseMessage("""{"type":"paneFocus","paneId":"right"}""");

        Assert.Equal("right", Assert.Single(focused));
    }

    [Fact]
    public async Task Pane_focus_message_with_an_unknown_or_missing_pane_is_dropped()
    {
        var page = await InitializedAsync();
        await page.OpenTerminalAsync("left");
        var focused = new List<string>();
        page.PaneFocusChanged += focused.Add;

        _host.RaiseMessage("""{"type":"paneFocus","paneId":"unknown"}""");
        _host.RaiseMessage("""{"type":"paneFocus"}""");

        Assert.Empty(focused);
    }

    [Theory]
    [InlineData(-1, AppStore.SplitRatioMin)]
    [InlineData(0.6, 0.6)]
    [InlineData(2, AppStore.SplitRatioMax)]
    public async Task Split_resize_message_clamps_and_raises_the_page_event(
        double ratio,
        double expected)
    {
        var page = await InitializedAsync();
        await page.OpenTerminalAsync("left");
        await page.OpenTerminalAsync("right");
        var resized = new List<(string Left, string Right, double Ratio)>();
        page.SplitResized += (left, right, applied) => resized.Add((left, right, applied));

        _host.RaiseMessage(
            $$"""{"type":"splitResize","leftPaneId":"left","rightPaneId":"right","ratio":{{ratio}}}""");

        Assert.Equal(("left", "right", expected), Assert.Single(resized));
    }

    [Fact]
    public async Task Invalid_split_resize_message_is_dropped()
    {
        var page = await InitializedAsync();
        await page.OpenTerminalAsync("left");
        await page.OpenTerminalAsync("right");
        var resized = new List<(string Left, string Right, double Ratio)>();
        page.SplitResized += (left, right, ratio) => resized.Add((left, right, ratio));

        _host.RaiseMessage(
            """{"type":"splitResize","leftPaneId":"left","rightPaneId":"unknown","ratio":0.5}""");
        _host.RaiseMessage(
            """{"type":"splitResize","leftPaneId":"left","rightPaneId":"right"}""");
        _host.RaiseMessage(
            """{"type":"splitResize","leftPaneId":"left","rightPaneId":"left","ratio":0.5}""");

        Assert.Empty(resized);
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

public sealed class LiveSplitLayoutPersistenceTests : IDisposable
{
    private readonly string _stateDir = Directory.CreateTempSubdirectory("agterm-p2-").FullName;

    [Fact]
    public async Task Split_ratio_persists_only_for_the_exact_main_and_split_pane_pair()
    {
        using var host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-p2-" + Guid.NewGuid().ToString("N"),
            OpenSurface = (request, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface
            {
                PaneToken = request.Kind switch
                {
                    LiveSurfaceKind.Main => $"main-{request.SessionId:N}",
                    LiveSurfaceKind.Split => $"split-{request.SessionId:N}",
                    _ => $"{request.Kind}-{request.SessionId:N}",
                },
            }),
        });
        host.Start();
        var client = new ControlPipeClient(host.PipeName);
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);
        var mainPaneId = $"main-{sessionId:N}";
        var splitPaneId = $"split-{sessionId:N}";

        var split = client.Send(new ControlRequest
        {
            Cmd = Command.SessionSplit,
            Args = new ControlArgs { Mode = "on" },
        });
        Assert.True(split.Ok, split.Error);

        Assert.False(await host.ApplySplitRatioAsync(splitPaneId, mainPaneId, 0.25));
        Assert.Null(await ReadRatioAsync(host, sessionId));

        Assert.False(await host.ApplySplitRatioAsync(mainPaneId, "unknown", 0.25));
        Assert.Null(await ReadRatioAsync(host, sessionId));

        Assert.True(await host.ApplySplitRatioAsync(mainPaneId, splitPaneId, 2));
        Assert.Equal(AppStore.SplitRatioMax, await ReadRatioAsync(host, sessionId));
    }

    public void Dispose()
    {
        try { Directory.Delete(_stateDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static Task<double?> ReadRatioAsync(LiveControlHost host, Guid sessionId) =>
        host.StoreCoordinator.ReadAsync(store => store.SessionWithId(sessionId)!.SplitRatio);
}

[Collection("webview2")]
public sealed class XtermPageSplitLiveTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly WebView2Fixture _webView;

    public XtermPageSplitLiveTests(WebView2Fixture webView)
    {
        _webView = webView;
    }

    [Fact]
    public async Task Split_shows_two_existing_panes_and_hides_every_other_pane()
    {
        var (page, view) = await BootPageAsync();
        await OpenThreePanesAsync(page);

        var accepted = await ApplyAsync(page,
            new TerminalLayout.Split(1, "split-left", "split-right", 0.6, "split-left"));

        Assert.True(accepted);
        var state = await ReadLayoutStateAsync(view);
        Assert.Equal("grid", state.ContainerDisplay);
        Assert.Equal("block", state.LeftDisplay);
        Assert.Equal("block", state.RightDisplay);
        Assert.Equal("none", state.OtherDisplay);
        Assert.Equal(1, state.DividerCount);
        Assert.True(state.LeftWidth > state.RightWidth, $"left={state.LeftWidth}, right={state.RightWidth}");
    }

    [Fact]
    public async Task Split_fits_and_reports_both_panes()
    {
        var (page, view) = await BootPageAsync();
        var left = await _webView.RunAsync(_ => page.OpenTerminalAsync("fit-left"));
        var right = await _webView.RunAsync(_ => page.OpenTerminalAsync("fit-right"));
        var leftResize = new TaskCompletionSource<(int Cols, int Rows)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rightResize = new TaskCompletionSource<(int Cols, int Rows)>(TaskCreationOptions.RunContinuationsAsynchronously);
        left.Resized += (cols, rows) => leftResize.TrySetResult((cols, rows));
        right.Resized += (cols, rows) => rightResize.TrySetResult((cols, rows));

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Split(1, "fit-left", "fit-right", 0.5, "fit-left")));

        var leftGrid = await leftResize.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var rightGrid = await rightResize.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(leftGrid.Cols > 0 && leftGrid.Rows > 0, $"left grid: {leftGrid}");
        Assert.True(rightGrid.Cols > 0 && rightGrid.Rows > 0, $"right grid: {rightGrid}");
        var state = await ReadLayoutStateAsync(view, "fit-left", "fit-right", "fit-right");
        Assert.True(state.LeftWidth > 0 && state.RightWidth > 0);
        Assert.InRange(Math.Abs(state.LeftWidth - state.RightWidth), 0, 4);
    }

    [Fact]
    public async Task Split_preserves_independent_existing_terminal_buffers()
    {
        var (page, _) = await BootPageAsync();
        var left = await _webView.RunAsync(_ => page.OpenTerminalAsync("buffer-left"));
        var right = await _webView.RunAsync(_ => page.OpenTerminalAsync("buffer-right"));

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Split(1, "buffer-left", "buffer-right", 0.5, "buffer-left")));
        await _webView.RunAsync<object?>(_ =>
        {
            left.FeedOutput(Encoding.UTF8.GetBytes("left-only\r\n"));
            right.FeedOutput(Encoding.UTF8.GetBytes("right-only\r\n"));
            return Task.FromResult<object?>(null);
        });

        var leftText = await WaitForTextAsync(left, "left-only");
        var rightText = await WaitForTextAsync(right, "right-only");
        Assert.Contains("left-only", leftText);
        Assert.DoesNotContain("right-only", leftText);
        Assert.Contains("right-only", rightText);
        Assert.DoesNotContain("left-only", rightText);
    }

    [Fact]
    public async Task Split_focuses_the_requested_pane_and_reports_later_pane_focus()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("focus-left"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("focus-right"));
        var focusMessages = new List<string>();
        var rightFocused = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        page.PaneFocusChanged += paneId =>
        {
            focusMessages.Add(paneId);
            if (paneId == "focus-right") rightFocused.TrySetResult(paneId);
        };

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Split(1, "focus-left", "focus-right", 0.5, "focus-right")));

        Assert.Equal("focus-right", await rightFocused.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("\"focus-right\"", await ActivePaneIdAsync(view));
        await Task.Delay(100);
        Assert.Equal(new[] { "focus-right" }, focusMessages);

        var leftFocused = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        page.PaneFocusChanged += paneId =>
        {
            if (paneId == "focus-left") leftFocused.TrySetResult(paneId);
        };
        await ExecuteAsync(view, "agterm.focus('focus-left')");

        Assert.Equal("focus-left", await leftFocused.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("\"focus-left\"", await ActivePaneIdAsync(view));
        await Task.Delay(100);
        Assert.Equal(new[] { "focus-right", "focus-left" }, focusMessages);
    }

    [Fact]
    public async Task Stale_layout_is_rejected_and_split_to_single_retains_the_hidden_terminal()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("generation-left"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("generation-right"));

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Split(2, "generation-left", "generation-right", 0.5, "generation-right")));
        Assert.False(await ApplyAsync(page,
            new TerminalLayout.Single(1, "generation-left", "stale")));

        var splitState = await ReadLayoutStateAsync(
            view, "generation-left", "generation-right", "generation-right");
        Assert.Equal("block", splitState.LeftDisplay);
        Assert.Equal("block", splitState.RightDisplay);
        Assert.Equal(1, splitState.DividerCount);

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Single(3, "generation-left", "single")));
        var singleState = await ReadLayoutStateAsync(
            view, "generation-left", "generation-right", "generation-right");
        Assert.Equal("block", singleState.LeftDisplay);
        Assert.Equal("none", singleState.RightDisplay);
        Assert.Equal(0, singleState.DividerCount);

        var retained = await ExecuteAsync(view, "agterm.text('generation-right', true, null) !== null");
        Assert.Equal("true", retained);
    }

    [Fact]
    public async Task Divider_reports_one_clamped_ratio_only_when_drag_ends()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("drag-left"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("drag-right"));
        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Split(1, "drag-left", "drag-right", 0.5, "drag-left")));
        var resized = new List<(string Left, string Right, double Ratio)>();
        page.SplitResized += (left, right, ratio) => resized.Add((left, right, ratio));

        await ExecuteAsync(view,
            """
            (function() {
              const divider = document.querySelector('.split-divider');
              const bounds = document.getElementById('panes').getBoundingClientRect();
              const start = divider.getBoundingClientRect();
              divider.dispatchEvent(new PointerEvent('pointerdown', {
                bubbles: true, pointerId: 7, clientX: start.left + start.width / 2, buttons: 1
              }));
              window.dispatchEvent(new PointerEvent('pointermove', {
                bubbles: true, pointerId: 7, clientX: bounds.left + bounds.width * 0.7, buttons: 1
              }));
              return true;
            })()
            """);
        await Task.Delay(200);
        Assert.Empty(resized);

        await ExecuteAsync(view,
            """
            (function() {
              const bounds = document.getElementById('panes').getBoundingClientRect();
              window.dispatchEvent(new PointerEvent('pointerup', {
                bubbles: true, pointerId: 7, clientX: bounds.left + bounds.width * 0.7
              }));
              return true;
            })()
            """);

        await WaitUntilAsync(() => resized.Count == 1, TimeSpan.FromSeconds(10));
        var result = Assert.Single(resized);
        Assert.Equal("drag-left", result.Left);
        Assert.Equal("drag-right", result.Right);
        Assert.InRange(result.Ratio, 0.68, 0.72);
    }

    [Fact]
    public async Task Window_resize_refits_both_split_panes()
    {
        var (page, view) = await BootPageAsync();
        var left = await _webView.RunAsync(_ => page.OpenTerminalAsync("resize-left"));
        var right = await _webView.RunAsync(_ => page.OpenTerminalAsync("resize-right"));
        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Split(1, "resize-left", "resize-right", 0.5, "resize-left")));
        var leftCount = 0;
        var rightCount = 0;
        left.Resized += (_, _) => Interlocked.Increment(ref leftCount);
        right.Resized += (_, _) => Interlocked.Increment(ref rightCount);

        await ExecuteAsync(view, "window.dispatchEvent(new Event('resize'))");

        await WaitUntilAsync(
            () => Volatile.Read(ref leftCount) > 0 && Volatile.Read(ref rightCount) > 0,
            TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Invalid_split_does_not_advance_generation_or_change_the_visible_layout()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("invalid-left"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("invalid-right"));
        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Split(1, "invalid-left", "invalid-right", 0.5, "invalid-left")));

        var invalid = await ExecuteAsync(view,
            "agterm.applyLayout({type:'split',generation:5,leftPaneId:'invalid-left',rightPaneId:'invalid-left',ratio:0.5,focusedPaneId:'invalid-left'})");
        Assert.Equal("false", invalid);
        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Single(2, "invalid-right", "valid-after-invalid")));

        var state = await ReadLayoutStateAsync(view, "invalid-right", "invalid-left", "invalid-left");
        Assert.Equal("block", state.LeftDisplay);
        Assert.Equal("none", state.RightDisplay);
        Assert.Equal(0, state.DividerCount);
    }

    [Fact]
    public async Task Split_to_single_cancels_a_pending_fit_for_the_hidden_pane()
    {
                var (page, view) = await BootPageAsync();
                var left = await _webView.RunAsync(_ => page.OpenTerminalAsync("raf-left"));
                var right = await _webView.RunAsync(_ => page.OpenTerminalAsync("raf-right"));
                Assert.True(await ApplyAsync(page,
                        new TerminalLayout.Split(1, "raf-left", "raf-right", 0.5, "raf-left")));
                var leftResizes = 0;
                var rightResizes = 0;
                left.Resized += (_, _) => Interlocked.Increment(ref leftResizes);
                right.Resized += (_, _) => Interlocked.Increment(ref rightResizes);

                await ExecuteAsync(view,
                        """
                        (async function() {
                            const divider = document.querySelector('.split-divider');
                            const bounds = document.getElementById('panes').getBoundingClientRect();
                            divider.dispatchEvent(new PointerEvent('pointerdown', {
                                bubbles: true, pointerId: 11, clientX: bounds.left + bounds.width * 0.5, buttons: 1
                            }));
                            window.dispatchEvent(new PointerEvent('pointermove', {
                                bubbles: true, pointerId: 11, clientX: bounds.left + bounds.width * 0.7, buttons: 1
                            }));
                            agterm.applyLayout({
                                type: 'single', generation: 2, paneId: 'raf-left', title: 'single'
                            });
                            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                            return true;
                        })()
                        """);

                Assert.Equal(1, Volatile.Read(ref leftResizes));
                Assert.Equal(0, Volatile.Read(ref rightResizes));
    }

    [Fact]
    public async Task Closing_a_visible_split_pane_clears_layout_and_keeps_the_survivor_addressable()
    {
                var (page, view) = await BootPageAsync();
                await _webView.RunAsync(_ => page.OpenTerminalAsync("close-left"));
                var right = await _webView.RunAsync(_ => page.OpenTerminalAsync("close-right"));
                Assert.True(await ApplyAsync(page,
                        new TerminalLayout.Split(1, "close-left", "close-right", 0.5, "close-left")));

                await _webView.RunAsync<object?>(async _ =>
                {
                        await page.ClosePaneAsync("close-left");
                        right.FeedOutput(Encoding.UTF8.GetBytes("survivor-still-live\r\n"));
                        return null;
                });

                var state = await ReadLayoutStateAsync(view, "close-left", "close-right", "close-right");
                Assert.Null(state.LeftDisplay);
                Assert.Equal("none", state.RightDisplay);
                Assert.Equal(0, state.DividerCount);
                var text = await _webView.RunAsync(_ => right.ReadTextAsync(all: true, lines: null));
                Assert.Contains("survivor-still-live", text);
    }

    private async Task<(XtermPage Page, Microsoft.Web.WebView2.WinForms.WebView2 View)> BootPageAsync()
    {
        var view = await _webView.CreateWebViewAsync();
        var page = await _webView.RunAsync(async _ =>
        {
            var created = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await created.InitializeAsync();
            return created;
        });
        return (page, view);
    }

    private async Task OpenThreePanesAsync(XtermPage page)
    {
        await _webView.RunAsync(_ => page.OpenTerminalAsync("split-left"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("split-right"));
        await _webView.RunAsync(_ => page.OpenTerminalAsync("split-other"));
    }

    private Task<bool> ApplyAsync(XtermPage page, TerminalLayout layout) =>
        _webView.RunAsync(_ => page.ApplyLayoutAsync(layout));

    private Task<string> ExecuteAsync(
        Microsoft.Web.WebView2.WinForms.WebView2 view,
        string script) =>
        _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(script));

    private Task<string> ActivePaneIdAsync(Microsoft.Web.WebView2.WinForms.WebView2 view) =>
        ExecuteAsync(
            view,
            "document.activeElement && document.activeElement.closest('.pane') ? document.activeElement.closest('.pane').dataset.paneId : null");

    private async Task<LayoutState> ReadLayoutStateAsync(
        Microsoft.Web.WebView2.WinForms.WebView2 view,
        string leftPaneId = "split-left",
        string rightPaneId = "split-right",
        string otherPaneId = "split-other")
    {
        var raw = await ExecuteAsync(
            view,
            $$"""
            (function() {
              const container = document.getElementById('panes');
              const left = document.querySelector('[data-pane-id="{{leftPaneId}}"]');
              const right = document.querySelector('[data-pane-id="{{rightPaneId}}"]');
              const other = document.querySelector('[data-pane-id="{{otherPaneId}}"]');
              return {
                containerDisplay: getComputedStyle(container).display,
                leftDisplay: left ? getComputedStyle(left).display : null,
                rightDisplay: right ? getComputedStyle(right).display : null,
                otherDisplay: other ? getComputedStyle(other).display : null,
                leftWidth: left ? left.getBoundingClientRect().width : 0,
                rightWidth: right ? right.getBoundingClientRect().width : 0,
                dividerCount: container.querySelectorAll('.split-divider').length
              };
            })()
            """);
        return JsonSerializer.Deserialize<LayoutState>(raw, JsonOptions)!;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not met within the test timeout.");
            }

            await Task.Delay(25);
        }
    }

    private async Task<string> WaitForTextAsync(XtermTerminal terminal, string expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var text = string.Empty;
        while (DateTime.UtcNow < deadline)
        {
            text = await _webView.RunAsync(_ => terminal.ReadTextAsync(all: true, lines: null));
            if (text.Contains(expected, StringComparison.Ordinal))
            {
                return text;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Pane text did not contain '{expected}'. Last buffer: {text}");
    }

    private sealed record LayoutState(
        string? ContainerDisplay,
        string? LeftDisplay,
        string? RightDisplay,
        string? OtherDisplay,
        double LeftWidth,
        double RightWidth,
        int DividerCount);
}
