using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class XtermPageAskTests
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

    [Fact]
    public async Task ApplyAsk_serializes_the_exact_bridge_contract()
    {
        var page = await InitializedAsync();

        await page.ApplyAskAsync(new TerminalAskView("ASK", "right-token", new[] { "right-token" }, "Delete?", null,
            new[] { new TerminalAskButtonView(new[] { new AskLabelRun("! ", false), new AskLabelRun("D", true), new AskLabelRun("elete", false) }, true) },
            0, "right", null));

        var script = Assert.Single(_host.Scripts, item => item.Contains("agterm.applyAsk"));
        Assert.Contains(
            """{"askId":"ASK","paneId":"right-token","coveredPaneIds":["right-token"],"title":"Delete?","message":null,"buttons":[{"runs":[{"text":"! ","underline":false},{"text":"D","underline":true},{"text":"elete","underline":false}],"destructive":true}],"highlighted":0,"align":"right","width":null}""",
            script);
    }

    [Fact]
    public async Task ApplyAsk_with_no_view_clears_the_page()
    {
        var page = await InitializedAsync();
        await page.ApplyAskAsync(null);
        Assert.Contains(_host.Scripts, item => item.Contains("agterm.applyAsk(null)"));
    }

    [Theory]
    [InlineData("""{"type":"askIntent","askId":"A","intent":"forward"}""", "forward")]
    [InlineData("""{"type":"askIntent","askId":"A","intent":"backward"}""", "backward")]
    [InlineData("""{"type":"askIntent","askId":"A","intent":"activate"}""", "activate")]
    [InlineData("""{"type":"askIntent","askId":"A","intent":"cancel"}""", "cancel")]
    [InlineData("""{"type":"askIntent","askId":"A","intent":"hotkey","letter":"y"}""", "hotkey:y")]
    [InlineData("""{"type":"askIntent","askId":"A","intent":"press","index":2}""", "press:2")]
    public async Task Ask_intents_reach_the_host(string json, string expected)
    {
        var page = await InitializedAsync();
        string? seen = null;
        page.AskIntentReceived += (askId, intent) => seen = askId + "/" + intent switch
        {
            AskIntent.Forward => "forward",
            AskIntent.Backward => "backward",
            AskIntent.Activate => "activate",
            AskIntent.Cancel => "cancel",
            AskIntent.Hotkey hotkey => "hotkey:" + hotkey.Letter,
            AskIntent.Press press => "press:" + press.Index,
            _ => "?",
        };

        _host.RaiseMessage(json);

        Assert.Equal("A/" + expected, seen);
    }

    [Theory]
    [InlineData("""{"type":"askIntent","askId":"A","intent":"bogus"}""")]
    [InlineData("""{"type":"askIntent","askId":"A","intent":"hotkey"}""")]
    [InlineData("""{"type":"askIntent","askId":"A","intent":"press"}""")]
    [InlineData("""{"type":"askIntent","intent":"activate"}""")]
    public async Task Malformed_ask_intents_are_dropped(string json)
    {
        var page = await InitializedAsync();
        var raised = false;
        page.AskIntentReceived += (_, _) => raised = true;
        _host.RaiseMessage(json);
        Assert.False(raised);
    }

    [Fact]
    public async Task Ask_focus_changes_reach_the_host()
    {
        var page = await InitializedAsync();
        var seen = new List<string?>();
        page.AskFocusChanged += seen.Add;

        _host.RaiseMessage("""{"type":"askFocus","askId":"A"}""");
        _host.RaiseMessage("""{"type":"askFocus","askId":null}""");

        Assert.Equal(new string?[] { "A", null }, seen);
    }
}
