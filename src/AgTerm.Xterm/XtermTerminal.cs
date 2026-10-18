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
/// One pane's handle on the shared terminal page (bridge v2): pty output feeds in, keystrokes/
/// resizes/OSC reports come out as events, and buffer reads (text/selection/search) run as script
/// evaluations — all addressed by this pane's id. Created by XtermPage.OpenTerminalAsync; thread-
/// affine to the WebView2 UI thread, like the seam itself.
/// </summary>
public sealed class XtermTerminal
{
    /// <summary>For strings embedded in scripts: plain \" instead of &#92;u0022 — both are valid JS,
    /// this one stays readable in logs/tests.</summary>
    internal static readonly JsonSerializerOptions ScriptLiteral = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly XtermPage _page;
    private readonly string _pane;

    internal XtermTerminal(XtermPage page, string paneId, int cols, int rows)
    {
        _page = page;
        _pane = JsonSerializer.Serialize(paneId, ScriptLiteral); // pre-quoted script literal
        PaneId = paneId;
        Cols = cols;
        Rows = rows;
    }

    /// <summary>The pane id addressing this terminal in the page (also the surface's spawn token).</summary>
    public string PaneId { get; }

    public int Cols { get; private set; }

    public int Rows { get; private set; }

    /// <summary>User keystrokes from xterm (onData), verbatim — already encoded by the engine:
    /// Enter is CR, and under a pushed kitty keyboard protocol keys carry their CSI u form
    /// (Shift+Enter becomes ESC [13;2u), which is what makes it distinguishable from Enter.</summary>
    public event Action<string>? DataReceived;

    /// <summary>The fit addon recomputed cols/rows — forward to ConPTY resize.</summary>
    public event Action<int, int>? Resized;

    /// <summary>OSC 0/1/2 title report.</summary>
    public event Action<string>? TitleChanged;

    /// <summary>OSC 7 / OSC 9;9 cwd report, already parsed to a local path.</summary>
    public event Action<string>? CwdChanged;

    /// <summary>Raw VT output from the pty → xterm.write (base64 over the message channel;
    /// xterm decodes UTF-8 itself, so chunk boundaries inside a rune are safe).</summary>
    public void FeedOutput(ReadOnlyMemory<byte> chunk) =>
        _page.PostJson(JsonSerializer.Serialize(new
        {
            type = "output",
            paneId = PaneId,
            data = Convert.ToBase64String(chunk.Span),
        }));

    public void SetFontSize(double px) =>
        _page.PostJson(JsonSerializer.Serialize(new { type = "options", paneId = PaneId, fontSize = px }));

    public async Task<string> ReadTextAsync(bool all, int? lines)
    {
        var result = await _page.ExecuteScriptAsync(
            $"agterm.text({_pane}, {(all ? "true" : "false")}, {(lines?.ToString() ?? "null")})")
            .ConfigureAwait(false);
        return DecodeString(result) ?? "";
    }

    public async Task<string> GetSelectionAsync() =>
        DecodeString(await _page.ExecuteScriptAsync($"agterm.selection({_pane})").ConfigureAwait(false)) ?? "";

    public Task SelectAllAsync() => _page.ExecuteScriptAsync($"agterm.selectAll({_pane})");

    /// <summary>The full buffer as VT sequences (serialize addon) — the restore/persistence read.</summary>
    public async Task<string> SerializeAsync() =>
        DecodeString(await _page.ExecuteScriptAsync($"agterm.serialize({_pane})").ConfigureAwait(false)) ?? "";

    /// <summary>The normal-screen buffer as VT sequences without terminal modes — what is saved at
    /// shutdown and written back into the pane after a relaunch.</summary>
    public async Task<string> SerializeForRestoreAsync() =>
        DecodeString(await _page.ExecuteScriptAsync($"agterm.serializeForRestore({_pane})").ConfigureAwait(false)) ?? "";

    public async Task<SearchOutcome> SearchAsync(string needle, SearchDirection direction)
    {
        var script = $"agterm.search({_pane}, {JsonSerializer.Serialize(needle, ScriptLiteral)}, " +
                     $"{JsonSerializer.Serialize(direction == SearchDirection.Prev ? "prev" : "next", ScriptLiteral)})";
        var result = await _page.ExecuteScriptAsync(script).ConfigureAwait(false);
        var outcome = JsonSerializer.Deserialize<SearchResultDto>(result);
        return new SearchOutcome(outcome?.Found ?? false, outcome?.Index ?? -1, outcome?.Count ?? 0);
    }

    public Task SearchCloseAsync() => _page.ExecuteScriptAsync($"agterm.searchClose({_pane})");

    /// <summary>Paste text through xterm (bracketed-paste aware — the terminal-mode-correct route,
    /// unlike raw pty writes).</summary>
    public Task PasteAsync(string text) =>
        _page.ExecuteScriptAsync($"agterm.paste({_pane}, {JsonSerializer.Serialize(text, ScriptLiteral)})");

    /// <summary>Apply a font size and refit; returns the size xterm actually holds.</summary>
    public async Task<double> SetFontSizeAsync(double px)
    {
        var result = await _page.ExecuteScriptAsync(
            $"agterm.setFontSize({_pane}, {px.ToString(System.Globalization.CultureInfo.InvariantCulture)})")
            .ConfigureAwait(false);
        return result == "null" ? 0 : JsonSerializer.Deserialize<double>(result);
    }

    public Task FocusAsync() => _page.ExecuteScriptAsync($"agterm.focus({_pane})");

    internal void HandleMessage(BridgeMessage msg)
    {
        switch (msg.Type)
        {
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

    private sealed record SearchResultDto
    {
        [JsonPropertyName("found")] public bool Found { get; init; }
        [JsonPropertyName("index")] public int Index { get; init; }
        [JsonPropertyName("count")] public int Count { get; init; }
    }
}
