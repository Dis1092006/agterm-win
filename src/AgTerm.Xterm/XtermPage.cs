using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using AgTerm.Core.Model;

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

    private static readonly JsonSerializerOptions AskScriptLiteral = new(XtermTerminal.ScriptLiteral)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    // the page reads an absent value as null, so every key of the HUD payload is always written
    private static readonly JsonSerializerOptions HudScriptLiteral = new(XtermTerminal.ScriptLiteral)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    // a null theme is the page's "use the global theme", so nulls are written, not dropped
    private static readonly JsonSerializerOptions PaneThemeScriptLiteral = new(XtermTerminal.ScriptLiteral)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
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
    /// grid. The page shows the first opened pane automatically. A theme opens the pane in it.
    /// <paramref name="readlineMarker"/> makes the pane listen for the PowerShell integration's
    /// readline marker (OSC 7777); only a pane running the integrated PowerShell should.</summary>
    public async Task<XtermTerminal> OpenTerminalAsync(string paneId, double? fontSize = null, TerminalTheme? theme = null,
        bool readlineMarker = false)
    {
        var result = await _host.ExecuteScriptAsync(
            $"agterm.openPane({PaneLiteral(paneId)}, {OpenOptions(fontSize, theme, readlineMarker)})");
        var grid = JsonSerializer.Deserialize<GridDto>(result);
        var terminal = new XtermTerminal(this, paneId, grid?.Cols ?? 0, grid?.Rows ?? 0);
        _terminals[paneId] = terminal;
        return terminal;
    }

    private static string OpenOptions(double? fontSize, TerminalTheme? theme, bool readlineMarker)
    {
        var members = new List<string>();
        if (fontSize is { } px)
            members.Add($"\"fontSize\":{px.ToString(CultureInfo.InvariantCulture)}");
        if (theme is not null)
            members.Add($"\"theme\":{JsonSerializer.Serialize(PaneThemePayload.From(theme), PaneThemeScriptLiteral)}");
        if (readlineMarker)
            members.Add("\"readlineMarker\":true");
        return "{" + string.Join(",", members) + "}";
    }

    /// <summary>Show one pane, hide the rest; the page refits the shown pane and posts its resize.</summary>
    public Task ShowPaneAsync(string paneId) =>
        _host.ExecuteScriptAsync($"agterm.showPane({PaneLiteral(paneId)})");

    public Task ScrollInactiveAtAsync(double xFraction, double yFraction, int wheelDelta)
    {
        var arguments = JsonSerializer.Serialize(new[] { xFraction, yFraction, wheelDelta });
        return _host.ExecuteScriptAsync($"agterm.scrollInactiveAt(...{arguments})");
    }

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

    /// <summary>Gives panes their own theme; null returns a pane to the global theme. The page
    /// takes only the colors from it, keeps the assignment through later renderer settings, and
    /// ignores an unknown pane id. Panes not named keep their assignment.</summary>
    public Task ApplyPaneThemesAsync(IReadOnlyDictionary<string, TerminalTheme?> themes)
    {
        ArgumentNullException.ThrowIfNull(themes);
        var payload = JsonSerializer.Serialize(
            themes.ToDictionary(
                pair => pair.Key,
                pair => pair.Value is null ? null : PaneThemePayload.From(pair.Value),
                StringComparer.Ordinal),
            PaneThemeScriptLiteral);
        return _host.ExecuteScriptAsync($"agterm.applyPaneThemes({payload})");
    }

    /// <summary>Sets the terminal-area background (the page, the split gap, an empty layout) to
    /// the selected session's workspace theme; null returns it to the global theme's.</summary>
    public Task SetTerminalBackgroundAsync(string? color) =>
        _host.ExecuteScriptAsync(
            $"agterm.setTerminalBackground({JsonSerializer.Serialize(color, XtermTerminal.ScriptLiteral)})");

    public Task ApplyAskAsync(TerminalAskView? view)
    {
        if (view is null)
            return _host.ExecuteScriptAsync("agterm.applyAsk(null)");
        var payload = JsonSerializer.Serialize(new
        {
            askId = view.AskId,
            paneId = view.PaneId,
            coveredPaneIds = view.CoveredPaneIds,
            title = view.Title,
            message = view.Message,
            buttons = view.Buttons.Select(button => new
            {
                runs = button.Runs.Select(run => new { text = run.Text, underline = run.Underline }),
                destructive = button.Destructive,
            }),
            highlighted = view.Highlighted,
            align = view.Align,
            width = view.Width,
        }, AskScriptLiteral);
        return _host.ExecuteScriptAsync($"agterm.applyAsk({payload})");
    }

    public Task ApplyHudAsync(TerminalHudView? view)
    {
        if (view is null)
            return _host.ExecuteScriptAsync("agterm.applyHud(null)");
        var payload = JsonSerializer.Serialize(new
        {
            paneId = view.PaneId,
            rows = view.Rows.Select(HudRowLiteral),
            widthPercent = view.WidthPercent,
            heightPoints = view.HeightPoints,
            heightPercent = view.HeightPercent,
            position = view.Position,
            sticky = view.Sticky,
            frame = view.Frame,
            markdown = view.Markdown,
            background = view.Background,
            foreground = view.Foreground,
            fontSize = view.FontSize,
            spinnerFrames = view.SpinnerFrames,
            spinnerInterval = view.SpinnerInterval,
            revision = view.Revision,
        }, HudScriptLiteral);
        return _host.ExecuteScriptAsync($"agterm.applyHud({payload})");
    }

    // the page tells a markdown row by its runs, so a plain row keeps the {text, dim} shape
    private static object HudRowLiteral(TerminalHudRow row) => row.Runs is null
        ? new { text = row.Text, dim = row.Dim }
        : new { text = row.Text, dim = row.Dim, runs = row.Runs.Select(HudRunLiteral) };

    private static object HudRunLiteral(TerminalHudRun run) => run.Link is null
        ? new { text = run.Text, bold = run.Bold, italic = run.Italic, strike = run.Strike, dim = run.Dim, underline = run.Underline }
        : new { text = run.Text, bold = run.Bold, italic = run.Italic, strike = run.Strike, dim = run.Dim, underline = run.Underline, link = run.Link };

    public Task ApplySearchAsync(
        string paneId,
        string needle,
        string displayText,
        int? selected,
        bool focusInput)
    {
        var payload = JsonSerializer.Serialize(new
        {
            paneId,
            needle,
            displayText,
            selected,
            focusInput,
        }, XtermTerminal.ScriptLiteral);
        return _host.ExecuteScriptAsync($"agterm.applySearch({payload})");
    }

    /// <summary>Whether a Ctrl-Tab switch is in progress, as the window's switcher says. The page assumes
    /// one from its own Ctrl+Tab until told otherwise; while it holds, Esc and the Ctrl release are the
    /// switcher's.</summary>
    public Task SetSwitcherActiveAsync(bool active) =>
        _host.ExecuteScriptAsync($"agterm.setSwitcherActive({(active ? "true" : "false")})");

    public Task HideSearchAsync(string? clearOwnerPaneId, string? restorePaneId)
    {
        var payload = JsonSerializer.Serialize(new
        {
            clearOwnerPaneId,
            restorePaneId,
        }, XtermTerminal.ScriptLiteral);
        return _host.ExecuteScriptAsync($"agterm.hideSearch({payload})");
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

    public event Action<Core.Keymap.BuiltinHotkeyEvent>? BuiltinHotkeyPressed;

    public event Action<Core.Keymap.CustomHotkeyEvent>? CustomCommandPressed;

    /// <summary>Raised when the page requests host-side paste for a known pane id.</summary>
    public event Action<string>? PasteRequested;

    /// <summary>A Ctrl+click on a terminal link in a known pane; the host must classify its target.</summary>
    public event Action<string, string, bool>? TerminalLinkRequested;

    /// <summary>A clipboard copy asked for from a pane: the pane id and the text to write. The
    /// page cannot write the clipboard itself - the browser API refuses without a user gesture -
    /// so the host does it.</summary>
    public event Action<string, string>? CopyRequested;

    /// <summary>A mouse selection finished in a known pane, or the pane selected all: the pane id
    /// and the selected text, for the selection buffer.</summary>
    public event Action<string, string>? SelectionCopied;

    /// <summary>A middle-click in a known pane: the pane id and whether the pane has bracketed
    /// paste on.</summary>
    public event Action<string, bool>? SelectionPasteRequested;

    public event Action<string>? PaneFocusChanged;

    public event Action<string, string, double>? SplitResized;

    public event Action<string, string, int, int?>? SearchUpdated;

    public event Action<string, string, int>? SearchResultsChanged;

    public event Action<string>? SearchClosed;

    /// <summary>The user acted on the terminal question the page shows.</summary>
    public event Action<string, Core.Model.AskIntent>? AskIntentReceived;

    /// <summary>The question took the page focus (its id) or lost it (null).</summary>
    public event Action<string?>? AskFocusChanged;

    /// <summary>A Ctrl+click on a HUD link: the URL under the pointer, not yet checked.</summary>
    public event Action<string>? HudLinkRequested;

    /// <summary>The page measured the cell and the pane rectangles of the session it shows: a pane
    /// token of the shown layout, the metrics with the left and right rects of a shown split, and
    /// the rect of a single shown pane, whose role only the caller can tell from the token.</summary>
    public event Action<string, Core.Model.HudSessionMetrics, Core.Model.HudRect?>? HudMetricsReceived;

    public event Action<string>? DashboardHighlighted;

    public event Action<string>? DashboardActivated;

    public event Action? DashboardClosed;

    /// <summary>A Ctrl-Tab switcher key from the terminal page (upstream SessionSwitcher's monitor).</summary>
    public event Action<SessionSwitcherKey>? SwitcherKeyPressed;

    public event Action? KeyboardShortcutsRequested;

    /// <summary>Push the hotkey table every pane intercepts (replaces the previous table; empty
    /// clears the interception). Call after keymap load/reload, on the WebView2 UI thread — a
    /// script call, so nothing dispatched afterwards can outrun the table.</summary>
    public Task SetInterceptChordsAsync(IEnumerable<Core.Keymap.PhysicalChord> chords)
    {
        var items = chords.Select(c => new { ctrl = c.Ctrl, alt = c.Alt, shift = c.Shift, key = c.Key });
        return _host.ExecuteScriptAsync(
            $"agterm.setChords({JsonSerializer.Serialize(items, XtermTerminal.ScriptLiteral)})");
    }

    public Task SetExternalInputOwnerAsync(bool ownsInput) =>
        _host.ExecuteScriptAsync($"agterm.setExternalInputOwner({(ownsInput ? "true" : "false")})");

    public async Task<bool> SetHotkeyManifestAsync(
        Core.Keymap.HotkeyManifest manifest,
        IReadOnlyList<Core.Keymap.PhysicalChord>? zoomPassthroughSingles = null,
        IReadOnlyList<Core.Keymap.PhysicalChord>? dashboardToggleSingles = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Generation < 0)
            throw new ArgumentOutOfRangeException(nameof(manifest));
        if (manifest.LeaderTimeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(manifest));

        var payload = JsonSerializer.Serialize(new
        {
            manifest.Generation,
            manifest.LeaderTimeoutMs,
            manifest.BuiltinSingles,
            manifest.CustomBindings,
            ZoomPassthroughSingles = zoomPassthroughSingles ?? Array.Empty<Core.Keymap.PhysicalChord>(),
            DashboardToggleSingles = dashboardToggleSingles ?? Array.Empty<Core.Keymap.PhysicalChord>(),
        }, RendererSettingsScriptLiteral);
        var raw = await _host.ExecuteScriptAsync($"agterm.setHotkeyManifest({payload})")
            .ConfigureAwait(false);
        return JsonSerializer.Deserialize<bool?>(raw) == true;
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
            WriteLayoutObject(writer, layout);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteLayoutObject(Utf8JsonWriter writer, TerminalLayout layout)
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
            case TerminalLayout.Zoomed zoomed:
                writer.WriteString("type", "zoomed");
                writer.WriteNumber("generation", zoomed.Generation);
                writer.WriteString("paneId", zoomed.PaneId);
                break;
            case TerminalLayout.Dashboard dashboard:
                writer.WriteString("type", "dashboard");
                writer.WriteNumber("generation", dashboard.Generation);
                writer.WriteStartArray("cells");
                foreach (var cell in dashboard.Cells)
                {
                    writer.WriteStartObject();
                    writer.WriteString("paneId", cell.PaneId);
                    writer.WriteString("controlRef", cell.ControlRef);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteString("highlightedControlRef", dashboard.HighlightedControlRef);
                if (dashboard.FontSize is { } fontSize)
                    writer.WriteNumber("fontSize", fontSize);
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
            case TerminalLayout.Layered layered:
                writer.WriteString("type", "layered");
                writer.WriteNumber("generation", layered.Generation);
                writer.WritePropertyName("base");
                WriteLayeredBaseObject(writer, layered.BaseLayout);
                writer.WritePropertyName("layer");
                WriteLayerObject(writer, layered.Layer);
                break;
            default:
                throw new NotSupportedException($"Unknown terminal layout variant: {layout.GetType().FullName}");
        }

        writer.WriteEndObject();
    }

    private static void WriteLayeredBaseObject(Utf8JsonWriter writer, TerminalLayout baseLayout)
    {
        writer.WriteStartObject();
        switch (baseLayout)
        {
            case TerminalLayout.Empty:
                writer.WriteString("type", "empty");
                break;
            case TerminalLayout.Single single:
                writer.WriteString("type", "single");
                writer.WriteString("paneId", single.PaneId);
                break;
            case TerminalLayout.Split split:
                writer.WriteString("type", "split");
                writer.WriteString("leftPaneId", split.LeftPaneId);
                writer.WriteString("rightPaneId", split.RightPaneId);
                writer.WriteNumber("ratio", split.Ratio);
                writer.WriteString("focusedPaneId", split.FocusedPaneId);
                break;
            default:
                throw new NotSupportedException($"Unknown layered base layout variant: {baseLayout.GetType().FullName}");
        }

        writer.WriteEndObject();
    }

    private static void WriteLayerObject(Utf8JsonWriter writer, TerminalLayer layer)
    {
        writer.WriteStartObject();
        writer.WriteString("paneId", layer.PaneId);
        if (layer.SizePercent is { } sizePercent)
            writer.WriteNumber("sizePercent", sizePercent);
        if (layer.BackgroundColor is { } backgroundColor)
            writer.WriteString("backgroundColor", backgroundColor);
        writer.WriteEndObject();
    }

    private static Core.Model.AskIntent? AskIntentFrom(BridgeMessage msg) => msg.Intent switch
    {
        "forward" => new Core.Model.AskIntent.Forward(),
        "backward" => new Core.Model.AskIntent.Backward(),
        "activate" => new Core.Model.AskIntent.Activate(),
        "cancel" => new Core.Model.AskIntent.Cancel(),
        "hotkey" when msg.Letter is { Length: 1 } letter => new Core.Model.AskIntent.Hotkey(letter),
        "press" when msg.Index is { } index => new Core.Model.AskIntent.Press(index),
        _ => null,
    };

    private static (Core.Model.HudSessionMetrics Metrics, Core.Model.HudRect? SinglePane)? HudMetricsFrom(BridgeMessage msg)
    {
        if (!IsPositive(msg.FontSize) || !IsPositive(msg.CellWidth) || !IsPositive(msg.CellHeight)
            || !IsPositive(msg.BaseFontSize)
            || RectFrom(msg.Session) is not { } session
            || !TryOptionalRect(msg.Left, out var left) || !TryOptionalRect(msg.Right, out var right)
            || !TryOptionalRect(msg.Pane, out var pane))
            return null;
        // the page shows either a split or one pane, so a message with both shapes has no single reading
        if (pane is not null && (left is not null || right is not null))
            return null;
        return (new Core.Model.HudSessionMetrics(msg.FontSize!.Value, msg.CellWidth!.Value, msg.CellHeight!.Value,
            session, left, right, msg.BaseFontSize!.Value), pane);
    }

    // a zero cell would size every panel from its padding alone, and a zero probe font scales the cell to nothing
    private static bool IsPositive(double? value) => value is { } v && double.IsFinite(v) && v > 0;

    // zero is allowed: the store reads a zero extent as "not measured"
    private static bool IsExtent(double? value) => value is { } v && double.IsFinite(v) && v >= 0;

    private static Core.Model.HudRect? RectFrom(HudRectMessage? rect) =>
        rect is not null && IsExtent(rect.Width) && IsExtent(rect.Height)
            ? new Core.Model.HudRect(rect.Width!.Value, rect.Height!.Value)
            : null;

    // an absent rect is a pane the page does not show; a present one without both sizes is malformed
    private static bool TryOptionalRect(HudRectMessage? rect, out Core.Model.HudRect? value)
    {
        value = RectFrom(rect);
        return rect is null || value is not null;
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
            var chord = new Core.Keymap.PhysicalChord(
                msg.Ctrl ?? false,
                msg.Alt ?? false,
                msg.Shift ?? false,
                key);
            ChordPressed?.Invoke(chordPane, chord);
            if (msg.Generation is { } builtinGeneration)
                BuiltinHotkeyPressed?.Invoke(new Core.Keymap.BuiltinHotkeyEvent(
                    chordPane,
                    builtinGeneration,
                    chord));
            return;
        }
        if (msg is
            {
                Type: "customHotkey",
                PaneId: { } customPane,
                CustomCommandId: { } commandId,
                Generation: { } generation,
                Key: { } customKey,
            } &&
            _terminals.ContainsKey(customPane) &&
            Guid.TryParseExact(commandId, "D", out var customCommandId))
        {
            CustomCommandPressed?.Invoke(new Core.Keymap.CustomHotkeyEvent(
                customPane,
                customCommandId,
                generation,
                new Core.Keymap.PhysicalChord(
                    msg.Ctrl ?? false,
                    msg.Alt ?? false,
                    msg.Shift ?? false,
                    customKey)));
            return;
        }
        if (msg is { Type: "askIntent", AskId: { } askId } && AskIntentFrom(msg) is { } intent)
        {
            AskIntentReceived?.Invoke(askId, intent);
            return;
        }
        if (msg?.Type == "askFocus")
        {
            AskFocusChanged?.Invoke(msg.AskId);
            return;
        }
        if (msg?.Type == "hudLink")
        {
            if (msg.Url is { Length: > 0 } url)
                HudLinkRequested?.Invoke(url);
            return;
        }
        if (msg?.Type == "terminalLink")
        {
            if (msg.PaneId is { } linkPaneId && _terminals.ContainsKey(linkPaneId) && msg.Url is { Length: > 0 } target)
                TerminalLinkRequested?.Invoke(linkPaneId, target, msg.AllowPathFallback == true);
            return;
        }
        if (msg?.Type == "hudMetrics")
        {
            // no _terminals check: the window maps the token to a session and drops one it does not know
            if (msg.PaneId is { Length: > 0 } hudPaneId && HudMetricsFrom(msg) is { } measured)
                HudMetricsReceived?.Invoke(hudPaneId, measured.Metrics, measured.SinglePane);
            return;
        }
        if (msg?.Type == "pasteRequested")
        {
            if (msg.PaneId is { } pastePaneId && _terminals.ContainsKey(pastePaneId))
                PasteRequested?.Invoke(pastePaneId);
            return;
        }

        if (msg?.Type == "copyRequested")
        {
            if (msg.PaneId is { } copyPaneId
                && _terminals.ContainsKey(copyPaneId)
                && msg.Text is { Length: > 0 } copyText)
                CopyRequested?.Invoke(copyPaneId, copyText);
            return;
        }

        if (msg?.Type == "selectionCopied")
        {
            if (msg.PaneId is { } selectedPaneId
                && _terminals.ContainsKey(selectedPaneId)
                && msg.Text is { Length: > 0 } selectedText)
                SelectionCopied?.Invoke(selectedPaneId, selectedText);
            return;
        }

        if (msg?.Type == "selectionPasteRequested")
        {
            if (msg.PaneId is { } middlePaneId && _terminals.ContainsKey(middlePaneId))
                SelectionPasteRequested?.Invoke(middlePaneId, msg.Bracketed == true);
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

        if (msg is { Type: "searchUpdated", PaneId: { } searchPaneId, Needle: { } needle }
            && _terminals.ContainsKey(searchPaneId))
        {
            SearchUpdated?.Invoke(searchPaneId, needle, Math.Max(0, msg.Count ?? 0), msg.Selected);
            return;
        }

        if (msg is { Type: "searchResultsChanged", PaneId: { } resultsPaneId, Needle: { } resultsNeedle, Count: >= 0 }
            && _terminals.ContainsKey(resultsPaneId))
        {
            SearchResultsChanged?.Invoke(resultsPaneId, resultsNeedle, msg.Count.Value);
            return;
        }

        if (msg is { Type: "searchClosed", PaneId: { } closedSearchPaneId }
            && _terminals.ContainsKey(closedSearchPaneId))
        {
            SearchClosed?.Invoke(closedSearchPaneId);
            return;
        }

        if (msg is { Type: "dashboardHighlight", ControlRef: { Length: > 0 } highlightedControlRef })
        {
            DashboardHighlighted?.Invoke(highlightedControlRef);
            return;
        }

        if (msg is { Type: "dashboardActivate", ControlRef: { Length: > 0 } activatedControlRef })
        {
            DashboardActivated?.Invoke(activatedControlRef);
            return;
        }

        if (msg?.Type == "dashboardClose")
        {
            DashboardClosed?.Invoke();
            return;
        }

        if (msg?.Type == "keyboardShortcuts")
        {
            KeyboardShortcutsRequested?.Invoke();
            return;
        }

        if (msg is { Type: "switcher", Key: { } switcherKey })
        {
            SessionSwitcherKey? parsed = switcherKey switch
            {
                "tab" => SessionSwitcherKey.Tab,
                "reverseTab" => SessionSwitcherKey.ReverseTab,
                "escape" => SessionSwitcherKey.Escape,
                "controlReleased" => SessionSwitcherKey.ControlReleased,
                _ => null,
            };
            if (parsed is { } pressed)
                SwitcherKeyPressed?.Invoke(pressed);
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
    [JsonPropertyName("identifier")] public int? Identifier { get; init; }
    [JsonPropertyName("focused")] public bool? Focused { get; init; }
    [JsonPropertyName("oversized")] public bool? Oversized { get; init; }
    [JsonPropertyName("cols")] public int? Cols { get; init; }
    [JsonPropertyName("rows")] public int? Rows { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("uri")] public string? Uri { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
    [JsonPropertyName("allowPathFallback")] public bool? AllowPathFallback { get; init; }
    [JsonPropertyName("ctrl")] public bool? Ctrl { get; init; }
    [JsonPropertyName("alt")] public bool? Alt { get; init; }
    [JsonPropertyName("shift")] public bool? Shift { get; init; }
    [JsonPropertyName("key")] public string? Key { get; init; }
    [JsonPropertyName("code")] public string? Code { get; init; }
    [JsonPropertyName("meta")] public bool? Meta { get; init; }
    [JsonPropertyName("generation")] public long? Generation { get; init; }
    [JsonPropertyName("customCommandId")] public string? CustomCommandId { get; init; }
    [JsonPropertyName("leftPaneId")] public string? LeftPaneId { get; init; }
    [JsonPropertyName("rightPaneId")] public string? RightPaneId { get; init; }
    [JsonPropertyName("ratio")] public double? Ratio { get; init; }
    [JsonPropertyName("text")] public string? Text { get; init; }
    [JsonPropertyName("bracketed")] public bool? Bracketed { get; init; }
    [JsonPropertyName("needle")] public string? Needle { get; init; }
    [JsonPropertyName("count")] public int? Count { get; init; }
    [JsonPropertyName("selected")] public int? Selected { get; init; }
    [JsonPropertyName("askId")] public string? AskId { get; init; }
    [JsonPropertyName("intent")] public string? Intent { get; init; }
    [JsonPropertyName("letter")] public string? Letter { get; init; }
    [JsonPropertyName("index")] public int? Index { get; init; }
    [JsonPropertyName("controlRef")] public string? ControlRef { get; init; }
    [JsonPropertyName("fontSize")] public double? FontSize { get; init; }
    [JsonPropertyName("baseFontSize")] public double? BaseFontSize { get; init; }
    [JsonPropertyName("cellWidth")] public double? CellWidth { get; init; }
    [JsonPropertyName("cellHeight")] public double? CellHeight { get; init; }
    [JsonPropertyName("session")] public HudRectMessage? Session { get; init; }
    [JsonPropertyName("left")] public HudRectMessage? Left { get; init; }
    [JsonPropertyName("right")] public HudRectMessage? Right { get; init; }
    [JsonPropertyName("pane")] public HudRectMessage? Pane { get; init; }
}

/// <summary>A width and height in CSS pixels from the page's hudMetrics message.</summary>
internal sealed record HudRectMessage
{
    [JsonPropertyName("width")] public double? Width { get; init; }
    [JsonPropertyName("height")] public double? Height { get; init; }
}

/// <summary>A TerminalTheme under the renderer settings' key names, which the page spreads over
/// its global settings for one pane.</summary>
internal sealed record PaneThemePayload(
    string ForegroundColor,
    string BackgroundColor,
    string CursorColor,
    string CursorTextColor,
    string SelectionBackgroundColor,
    string SelectionForegroundColor,
    IReadOnlyList<string> AnsiColors)
{
    public static PaneThemePayload From(TerminalTheme theme) => new(
        theme.ForegroundColor,
        theme.BackgroundColor,
        theme.CursorColor,
        theme.CursorTextColor,
        theme.SelectionBackgroundColor,
        theme.SelectionForegroundColor,
        theme.AnsiColors);
}
