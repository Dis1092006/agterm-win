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
    public async Task OpenTerminal_passes_the_font_size_into_openPane()
    {
        var page = await InitializedAsync();
        await page.OpenTerminalAsync("p1", fontSize: 16);
        Assert.Contains("agterm.openPane(\"p1\", {\"fontSize\":16})", _host.Scripts[^1]);
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
}
