using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgTerm.Xterm;

/// <summary>
/// The C# owner of ONE terminal.html page hosting MANY xterm.js panes (bridge v2; bridge.js is the
/// page half). Boots the page once, opens per-pane XtermTerminal handles keyed by pane id, and
/// demultiplexes every page→host message to the terminal whose paneId matches. Thread-affine to
/// the WebView2 UI thread, like the seam itself.
/// </summary>
public sealed class XtermPage
{
    private const string AssetsHost = "agterm.assets";

    private readonly IWebViewScriptHost _host;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<string, XtermTerminal> _terminals = new();

    public XtermPage(IWebViewScriptHost host)
    {
        _host = host;
    }

    /// <summary>Maps the asset folder, loads terminal.html and completes when the page's bridge is
    /// up (its single ready message). Call on the WebView2 UI thread after the core is created.</summary>
    public Task InitializeAsync()
    {
        _host.JsonMessageReceived += OnMessage;
        _host.MapVirtualHostToFolder(AssetsHost, XtermAssets.Directory);
        _host.Navigate($"https://{AssetsHost}/terminal.html");
        return _ready.Task;
    }

    /// <summary>Creates the pane in the page (agterm.openPane) and returns its handle at the fitted
    /// grid. The page shows the first opened pane automatically.</summary>
    public async Task<XtermTerminal> OpenTerminalAsync(string paneId, double? fontSize = null)
    {
        var opts = fontSize is { } px
            ? $"{{\"fontSize\":{px.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"
            : "{}";
        var result = await _host.ExecuteScriptAsync(
            $"agterm.openPane({PaneLiteral(paneId)}, {opts})").ConfigureAwait(false);
        var grid = JsonSerializer.Deserialize<GridDto>(result);
        var terminal = new XtermTerminal(this, paneId, grid?.Cols ?? 0, grid?.Rows ?? 0);
        _terminals[paneId] = terminal;
        return terminal;
    }

    /// <summary>Show one pane, hide the rest; the page refits the shown pane and posts its resize.</summary>
    public Task ShowPaneAsync(string paneId) =>
        _host.ExecuteScriptAsync($"agterm.showPane({PaneLiteral(paneId)})");

    /// <summary>Dispose the pane's terminal and div, and stop routing messages to its handle.</summary>
    public async Task ClosePaneAsync(string paneId)
    {
        _terminals.Remove(paneId);
        await _host.ExecuteScriptAsync($"agterm.closePane({PaneLiteral(paneId)})").ConfigureAwait(false);
    }

    internal Task<string> ExecuteScriptAsync(string script) => _host.ExecuteScriptAsync(script);

    internal void PostJson(string json) => _host.PostJson(json);

    internal static string PaneLiteral(string paneId) =>
        JsonSerializer.Serialize(paneId, XtermTerminal.ScriptLiteral);

    private void OnMessage(string json)
    {
        BridgeMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<BridgeMessage>(json);
        }
        catch (JsonException)
        {
            return; // not a bridge message
        }
        if (msg?.Type == "ready")
        {
            _ready.TrySetResult();
            return;
        }
        if (msg?.PaneId is { } paneId && _terminals.TryGetValue(paneId, out var terminal))
            terminal.HandleMessage(msg);
    }

    private sealed record GridDto
    {
        [JsonPropertyName("cols")] public int Cols { get; init; }
        [JsonPropertyName("rows")] public int Rows { get; init; }
    }
}

/// <summary>One page→host bridge message; paneId addresses the pane (absent only on ready).</summary>
internal sealed record BridgeMessage
{
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("paneId")] public string? PaneId { get; init; }
    [JsonPropertyName("data")] public string? Data { get; init; }
    [JsonPropertyName("cols")] public int? Cols { get; init; }
    [JsonPropertyName("rows")] public int? Rows { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("uri")] public string? Uri { get; init; }
}
