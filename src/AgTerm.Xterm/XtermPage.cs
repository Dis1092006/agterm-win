using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

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
    private static readonly JsonSerializerOptions RendererSettingsScriptLiteral = new(XtermTerminal.ScriptLiteral)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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

    /// <summary>Apply a discriminated layout on the page and return whether the page accepted it.
    /// Unknown layout variants throw so new variants cannot serialize silently.</summary>
    public async Task<bool> ApplyLayoutAsync(TerminalLayout layout)
    {
        var payload = SerializeLayout(layout);
        var raw = await _host.ExecuteScriptAsync($"agterm.applyLayout({payload})").ConfigureAwait(false);
        return JsonSerializer.Deserialize<bool>(raw);
    }

    /// <summary>Apply page-global renderer settings for all panes (last writer wins).</summary>
    public Task ApplyRendererSettingsAsync(RendererSettingsSnapshot settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var payload = JsonSerializer.Serialize(settings, RendererSettingsScriptLiteral);
        return _host.ExecuteScriptAsync($"agterm.applyRendererSettings({payload})");
    }

    /// <summary>Dispose the pane's terminal and div, and stop routing messages to its handle.</summary>
    public async Task ClosePaneAsync(string paneId)
    {
        _terminals.Remove(paneId);
        await _host.ExecuteScriptAsync($"agterm.closePane({PaneLiteral(paneId)})").ConfigureAwait(false);
    }

    /// <summary>A key press one of the panes intercepted against the pushed chord table — the
    /// window's hotkey dispatcher handles it (the shell never saw the key).</summary>
    public event Action<string, Core.Keymap.PhysicalChord>? ChordPressed;

    /// <summary>Raised when the page requests host-side paste for a known pane id.</summary>
    public event Action<string>? PasteRequested;

    public event Action<string>? PaneFocusChanged;

    public event Action<string, string, double>? SplitResized;

    /// <summary>Push the hotkey table every pane intercepts (replaces the previous table; empty
    /// clears the interception). Call after keymap load/reload, on the WebView2 UI thread — a
    /// script call, so nothing dispatched afterwards can outrun the table.</summary>
    public Task SetInterceptChordsAsync(IEnumerable<Core.Keymap.PhysicalChord> chords)
    {
        var items = chords.Select(c => new { ctrl = c.Ctrl, alt = c.Alt, shift = c.Shift, key = c.Key });
        return _host.ExecuteScriptAsync(
            $"agterm.setChords({JsonSerializer.Serialize(items, XtermTerminal.ScriptLiteral)})");
    }

    internal Task<string> ExecuteScriptAsync(string script) => _host.ExecuteScriptAsync(script);

    internal void PostJson(string json) => _host.PostJson(json);

    internal static string PaneLiteral(string paneId) =>
        JsonSerializer.Serialize(paneId, XtermTerminal.ScriptLiteral);

    private static string SerializeLayout(TerminalLayout layout)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            switch (layout)
            {
                case TerminalLayout.Single single:
                    writer.WriteString("type", "single");
                    writer.WriteNumber("generation", single.Generation);
                    writer.WriteString("paneId", single.PaneId);
                    writer.WriteString("title", single.Title);
                    break;
                case TerminalLayout.Split split:
                    writer.WriteString("type", "split");
                    writer.WriteNumber("generation", split.Generation);
                    writer.WriteString("leftPaneId", split.LeftPaneId);
                    writer.WriteString("rightPaneId", split.RightPaneId);
                    writer.WriteNumber("ratio", split.Ratio);
                    writer.WriteString("focusedPaneId", split.FocusedPaneId);
                    break;
                case TerminalLayout.Empty empty:
                    writer.WriteString("type", "empty");
                    writer.WriteNumber("generation", empty.Generation);
                    break;
                default:
                    throw new NotSupportedException($"Unknown terminal layout variant: {layout.GetType().FullName}");
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

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
        if (msg is { Type: "chord", PaneId: { } chordPane, Key: { } key })
        {
            ChordPressed?.Invoke(chordPane,
                new Core.Keymap.PhysicalChord(msg.Ctrl ?? false, msg.Alt ?? false, msg.Shift ?? false, key));
            return;
        }
        if (msg?.Type == "pasteRequested")
        {
            if (msg.PaneId is { } pastePaneId && _terminals.ContainsKey(pastePaneId))
                PasteRequested?.Invoke(pastePaneId);
            return;
        }

        if (msg?.Type == "paneFocus")
        {
            if (msg.PaneId is { } focusedPaneId && _terminals.ContainsKey(focusedPaneId))
                PaneFocusChanged?.Invoke(focusedPaneId);
            return;
        }

        if (msg?.Type == "splitResize")
        {
            if (msg.LeftPaneId is { } leftPaneId
                && msg.RightPaneId is { } rightPaneId
                && leftPaneId != rightPaneId
                && _terminals.ContainsKey(leftPaneId)
                && _terminals.ContainsKey(rightPaneId)
                && msg.Ratio is { } ratio
                && double.IsFinite(ratio))
            {
                SplitResized?.Invoke(leftPaneId, rightPaneId, Core.Model.AppStore.ClampSplitRatio(ratio));
            }

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
    [JsonPropertyName("ctrl")] public bool? Ctrl { get; init; }
    [JsonPropertyName("alt")] public bool? Alt { get; init; }
    [JsonPropertyName("shift")] public bool? Shift { get; init; }
    [JsonPropertyName("key")] public string? Key { get; init; }
    [JsonPropertyName("leftPaneId")] public string? LeftPaneId { get; init; }
    [JsonPropertyName("rightPaneId")] public string? RightPaneId { get; init; }
    [JsonPropertyName("ratio")] public double? Ratio { get; init; }
}
