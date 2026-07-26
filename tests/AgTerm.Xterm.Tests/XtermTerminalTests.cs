using System.Text;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>Records every host call and lets a test play the page's side of the protocol.</summary>
internal sealed class FakeScriptHost : IWebViewScriptHost
{
    public readonly List<(string Host, string Folder)> Mapped = new();
    public readonly List<string> Navigated = new();
    public readonly List<string> PostedJson = new();
    public readonly List<string> Scripts = new();

    /// <summary>Script → JSON-encoded result the fake returns (ExecuteScriptAsync semantics).</summary>
    public Func<string, string> ScriptResult { get; set; } = _ => "null";

    public event Action<string>? JsonMessageReceived;

    public void MapVirtualHostToFolder(string hostName, string folderPath) =>
        Mapped.Add((hostName, folderPath));

    public void Navigate(string url) => Navigated.Add(url);

    public Task<string> ExecuteScriptAsync(string script)
    {
        Scripts.Add(script);
        return Task.FromResult(ScriptResult(script));
    }

    public void PostJson(string json) => PostedJson.Add(json);

    public void RaiseMessage(string json) => JsonMessageReceived?.Invoke(json);
}

public class XtermTerminalTests
{
    private readonly FakeScriptHost _host = new();

    private async Task<XtermTerminal> InitializedAsync()
    {
        var terminal = new XtermTerminal(_host);
        var init = terminal.InitializeAsync();
        _host.RaiseMessage("""{"type":"ready","cols":80,"rows":24}""");
        await init;
        return terminal;
    }

    [Fact]
    public async Task Initialize_maps_assets_navigates_and_awaits_ready()
    {
        var terminal = new XtermTerminal(_host);
        var init = terminal.InitializeAsync();
        Assert.False(init.IsCompleted);
        Assert.Equal(("agterm.assets", XtermAssets.Directory), Assert.Single(_host.Mapped));
        Assert.Equal("https://agterm.assets/terminal.html", Assert.Single(_host.Navigated));

        _host.RaiseMessage("""{"type":"ready","cols":80,"rows":24}""");
        await init;
        Assert.Equal(80, terminal.Cols);
        Assert.Equal(24, terminal.Rows);
    }

    [Fact]
    public async Task FeedOutput_posts_base64_output_message()
    {
        var terminal = await InitializedAsync();
        terminal.FeedOutput(Encoding.UTF8.GetBytes("hi"));
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("hi"));
        var json = Assert.Single(_host.PostedJson);
        Assert.Contains("\"output\"", json);
        Assert.Contains(expected, json);
    }

    [Fact]
    public async Task Data_message_raises_DataReceived_verbatim()
    {
        var terminal = await InitializedAsync();
        var received = new List<string>();
        terminal.DataReceived += received.Add;
        _host.RaiseMessage("""{"type":"data","data":"ls\r"}""");
        Assert.Equal("ls\r", Assert.Single(received));
    }

    [Fact]
    public async Task Resize_message_raises_Resized_and_updates_size()
    {
        var terminal = await InitializedAsync();
        var sizes = new List<(int, int)>();
        terminal.Resized += (c, r) => sizes.Add((c, r));
        _host.RaiseMessage("""{"type":"resize","cols":132,"rows":43}""");
        Assert.Equal((132, 43), Assert.Single(sizes));
        Assert.Equal(132, terminal.Cols);
        Assert.Equal(43, terminal.Rows);
    }

    [Fact]
    public async Task Title_message_raises_TitleChanged()
    {
        var terminal = await InitializedAsync();
        var titles = new List<string>();
        terminal.TitleChanged += titles.Add;
        _host.RaiseMessage("""{"type":"title","title":"user@host: ~"}""");
        Assert.Equal("user@host: ~", Assert.Single(titles));
    }

    [Fact]
    public async Task Cwd_message_raises_CwdChanged_with_the_parsed_path()
    {
        var terminal = await InitializedAsync();
        var cwds = new List<string>();
        terminal.CwdChanged += cwds.Add;
        _host.RaiseMessage("""{"type":"cwd","uri":"file:///C:/git/proj"}""");
        _host.RaiseMessage("""{"type":"cwd","uri":"file://foreign/x"}"""); // unparseable → dropped
        Assert.Equal("C:\\git\\proj", Assert.Single(cwds));
    }

    [Fact]
    public async Task ReadText_executes_the_buffer_read_and_decodes_the_json_string()
    {
        var terminal = await InitializedAsync();
        _host.ScriptResult = script => script.Contains("agterm.text") ? "\"line1\\nline2\"" : "null";
        Assert.Equal("line1\nline2", await terminal.ReadTextAsync(all: true, lines: null));
        Assert.Contains("agterm.text(true, null)", _host.Scripts[^1]);

        await terminal.ReadTextAsync(all: false, lines: 5);
        Assert.Contains("agterm.text(false, 5)", _host.Scripts[^1]);
    }

    [Fact]
    public async Task GetSelection_decodes_the_selection_string()
    {
        var terminal = await InitializedAsync();
        _host.ScriptResult = script => script.Contains("agterm.selection") ? "\"picked\"" : "null";
        Assert.Equal("picked", await terminal.GetSelectionAsync());
    }

    [Fact]
    public async Task Search_escapes_the_needle_and_parses_the_outcome()
    {
        var terminal = await InitializedAsync();
        _host.ScriptResult = script => script.Contains("agterm.search")
            ? """{"found":true,"index":1,"count":3}"""
            : "null";
        var outcome = await terminal.SearchAsync("say \"hi\"", SearchDirection.Next);
        Assert.True(outcome.Found);
        Assert.Equal(1, outcome.Index);
        Assert.Equal(3, outcome.Count);
        // the needle travels as a JS string literal — quotes must be escaped, direction is 'next'
        Assert.Contains("agterm.search(\"say \\\"hi\\\"\", \"next\")", _host.Scripts[^1]);
    }
}
