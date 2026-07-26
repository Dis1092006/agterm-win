using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgTerm.Xterm;

public enum SearchDirection
{
    Next,
    Prev,
}

/// <summary>One search step's result: whether a match was selected, and the "N of M" numbers
/// (index is 0-based from the search addon; -1 when unknown).</summary>
public readonly record struct SearchOutcome(bool Found, int Index, int Count);

/// <summary>
/// The C# half of the terminal bridge (bridge.js is the page half). Owns the message protocol over
/// an IWebViewScriptHost: pty output feeds in, keystrokes/resizes/OSC reports come out as events,
/// and buffer reads (text/selection/search) run as script evaluations. Thread-affine to the
/// WebView2 UI thread, like the seam itself.
/// </summary>
public sealed class XtermTerminal
{
    private const string AssetsHost = "agterm.assets";

    /// <summary>For strings embedded in scripts: plain \" instead of " — both are valid JS,
    /// this one stays readable in logs/tests.</summary>
    private static readonly JsonSerializerOptions ScriptLiteral = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IWebViewScriptHost _host;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public XtermTerminal(IWebViewScriptHost host)
    {
        _host = host;
    }

    public int Cols { get; private set; }

    public int Rows { get; private set; }

    /// <summary>User keystrokes from xterm (onData), verbatim — Enter is already CR.</summary>
    public event Action<string>? DataReceived;

    /// <summary>The fit addon recomputed cols/rows — forward to ConPTY resize.</summary>
    public event Action<int, int>? Resized;

    /// <summary>OSC 0/1/2 title report.</summary>
    public event Action<string>? TitleChanged;

    /// <summary>OSC 7 / OSC 9;9 cwd report, already parsed to a local path.</summary>
    public event Action<string>? CwdChanged;

    /// <summary>Maps the asset folder, loads terminal.html and completes when the page's xterm
    /// is up (its ready message). Call on the WebView2 UI thread after the core is created.</summary>
    public Task InitializeAsync()
    {
        _host.JsonMessageReceived += OnMessage;
        _host.MapVirtualHostToFolder(AssetsHost, XtermAssets.Directory);
        _host.Navigate($"https://{AssetsHost}/terminal.html");
        return _ready.Task;
    }

    /// <summary>Raw VT output from the pty → xterm.write (base64 over the message channel;
    /// xterm decodes UTF-8 itself, so chunk boundaries inside a rune are safe).</summary>
    public void FeedOutput(byte[] chunk) =>
        _host.PostJson(JsonSerializer.Serialize(new { type = "output", data = Convert.ToBase64String(chunk) }));

    public void SetFontSize(double px) =>
        _host.PostJson(JsonSerializer.Serialize(new { type = "options", fontSize = px }));

    public async Task<string> ReadTextAsync(bool all, int? lines)
    {
        var result = await _host.ExecuteScriptAsync(
            $"agterm.text({(all ? "true" : "false")}, {(lines?.ToString() ?? "null")})").ConfigureAwait(false);
        return DecodeString(result) ?? "";
    }

    public async Task<string> GetSelectionAsync() =>
        DecodeString(await _host.ExecuteScriptAsync("agterm.selection()").ConfigureAwait(false)) ?? "";

    public Task SelectAllAsync() => _host.ExecuteScriptAsync("agterm.selectAll()");

    /// <summary>The full buffer as VT sequences (serialize addon) — the restore/persistence read.</summary>
    public async Task<string> SerializeAsync() =>
        DecodeString(await _host.ExecuteScriptAsync("agterm.serialize()").ConfigureAwait(false)) ?? "";

    public async Task<SearchOutcome> SearchAsync(string needle, SearchDirection direction)
    {
        var script = $"agterm.search({JsonSerializer.Serialize(needle, ScriptLiteral)}, " +
                     $"{JsonSerializer.Serialize(direction == SearchDirection.Prev ? "prev" : "next", ScriptLiteral)})";
        var result = await _host.ExecuteScriptAsync(script).ConfigureAwait(false);
        var outcome = JsonSerializer.Deserialize<SearchResultDto>(result);
        return new SearchOutcome(outcome?.Found ?? false, outcome?.Index ?? -1, outcome?.Count ?? 0);
    }

    public Task SearchCloseAsync() => _host.ExecuteScriptAsync("agterm.searchClose()");

    public Task FocusAsync() => _host.ExecuteScriptAsync("agterm.focus()");

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
        switch (msg?.Type)
        {
            case "ready":
                Cols = msg.Cols ?? 0;
                Rows = msg.Rows ?? 0;
                _ready.TrySetResult();
                break;
            case "data" when msg.Data is not null:
                DataReceived?.Invoke(msg.Data);
                break;
            case "resize" when msg is { Cols: { } cols, Rows: { } rows }:
                Cols = cols;
                Rows = rows;
                Resized?.Invoke(cols, rows);
                break;
            case "title" when msg.Title is not null:
                TitleChanged?.Invoke(msg.Title);
                break;
            case "cwd" when msg.Uri is not null:
                if (CwdReport.Parse(msg.Uri) is { } path) CwdChanged?.Invoke(path);
                break;
        }
    }

    /// <summary>ExecuteScriptAsync returns the value JSON-encoded; a JS string arrives quoted.</summary>
    private static string? DecodeString(string jsonResult) =>
        jsonResult == "null" ? null : JsonSerializer.Deserialize<string>(jsonResult);

    private sealed record BridgeMessage
    {
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("data")] public string? Data { get; init; }
        [JsonPropertyName("cols")] public int? Cols { get; init; }
        [JsonPropertyName("rows")] public int? Rows { get; init; }
        [JsonPropertyName("title")] public string? Title { get; init; }
        [JsonPropertyName("uri")] public string? Uri { get; init; }
    }

    private sealed record SearchResultDto
    {
        [JsonPropertyName("found")] public bool Found { get; init; }
        [JsonPropertyName("index")] public int Index { get; init; }
        [JsonPropertyName("count")] public int Count { get; init; }
    }
}
