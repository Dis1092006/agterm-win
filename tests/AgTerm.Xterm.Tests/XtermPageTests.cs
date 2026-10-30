using System.Collections.Concurrent;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// The page half of bridge v2: one XtermPage per CoreWebView2 boots terminal.html once, opens N
/// terminals keyed by pane id, and demultiplexes every page→host message to the right terminal.
/// Unit-level over FakeScriptHost; the real-JS twin is XtermPageLiveTests.
/// </summary>
public class XtermPageTests
{
    private readonly FakeScriptHost _host = new();

    private async Task<XtermPage> InitializedAsync()
    {
        var page = new XtermPage(_host);
        var init = page.InitializeAsync();
        _host.RaiseMessage("""{"type":"ready"}""");
        await init;
        return page;
    }

    [Fact]
    public async Task Inactive_wheel_passes_view_relative_coordinates_and_native_delta_to_the_page()
    {
        var page = await InitializedAsync();
        await page.ScrollInactiveAtAsync(0.25, 0.75, 120);
        Assert.Equal("agterm.scrollInactiveAt(...[0.25,0.75,120])", Assert.Single(_host.Scripts));
    }

    [Fact]
    public async Task Initialize_maps_assets_navigates_and_awaits_the_page_ready()
    {
        var page = new XtermPage(_host);
        var init = page.InitializeAsync();
        Assert.False(init.IsCompleted);
        Assert.Equal(("agterm.assets", XtermAssets.Directory), Assert.Single(_host.Mapped));
        Assert.Equal("https://agterm.assets/terminal.html", Assert.Single(_host.Navigated));

        _host.RaiseMessage("""{"type":"ready"}""");
        await init;
    }

    [Fact]
    public async Task OpenTerminal_runs_openPane_and_adopts_the_returned_grid()
    {
        var page = await InitializedAsync();
        _host.ScriptResult = s => s.Contains("agterm.openPane") ? """{"cols":120,"rows":30}""" : "null";
        var terminal = await page.OpenTerminalAsync("p1");
        Assert.Contains("agterm.openPane(\"p1\"", _host.Scripts[^1]);
        Assert.Equal(120, terminal.Cols);
        Assert.Equal(30, terminal.Rows);
    }

    [Fact]
    public async Task Concurrent_open_completions_update_terminals_only_on_the_ui_context()
    {
        var page = await InitializedAsync();
        const int paneCount = 13;
        var pending = new Queue<TaskCompletionSource<string>>(
            Enumerable.Range(0, paneCount).Select(_ =>
                new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)));
        var completions = pending.ToArray();
        _host.ScriptTask = _ => pending.Dequeue().Task;

        var context = new QueuedSynchronizationContext();
        var previous = SynchronizationContext.Current;
        Task<XtermTerminal>[] opens;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            opens = Enumerable.Range(0, paneCount)
                .Select(i => page.OpenTerminalAsync($"pane-{i}")).ToArray();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        await Task.Run(() => Parallel.ForEach(completions,
            completion => completion.SetResult("""{"cols":80,"rows":24}""")));

        Assert.All(opens, open => Assert.False(open.IsCompleted));
        Assert.Equal(paneCount, context.PendingCount);
        context.RunPending();
        var terminals = await Task.WhenAll(opens);
        for (var i = 0; i < paneCount; i++)
        {
            var received = new List<string>();
            terminals[i].DataReceived += received.Add;
            _host.RaiseMessage($$"""{"type":"data","paneId":"pane-{{i}}","data":"{{i}}"}""");
            Assert.Equal(i.ToString(), Assert.Single(received));
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _pending = new();

        public int PendingCount => _pending.Count;

        public override void Post(SendOrPostCallback callback, object? state) =>
            _pending.Enqueue((callback, state));

        public void RunPending()
        {
            var previous = Current;
            try
            {
                SetSynchronizationContext(this);
                while (_pending.TryDequeue(out var work))
                    work.Callback(work.State);
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }
    }

    [Fact]
    public async Task OpenTerminal_passes_the_font_size_into_openPane()
    {
        var page = await InitializedAsync();
        await page.OpenTerminalAsync("p1", fontSize: 16);
        Assert.Contains("agterm.openPane(\"p1\", {\"fontSize\":16})", _host.Scripts[^1]);
    }

    [Fact]
    public async Task OpenTerminal_asks_openPane_to_listen_for_the_readline_marker_only_when_told()
    {
        var page = await InitializedAsync();

        await page.OpenTerminalAsync("p1", fontSize: 16, readlineMarker: true);
        Assert.Contains("agterm.openPane(\"p1\", {\"fontSize\":16,\"readlineMarker\":true})", _host.Scripts[^1]);

        await page.OpenTerminalAsync("p2", readlineMarker: true);
        Assert.Contains("agterm.openPane(\"p2\", {\"readlineMarker\":true})", _host.Scripts[^1]);

        await page.OpenTerminalAsync("p3");
        Assert.Contains("agterm.openPane(\"p3\", {})", _host.Scripts[^1]);
    }

    [Fact]
    public async Task Search_presentation_scripts_preserve_owner_state_and_escape_text()
    {
        var page = await InitializedAsync();

        await page.ApplySearchAsync("pane-1", "say \"hi\"", "2 of 5", selected: 2, focusInput: true);
        Assert.Contains("agterm.applySearch", _host.Scripts[^1]);
        Assert.Contains("\"paneId\":\"pane-1\"", _host.Scripts[^1]);
        Assert.Contains("\"needle\":\"say \\\"hi\\\"\"", _host.Scripts[^1]);
        Assert.Contains("\"displayText\":\"2 of 5\"", _host.Scripts[^1]);
        Assert.Contains("\"selected\":2", _host.Scripts[^1]);
        Assert.Contains("\"focusInput\":true", _host.Scripts[^1]);

        await page.HideSearchAsync("pane-1", "pane-2");
        Assert.Contains("agterm.hideSearch", _host.Scripts[^1]);
        Assert.Contains("\"clearOwnerPaneId\":\"pane-1\"", _host.Scripts[^1]);
        Assert.Contains("\"restorePaneId\":\"pane-2\"", _host.Scripts[^1]);
    }

    [Fact]
    public async Task OSC_notification_and_actual_focus_messages_route_only_to_known_panes()
    {
        var page = await InitializedAsync();
        var one = await page.OpenTerminalAsync("p1");
        var two = await page.OpenTerminalAsync("p2");
        var notifications = new List<TerminalOscReport>();
        var focuses = 0;
        one.TerminalNotificationReceived += _ => Assert.Fail("wrong notification pane");
        two.TerminalNotificationReceived += notifications.Add;
        two.TerminalFocused += () => focuses++;
        _host.RaiseMessage("""{"type":"terminalNotification","paneId":"p2","identifier":777,"data":"notify;t;b","focused":true}""");
        _host.RaiseMessage("""{"type":"terminalNotification","paneId":"gone","identifier":9,"data":"stale","focused":false}""");
        _host.RaiseMessage("""{"type":"terminalFocused","paneId":"p2"}""");
        Assert.Equal(new TerminalOscReport(777, "notify;t;b", true), Assert.Single(notifications));
        Assert.Equal(1, focuses);
    }

    [Fact]
    public async Task Messages_route_to_the_terminal_with_the_matching_pane_id()
    {
        var page = await InitializedAsync();
        var one = await page.OpenTerminalAsync("p1");
        var two = await page.OpenTerminalAsync("p2");

        var oneData = new List<string>();
        var twoData = new List<string>();
        one.DataReceived += oneData.Add;
        two.DataReceived += twoData.Add;

        _host.RaiseMessage("""{"type":"data","paneId":"p2","data":"ls\r"}""");
        Assert.Empty(oneData);
        Assert.Equal("ls\r", Assert.Single(twoData));

        var oneTitles = new List<string>();
        one.TitleChanged += oneTitles.Add;
        _host.RaiseMessage("""{"type":"title","paneId":"p1","title":"T1"}""");
        _host.RaiseMessage("""{"type":"title","paneId":"p2","title":"T2"}""");
        Assert.Equal("T1", Assert.Single(oneTitles));

        var twoSizes = new List<(int, int)>();
        two.Resized += (c, r) => twoSizes.Add((c, r));
        _host.RaiseMessage("""{"type":"resize","paneId":"p2","cols":132,"rows":43}""");
        Assert.Equal((132, 43), Assert.Single(twoSizes));
        Assert.Equal(132, two.Cols);
        Assert.Equal(80, one.Cols); // untouched

        var oneCwds = new List<string>();
        one.CwdChanged += oneCwds.Add;
        _host.RaiseMessage("""{"type":"cwd","paneId":"p1","uri":"file:///C:/git/proj"}""");
        Assert.Equal("C:\\git\\proj", Assert.Single(oneCwds));
    }

    [Fact]
    public async Task Messages_for_unknown_or_missing_pane_ids_are_dropped()
    {
        var page = await InitializedAsync();
        var one = await page.OpenTerminalAsync("p1");
        var data = new List<string>();
        one.DataReceived += data.Add;
        _host.RaiseMessage("""{"type":"data","paneId":"ghost","data":"x"}""");
        _host.RaiseMessage("""{"type":"data","data":"y"}""");
        Assert.Empty(data);
    }

    [Fact]
    public async Task Keystrokes_route_only_from_open_panes_and_classify_interrupts()
    {
        var page = await InitializedAsync();
        var terminal = await page.OpenTerminalAsync("p1");
        var keys = new List<bool>();
        terminal.UserKeystroke += keys.Add;

        _host.RaiseMessage("""{"type":"userKeystroke","paneId":"missing","code":"Escape","key":"Escape"}""");
        _host.RaiseMessage("""{"type":"userKeystroke","paneId":"p1","code":"KeyA","key":"a"}""");
        _host.RaiseMessage("""{"type":"userKeystroke","paneId":"p1","code":"KeyC","key":"\u0441","ctrl":true}""");
        _host.RaiseMessage("""{"type":"userKeystroke","paneId":"p1","code":"KeyC","key":"C","ctrl":true,"shift":true}""");
        _host.RaiseMessage("""{"type":"userKeystroke","paneId":"p1","code":"Escape","key":"Escape"}""");
        await page.ClosePaneAsync("p1");
        _host.RaiseMessage("""{"type":"userKeystroke","paneId":"p1","code":"Escape","key":"Escape"}""");

        Assert.Equal([false, true, false, true], keys);
    }

    [Fact]
    public async Task ShowPane_runs_the_show_script()
    {
        var page = await InitializedAsync();
        await page.OpenTerminalAsync("p1");
        await page.ShowPaneAsync("p1");
        Assert.Contains("agterm.showPane(\"p1\")", _host.Scripts[^1]);
    }

    [Fact]
    public async Task ApplyLayout_Single_posts_discriminated_camelCase_json_and_returns_bridge_result()
    {
        var page = await InitializedAsync();
        _host.ScriptResult = _ => "true";

        var accepted = await page.ApplyLayoutAsync(new TerminalLayout.Single(7L, "p1", "shell"));

        Assert.True(accepted);
        var applyScript = Assert.Single(_host.Scripts, s => s.Contains("agterm.applyLayout"));
        Assert.Contains("""{"type":"single","generation":7,"paneId":"p1","title":"shell"}""", applyScript);
    }

    [Fact]
    public async Task WorkspaceName_stays_out_of_bridge_json()
    {
        var page = await InitializedAsync();
        _host.ScriptResult = _ => "true";

        var accepted = await page.ApplyLayoutAsync(
            new TerminalLayout.Single(8L, "p1", "session", "workspace-private"));

        Assert.True(accepted);
        var applyScript = Assert.Single(_host.Scripts, s => s.Contains("agterm.applyLayout"));
        Assert.Contains("\"type\":\"single\"", applyScript);
        Assert.Contains("\"generation\":8", applyScript);
        Assert.Contains("\"paneId\":\"p1\"", applyScript);
        Assert.Contains("\"title\":\"session\"", applyScript);
        Assert.DoesNotContain("workspaceName", applyScript);
        Assert.DoesNotContain("workspace-private", applyScript);
    }

    [Fact]
    public async Task ApplyLayout_Empty_returns_bridge_bool_and_exact_discriminated_camelCase_json()
    {
        var page = await InitializedAsync();
        _host.ScriptResult = _ => "true";

        var accepted = await page.ApplyLayoutAsync(new TerminalLayout.Empty(3L));

        Assert.True(accepted);
        var applyScript = Assert.Single(_host.Scripts, s => s.Contains("agterm.applyLayout"));
        Assert.Contains("""{"type":"empty","generation":3}""", applyScript);
        Assert.DoesNotContain("paneId", applyScript);
        Assert.DoesNotContain("title", applyScript);
    }

    [Fact]
    public async Task SetInterceptChords_posts_the_chord_table_and_chord_messages_raise_the_event()
    {
        var page = await InitializedAsync();
        await page.OpenTerminalAsync("p1");
        await page.SetInterceptChordsAsync(
            new[] { new AgTerm.Core.Keymap.PhysicalChord(true, false, false, "n") });
        var script = Assert.Single(_host.Scripts, s => s.Contains("agterm.setChords"));
        Assert.Contains("\"ctrl\":true", script);
        Assert.Contains("\"key\":\"n\"", script);

        var pressed = new List<(string PaneId, AgTerm.Core.Keymap.PhysicalChord Chord)>();
        page.ChordPressed += (paneId, chord) => pressed.Add((paneId, chord));
        _host.RaiseMessage("""{"type":"chord","paneId":"p1","ctrl":true,"alt":false,"shift":false,"key":"n"}""");
        var hit = Assert.Single(pressed);
        Assert.Equal("p1", hit.PaneId);
        Assert.Equal(new AgTerm.Core.Keymap.PhysicalChord(true, false, false, "n"), hit.Chord);
    }

    [Fact]
    public async Task SetHotkeyManifest_serializes_custom_bindings_and_routes_generation_stamped_event()
    {
        var page = await InitializedAsync();
        await page.OpenTerminalAsync("p1");
        var commandId = Guid.NewGuid();
        var manifest = new AgTerm.Core.Keymap.HotkeyManifest(
            Generation: 7,
            LeaderTimeoutMs: 250,
            BuiltinSingles: [new(true, false, false, "n")],
            CustomBindings:
            [
                new(commandId,
                [
                    new(true, false, false, "a"),
                    new(false, false, false, "b"),
                ]),
            ]);

        _host.ScriptResult = _ => "true";
        await page.SetHotkeyManifestAsync(manifest);

        var script = Assert.Single(_host.Scripts, item => item.Contains("agterm.setHotkeyManifest"));
        Assert.Contains("\"generation\":7", script);
        Assert.Contains("\"leaderTimeoutMs\":250", script);
        Assert.Contains(commandId.ToString("D"), script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"chords\":[{\"ctrl\":true", script);

        AgTerm.Core.Keymap.CustomHotkeyEvent? observed = null;
        page.CustomCommandPressed += hit => observed = hit;
        _host.RaiseMessage($$"""
            {"type":"customHotkey","paneId":"p1","generation":7,"customCommandId":"{{commandId:D}}","ctrl":false,"alt":false,"shift":false,"key":"b"}
            """);

        Assert.NotNull(observed);
        Assert.Equal("p1", observed!.PaneId);
        Assert.Equal(commandId, observed.CommandId);
        Assert.Equal(7, observed.Generation);
        Assert.Equal(new(false, false, false, "b"), observed.Chord);
    }

    [Fact]
    public async Task ClosePane_runs_the_close_script_and_unroutes_the_terminal()
    {
        var page = await InitializedAsync();
        var one = await page.OpenTerminalAsync("p1");
        var data = new List<string>();
        one.DataReceived += data.Add;

        await page.ClosePaneAsync("p1");
        Assert.Contains("agterm.closePane(\"p1\")", _host.Scripts[^1]);
        _host.RaiseMessage("""{"type":"data","paneId":"p1","data":"late"}""");
        Assert.Empty(data);
    }

    // ── ApplyRendererSettingsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task ApplyRendererSettings_emits_applyRendererSettings_script_with_all_fields_camelCase()
    {
        var page = await InitializedAsync();
        var snapshot = RendererSettingsSnapshot.From(new AppSettings
        {
            FontFamily = "Fira Code",
            FontSize = 16.0,
            BackgroundOpacity = 0.85,
            BackgroundBlur = 5,
            MouseScrollMultiplier = 5.0,
            RightClickPaste = false,
        });

        await page.ApplyRendererSettingsAsync(snapshot);

        var script = Assert.Single(_host.Scripts, s => s.Contains("agterm.applyRendererSettings"));

        // every field must be present with its camelCase key
        Assert.Contains("\"fontFamily\"", script);
        Assert.Contains("\"fontSize\"", script);
        Assert.Contains("\"foregroundColor\"", script);
        Assert.Contains("\"backgroundColor\"", script);
        Assert.Contains("\"cursorColor\"", script);
        Assert.Contains("\"selectionBackgroundColor\"", script);
        Assert.Contains("\"backgroundOpacity\"", script);
        Assert.Contains("\"backgroundBlur\"", script);
        Assert.Contains("\"cursorBlink\"", script);
        Assert.Contains("\"cursorStyle\"", script);
        Assert.Contains("\"scrollSensitivity\"", script);
        Assert.Contains("\"scrollback\"", script);
        Assert.Contains("\"rightClickPaste\"", script);
        Assert.Contains("\"screenReaderMode\"", script);

        // none of those fields may appear with PascalCase names
        Assert.DoesNotContain("\"FontFamily\"", script);
        Assert.DoesNotContain("\"FontSize\"", script);
        Assert.DoesNotContain("\"ForegroundColor\"", script);
        Assert.DoesNotContain("\"BackgroundColor\"", script);
        Assert.DoesNotContain("\"CursorColor\"", script);
        Assert.DoesNotContain("\"SelectionBackgroundColor\"", script);
        Assert.DoesNotContain("\"BackgroundOpacity\"", script);
        Assert.DoesNotContain("\"BackgroundBlur\"", script);
        Assert.DoesNotContain("\"CursorBlink\"", script);
        Assert.DoesNotContain("\"CursorStyle\"", script);
        Assert.DoesNotContain("\"ScrollSensitivity\"", script);
        Assert.DoesNotContain("\"Scrollback\"", script);
        Assert.DoesNotContain("\"RightClickPaste\"", script);
        Assert.DoesNotContain("\"ScreenReaderMode\"", script);

        // non-default values appear verbatim so the serialization is verifiable
        Assert.Contains("\"Fira Code\"", script);
        Assert.Contains("\"fontSize\":16", script);
        Assert.Contains("\"backgroundOpacity\":0.85", script);
        Assert.Contains("\"backgroundBlur\":5", script);
        Assert.Contains("\"scrollSensitivity\":5", script);
        Assert.Contains("\"rightClickPaste\":false", script);
    }

    [Fact]
    public async Task ApplyRendererSettings_called_twice_emits_two_scripts_and_second_carries_updated_settings()
    {
        var page = await InitializedAsync();
        var first = RendererSettingsSnapshot.From(new AppSettings { FontFamily = "Fira Code", FontSize = 14.0 });
        var second = RendererSettingsSnapshot.From(new AppSettings { FontFamily = "Consolas", FontSize = 20.0 });

        await page.ApplyRendererSettingsAsync(first);
        await page.ApplyRendererSettingsAsync(second);

        var scripts = _host.Scripts.Where(s => s.Contains("agterm.applyRendererSettings")).ToList();
        Assert.Equal(2, scripts.Count);
        Assert.Contains("\"Fira Code\"", scripts[0]);
        Assert.Contains("\"Consolas\"", scripts[1]);
        Assert.DoesNotContain("\"Fira Code\"", scripts[1]);
    }

    [Fact]
    public async Task ApplyRendererSettings_null_snapshot_throws_ArgumentNullException()
    {
        var page = await InitializedAsync();
        await Assert.ThrowsAsync<ArgumentNullException>(() => page.ApplyRendererSettingsAsync(null!));
    }

    [Fact]
    public async Task ApplyRendererSettings_does_not_affect_per_pane_fontSize_passed_to_openPane()
    {
        var page = await InitializedAsync();
        var snapshot = RendererSettingsSnapshot.From(new AppSettings { FontSize = 24.0 });

        await page.ApplyRendererSettingsAsync(snapshot);
        await page.OpenTerminalAsync("p1", fontSize: 19);

        Assert.Contains("agterm.openPane(\"p1\", {\"fontSize\":19})", _host.Scripts[^1]);
    }

    // ── PasteRequested routing ────────────────────────────────────────────────

    [Fact]
    public async Task PasteRequested_fires_for_known_pane_and_not_for_unknown_or_missing()
    {
        var page = await InitializedAsync();
        await page.OpenTerminalAsync("p1");

        var pasted = new List<string>();
        page.PasteRequested += pasted.Add;

        _host.RaiseMessage("""{"type":"pasteRequested","paneId":"p1"}""");
        Assert.Equal("p1", Assert.Single(pasted));

        _host.RaiseMessage("""{"type":"pasteRequested","paneId":"ghost"}""");
        Assert.Single(pasted);

        _host.RaiseMessage("""{"type":"pasteRequested"}""");
        Assert.Single(pasted);
    }
}
