using System.Text.Json.Serialization;

namespace AgTerm.Core.Protocol;

/// <summary>
/// One control request: a command, an optional target (session or workspace id / "active" / prefix),
/// and an optional args bag. One request per connection, newline-delimited JSON.
/// </summary>
public sealed record ControlRequest
{
    [JsonPropertyName("cmd")]
    public required Command Cmd { get; init; }

    [JsonPropertyName("target")]
    public string? Target { get; init; }

    [JsonPropertyName("args")]
    public ControlArgs? Args { get; init; }
}

/// <summary>The single response written back per connection. Ok gates result vs error.</summary>
public sealed record ControlResponse
{
    [JsonPropertyName("ok")]
    public required bool Ok { get; init; }

    [JsonPropertyName("result")]
    public ControlResult? Result { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

/// <summary>The successful payload. All fields optional; only the ones the command produced are set.</summary>
public sealed record ControlResult
{
    /// <summary>Mutations return the new/affected id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The whole workspace tree for the tree command.</summary>
    [JsonPropertyName("tree")]
    public ControlTree? Tree { get; init; }

    /// <summary>Selected text (session.copy), buffer text (session.text / quick.text),
    /// "N of M" (session.search), or an advisory note (notify with banners off).</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>window.list payload.</summary>
    [JsonPropertyName("windows")]
    public ControlWindowNode[]? Windows { get; init; }

    /// <summary>The overlay program's exit status for session.overlay.result.</summary>
    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; init; }

    /// <summary>Diagnostic count (keymap.reload / config.reload) or total match count (session.search).</summary>
    [JsonPropertyName("count")]
    public int? Count { get; init; }

    /// <summary>Number of sessions actually changed by a batch mutation (session.close / session.move).</summary>
    [JsonPropertyName("affected")]
    public int? Affected { get; init; }

    /// <summary>The current/affected single theme name for theme.set (echo) and theme.list (current).</summary>
    [JsonPropertyName("theme")]
    public string? Theme { get; init; }

    /// <summary>The available bundled theme names for theme.list.</summary>
    [JsonPropertyName("themes")]
    public string[]? Themes { get; init; }

    /// <summary>The applied (clamped) left-pane split fraction echoed by session.resize.</summary>
    [JsonPropertyName("ratio")]
    public double? Ratio { get; init; }

    /// <summary>Light/dark syncing state for theme.set/theme.list; while syncing, theme is absent
    /// and the state rides sync/light/dark.</summary>
    [JsonPropertyName("sync")]
    public bool? Sync { get; init; }

    [JsonPropertyName("light")]
    public string? Light { get; init; }

    [JsonPropertyName("dark")]
    public string? Dark { get; init; }

    /// <summary>A page from the app-run event ring, present for events.read success and cursor errors.</summary>
    [JsonPropertyName("events")]
    public ControlEventBatch? Events { get; init; }
}

/// <summary>
/// Error strings for session.overlay.result, shared so a CLI --block poll matches the server's
/// wording exactly (the poll retries by error string while the overlay is still running).
/// </summary>
public static class OverlayResultError
{
    public const string StillRunning = "overlay still running";
    public const string NoResult = "no overlay result";
}

/// <summary>
/// Advisory text notify returns in result.text when the banner toggle is off; shared so the
/// server's wording and any caller matching on it cannot drift.
/// </summary>
public static class ControlNotify
{
    public const string BannersOffNote =
        "badge updated, but \"Show notification banners\" is off, so no banner was posted";
}
