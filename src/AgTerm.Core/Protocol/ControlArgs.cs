using System.Text.Json.Serialization;

namespace AgTerm.Core.Protocol;

/// <summary>
/// A bag of optional command parameters. Each command reads only the fields it needs; the rest stay
/// null and are omitted from the JSON, keeping the wire form compact. Property declaration order
/// matches the Swift struct so the encoded field order is identical.
/// </summary>
public sealed record ControlArgs
{
    /// <summary>New name for workspace.new/rename, session.rename; initial session name for
    /// session.new; theme name for theme.set.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Working directory for session.new.</summary>
    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    /// <summary>Ordered session targets for batch-capable commands (session.close, session.move);
    /// also the explicit pane ids for dashboard.</summary>
    [JsonPropertyName("targets")]
    public string[]? Targets { get; init; }

    /// <summary>Target workspace (id / unique prefix / "active") for session.new and session.move.</summary>
    [JsonPropertyName("workspace")]
    public string? Workspace { get; init; }

    /// <summary>Target workspace BY NAME for session.new (mutually exclusive with workspace).</summary>
    [JsonPropertyName("workspaceName")]
    public string? WorkspaceName { get; init; }

    /// <summary>With workspaceName: create the named workspace when none exists (reuse-or-create).</summary>
    [JsonPropertyName("createWorkspace")]
    public bool? CreateWorkspace { get; init; }

    /// <summary>For workspace.new: create the workspace already collapsed in the sidebar.</summary>
    [JsonPropertyName("collapsed")]
    public bool? Collapsed { get; init; }

    /// <summary>For session.new: create in the background without selecting or focusing.</summary>
    [JsonPropertyName("noSelect")]
    public bool? NoSelect { get; init; }

    /// <summary>Text to inject for session.type / quick.type; the search needle for session.search.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>Whether session.type may select a never-shown session to realize its surface.</summary>
    [JsonPropertyName("select")]
    public bool? Select { get; init; }

    /// <summary>Mode for the mode-bearing commands (session.split on|off|toggle, session.flag
    /// on|off|toggle|clear, sidebar.mode tree|flagged|toggle, session.background image|text|color|clear,
    /// session.restore set|none|clear, ...).</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    /// <summary>The image file path for session.background mode image (PNG or JPEG).</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>#rrggbb color: watermark text tint / solid background / overlay background /
    /// per-call status glyph tint.</summary>
    [JsonPropertyName("color")]
    public string? Color { get; init; }

    /// <summary>Per-call glyph silhouette for session.status
    /// (circle|square|triangle|diamond|capsule|star).</summary>
    [JsonPropertyName("shape")]
    public string? Shape { get; init; }

    /// <summary>Watermark opacity 0..1 for session.background (image + text); null = engine 1.0.</summary>
    [JsonPropertyName("opacity")]
    public double? Opacity { get; init; }

    /// <summary>Watermark fit (contain|cover|stretch|none); null = contain.</summary>
    [JsonPropertyName("fit")]
    public string? Fit { get; init; }

    /// <summary>Watermark position (center + 8 anchors); null = center.</summary>
    [JsonPropertyName("position")]
    public string? Position { get; init; }

    /// <summary>Watermark tiling flag; null = false.</summary>
    [JsonPropertyName("repeats")]
    public bool? Repeats { get; init; }

    /// <summary>Pane selector: session.focus left|right|other; session.text/type/status/restore
    /// pane addressing.</summary>
    [JsonPropertyName("pane")]
    public string? Pane { get; init; }

    /// <summary>A surface's stable spawn token for session.status/session.restore --pane-id; when it
    /// resolves it overrides the stale role pane.</summary>
    [JsonPropertyName("paneID")]
    public string? PaneId { get; init; }

    /// <summary>Absolute left-pane split fraction (0..1) for session.resize; clamped server-side.
    /// Mutually exclusive with ratioDelta.</summary>
    [JsonPropertyName("ratio")]
    public double? Ratio { get; init; }

    /// <summary>Signed relative divider nudge for session.resize (positive grows the left pane).
    /// Mutually exclusive with ratio.</summary>
    [JsonPropertyName("ratioDelta")]
    public double? RatioDelta { get; init; }

    /// <summary>For session.text / quick.text: read the full screen + scrollback.</summary>
    [JsonPropertyName("all")]
    public bool? All { get; init; }

    /// <summary>For session.text / quick.text: keep only the last N lines.</summary>
    [JsonPropertyName("lines")]
    public int? Lines { get; init; }

    /// <summary>Direction for session.go (next|prev|first|last|*-attention), reorder for
    /// session.move / workspace.move (up|down|top|bottom), and session.search (next|prev|close).</summary>
    [JsonPropertyName("to")]
    public string? To { get; init; }

    /// <summary>Anchor session to place right AFTER (placement form of session.new / session.move).</summary>
    [JsonPropertyName("after")]
    public string? After { get; init; }

    /// <summary>Anchor session to place right BEFORE, the mirror of after.</summary>
    [JsonPropertyName("before")]
    public string? Before { get; init; }

    /// <summary>App-run UUID paired with after for events.read; both omitted = bootstrap read.</summary>
    [JsonPropertyName("run")]
    public string? Run { get; init; }

    /// <summary>Raw event-kind filters for events.read; validated in the dispatcher so unknown future
    /// kinds produce a normal control error, not a decode failure.</summary>
    [JsonPropertyName("kinds")]
    public string[]? Kinds { get; init; }

    /// <summary>Maximum matching events returned by events.read; null = dispatcher default.</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    /// <summary>Desktop-notification title for notify (defaults to the target session's name).</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>Desktop-notification body for notify (required by the dispatcher).</summary>
    [JsonPropertyName("body")]
    public string? Body { get; init; }

    /// <summary>The program for session.overlay.open / session.new --command; also the shell line
    /// session.restore pins (mode set).</summary>
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    /// <summary>Keep the surface open after the command exits (session.overlay.open --wait,
    /// session.new --command --wait).</summary>
    [JsonPropertyName("wait")]
    public bool? Wait { get; init; }

    /// <summary>Floating overlay size (1..100 percent) for session.overlay.open/resize.</summary>
    [JsonPropertyName("sizePercent")]
    public int? SizePercent { get; init; }

    /// <summary>For session.overlay.resize: back to the full-pane overlay. Mutually exclusive with
    /// sizePercent.</summary>
    [JsonPropertyName("full")]
    public bool? Full { get; init; }

    /// <summary>For session.overlay.open: select the target after opening (default = background).</summary>
    [JsonPropertyName("follow")]
    public bool? Follow { get; init; }

    /// <summary>pick.open choices. An empty array is sent as [] (an itemless prompt with allowCustom).</summary>
    [JsonPropertyName("items")]
    public ControlPickItem[]? Items { get; init; }

    /// <summary>pick.open placeholder of the query field.</summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    /// <summary>pick.open initial text of the query field.</summary>
    [JsonPropertyName("query")]
    public string? Query { get; init; }

    /// <summary>pick.open: accept an unmatched non-blank query as a custom answer.</summary>
    [JsonPropertyName("allowCustom")]
    public bool? AllowCustom { get; init; }

    /// <summary>pick.open item id highlighted on open (CLI --select). Its own field because select is
    /// session.type's Bool.</summary>
    [JsonPropertyName("selection")]
    public string? Selection { get; init; }

    /// <summary>Target window (id / prefix / "active" = frontmost) for session/workspace/tree/font
    /// commands.</summary>
    [JsonPropertyName("window")]
    public string? Window { get; init; }

    /// <summary>New window frame width in points for window.resize.</summary>
    [JsonPropertyName("width")]
    public int? Width { get; init; }

    /// <summary>New window frame height in points for window.resize.</summary>
    [JsonPropertyName("height")]
    public int? Height { get; init; }

    /// <summary>New window top-left x for window.move, relative to the target display's top-left.</summary>
    [JsonPropertyName("x")]
    public int? X { get; init; }

    /// <summary>New window top-left y for window.move (measured down from the display's top edge).</summary>
    [JsonPropertyName("y")]
    public int? Y { get; init; }

    /// <summary>Target display index for window.move; null = the window's current display.</summary>
    [JsonPropertyName("display")]
    public int? Display { get; init; }

    /// <summary>Agent state for session.status (idle|active|completed|blocked).</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>Whether the session.status indicator pulses for attention.</summary>
    [JsonPropertyName("blink")]
    public bool? Blink { get; init; }

    /// <summary>Whether the session.status indicator resets to idle once the session is visited.</summary>
    [JsonPropertyName("autoReset")]
    public bool? AutoReset { get; init; }

    /// <summary>One-shot sound when session.status is set: default/beep = system alert, other =
    /// a named system sound; unknown name errors without changing the status.</summary>
    [JsonPropertyName("sound")]
    public string? Sound { get; init; }

    /// <summary>The light/single theme slot for theme.set (alias of name; both at once is an error).</summary>
    [JsonPropertyName("light")]
    public string? Light { get; init; }

    /// <summary>The dark theme slot for theme.set; its presence enables appearance tracking,
    /// the reserved "none" clears it.</summary>
    [JsonPropertyName("dark")]
    public string? Dark { get; init; }

    /// <summary>Whether dashboard closes the open dashboard (mutually exclusive with everything else).</summary>
    [JsonPropertyName("close")]
    public bool? Close { get; init; }

    /// <summary>Absolute dashboard cell font size in points; must be positive. Mutually exclusive
    /// with autoSize.</summary>
    [JsonPropertyName("fontSize")]
    public double? FontSize { get; init; }

    /// <summary>Size dashboard cells relative to the default font, shrinking as the grid grows.
    /// Mutually exclusive with fontSize.</summary>
    [JsonPropertyName("autoSize")]
    public bool? AutoSize { get; init; }

    /// <summary>Populate the dashboard from the window's MRU sessions instead of explicit ids.</summary>
    [JsonPropertyName("mru")]
    public bool? Mru { get; init; }
}
