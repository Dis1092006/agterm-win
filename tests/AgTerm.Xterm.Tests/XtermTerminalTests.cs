using System.Text;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>Per-pane XtermTerminal behavior over the shared page (bridge v2): every host→page
/// message and every script call is addressed by the terminal's pane id.</summary>
public class XtermTerminalTests
{
    private readonly FakeScriptHost _host = new();

    private async Task<XtermTerminal> OpenedAsync(string paneId = "p1")
    {
        var page = new XtermPage(_host);
        var init = page.InitializeAsync();
        _host.RaiseMessage("""{"type":"ready"}""");
        await init;
        return await page.OpenTerminalAsync(paneId);
    }

    [Fact]
    public async Task FeedOutput_posts_base64_output_addressed_to_the_pane()
    {
        var terminal = await OpenedAsync();
        terminal.FeedOutput(Encoding.UTF8.GetBytes("hi"));
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("hi"));
        var json = Assert.Single(_host.PostedJson);
        Assert.Contains("\"output\"", json);
        Assert.Contains("\"p1\"", json);
        Assert.Contains(expected, json);
    }

    [Fact]
    public async Task SetFontSize_posts_options_addressed_to_the_pane()
    {
        var terminal = await OpenedAsync();
        terminal.SetFontSize(18);
        var json = Assert.Single(_host.PostedJson);
        Assert.Contains("\"options\"", json);
        Assert.Contains("\"p1\"", json);
        Assert.Contains("18", json);
    }

    [Fact]
    public async Task ReadText_addresses_the_buffer_read_by_pane_id()
    {
        var terminal = await OpenedAsync();
        _host.ScriptResult = s => s.Contains("agterm.text") ? "\"line1\\nline2\"" : "null";
        Assert.Equal("line1\nline2", await terminal.ReadTextAsync(all: true, lines: null));
        Assert.Contains("agterm.text(\"p1\", true, null)", _host.Scripts[^1]);

        await terminal.ReadTextAsync(all: false, lines: 5);
        Assert.Contains("agterm.text(\"p1\", false, 5)", _host.Scripts[^1]);
    }

    [Fact]
    public async Task GetSelection_decodes_the_selection_string()
    {
        var terminal = await OpenedAsync();
        _host.ScriptResult = s => s.Contains("agterm.selection") ? "\"picked\"" : "null";
        Assert.Equal("picked", await terminal.GetSelectionAsync());
        Assert.Contains("agterm.selection(\"p1\")", _host.Scripts[^1]);
    }

    [Fact]
    public async Task Search_escapes_the_needle_and_parses_the_outcome()
    {
        var terminal = await OpenedAsync();
        _host.ScriptResult = s => s.Contains("agterm.search")
            ? """{"found":true,"index":1,"count":3}"""
            : "null";
        var outcome = await terminal.SearchAsync("say \"hi\"", SearchDirection.Next);
        Assert.True(outcome.Found);
        Assert.Equal(1, outcome.Index);
        Assert.Equal(3, outcome.Count);
        Assert.Contains("agterm.search(\"p1\", \"say \\\"hi\\\"\", \"next\")", _host.Scripts[^1]);
    }

    [Fact]
    public async Task Remaining_script_calls_are_addressed_by_pane_id()
    {
        var terminal = await OpenedAsync();
        _host.ScriptResult = s => s.Contains("setFontSize") ? "15" : "null";

        await terminal.SelectAllAsync();
        Assert.Contains("agterm.selectAll(\"p1\")", _host.Scripts[^1]);
        await terminal.SerializeAsync();
        Assert.Contains("agterm.serialize(\"p1\")", _host.Scripts[^1]);
        await terminal.SearchCloseAsync();
        Assert.Contains("agterm.searchClose(\"p1\")", _host.Scripts[^1]);
        await terminal.PasteAsync("x");
        Assert.Contains("agterm.paste(\"p1\", \"x\")", _host.Scripts[^1]);
        Assert.Equal(15, await terminal.SetFontSizeAsync(15));
        Assert.Contains("agterm.setFontSize(\"p1\", 15)", _host.Scripts[^1]);
        await terminal.FocusAsync();
        Assert.Contains("agterm.focus(\"p1\")", _host.Scripts[^1]);
    }
}
