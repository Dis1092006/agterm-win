using System.Text.Json.Serialization;
using AgTerm.Core.Model;

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

    /// <summary>Applied window frame width in integer points, echoed by window.resize.</summary>
    [JsonPropertyName("width")]
    public int? Width { get; init; }

    /// <summary>Applied window frame height in integer points, echoed by window.resize.</summary>
    [JsonPropertyName("height")]
    public int? Height { get; init; }

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

    /// <summary>The stored (clamped) sidebar width in points echoed by sidebar.width.</summary>
    [JsonPropertyName("sidebarWidth")]
    public double? SidebarWidth { get; init; }

    /// <summary>The resolved pane role (left or right) echoed by ask.open for a pane anchor.</summary>
    [JsonPropertyName("pane")]
    public string? Pane { get; init; }

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

    /// <summary>keymap.list payload.</summary>
    [JsonPropertyName("keymap")]
    public ControlKeymap? Keymap { get; init; }

    /// <summary>hooks.list payload.</summary>
    [JsonPropertyName("hooks")]
    public ControlHooks? Hooks { get; init; }

    /// <summary>pick.result payload.</summary>
    [JsonPropertyName("pick")]
    public ControlPickResult? Pick { get; init; }

    /// <summary>ask.result payload.</summary>
    [JsonPropertyName("ask")]
    public ControlAskResult? Ask { get; init; }

    /// <summary>The addressed surface's cursor position for surface.cursor.</summary>
    [JsonPropertyName("cursor")]
    public ControlCursor? Cursor { get; init; }

    /// <summary>The running app's identity for version.</summary>
    [JsonPropertyName("app")]
    public ControlAppIdentity? App { get; init; }

    /// <summary>The identity of the app on disk for version; absent when it equals the running one
    /// or cannot be read.</summary>
    [JsonPropertyName("installed")]
    public ControlAppIdentity? Installed { get; init; }

    /// <summary>session.overlay.open --html: the id of the opened page.</summary>
    [JsonPropertyName("pageID")]
    public string? PageId { get; init; }

    /// <summary>recent.list payload: the recently closed entries, newest first.</summary>
    [JsonPropertyName("recent")]
    public ControlRecentItem[]? Recent { get; init; }

    /// <summary>recent.open payload: the reopened entry, its window and whether it was consumed.</summary>
    [JsonPropertyName("recentOpen")]
    public ControlRecentOpen? RecentOpen { get; init; }

    /// <summary>session.close --undoable: whether the close went into the undo grace window.</summary>
    [JsonPropertyName("undoable")]
    public bool? Undoable { get; init; }

    /// <summary>The id every copy of a msg.send shares.</summary>
    [JsonPropertyName("messageID")]
    public string? MessageId { get; init; }

    /// <summary>msg.send's per-recipient results.</summary>
    [JsonPropertyName("delivered")]
    public IReadOnlyList<string>? Delivered { get; init; }

    [JsonPropertyName("rejected")]
    public IReadOnlyList<MessageRejection>? Rejected { get; init; }

    /// <summary>The oldest of the reader's mailbox; remaining counts the pending ones left out.</summary>
    [JsonPropertyName("messages")]
    public IReadOnlyList<BusMessage>? Messages { get; init; }

    [JsonPropertyName("remaining")]
    public int? Remaining { get; init; }

    /// <summary>The messages removed, present on every msg.ack and msg.read --ack reply, empty included.</summary>
    [JsonPropertyName("acked")]
    public IReadOnlyList<string>? Acked { get; init; }

    /// <summary>The icon names workspace.icon accepts, for icon.list. Windows-only
    /// (parity/windows-only.json); declared last so the upstream fields keep upstream order.</summary>
    [JsonPropertyName("icons")]
    public string[]? Icons { get; init; }
}

/// <summary>surface.cursor's payload, nested so a row could join it additively. Column is zero-based,
/// counted from the left edge of the grid.</summary>
public sealed record ControlCursor
{
    [JsonPropertyName("column")]
    public required int Column { get; init; }
}

/// <summary>An app build identity: a version and an optional commit.</summary>
public sealed record ControlAppIdentity
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("commit")]
    public string? Commit { get; init; }
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

/// <summary>Error strings for session.overlay.hide|show|toggle and for opening into a slot whose
/// overlay is hidden (upstream OverlayVisibilityError).</summary>
public static class OverlayVisibilityError
{
    /// <summary>The slot still holds its overlay, running; show or close it before opening another.</summary>
    public const string Hidden = "overlay hidden";

    public const string NoOverlay = "no overlay";

    public static string Message(OverlayVisibilityFailure failure) => failure switch
    {
        OverlayVisibilityFailure.UnknownSession => "no such session",
        _ => NoOverlay,
    };
}

/// <summary>Error strings for session.overlay.open/.resize --width/--height (upstream
/// OverlayPointError). Upstream's pane and remote cases are absent: the port has neither.</summary>
public static class OverlayPointError
{
    public const string NotPositive = "session.overlay: --width/--height must be positive";
    public const string NeedsPercent = "session.overlay.open: --width/--height need --size-percent";
    public const string FullConflict = "session.overlay.resize: --full is mutually exclusive with --width/--height";
    public const string NotFloating =
        "session.overlay.resize: --width/--height need a floating overlay; pass --size-percent too";
    public const string Hud = "a hud measures its own height: --width/--height do not apply";

    public static string Message(OverlayPointResizeFailure failure) => failure switch
    {
        OverlayPointResizeFailure.Hud => Hud,
        OverlayPointResizeFailure.NotFloating => NotFloating,
        _ => OverlayVisibilityError.NoOverlay,
    };
}

/// <summary>The rejection of a --pane selector on session.overlay.* (upstream PaneOverlayError).</summary>
public static class PaneOverlayError
{
    public const string InvalidPane = "session.overlay: --pane must be left or right";
}

/// <summary>Error strings for a page in the overlay slot (upstream OverlayHtmlError, 11fe64eb).
/// Only the file-page cases the port has taken are here.</summary>
public static class OverlayHtmlError
{
    public const string CommandAndHtml = "session.overlay.open takes a command or --html, not both";
    public const string WaitWithHtml = "session.overlay.open: --wait cannot be combined with --html";
    public const string NavigationWithoutPage = "session.overlay.open: --navigation requires --html or --url";
    public const string JavascriptWithoutPage = "session.overlay.open: --js requires --html or --url";
    public const string NoOverlay = "no overlay";
    public const string NotHtml = "the overlay is not an html page";
    public const string Navigation = "session.overlay.navigate requires back, forward, browser or finder";
    public const string FinderRequiresFile = "show in Finder requires a file page";
    public const string NotRealized = "html overlay not realized";
    public const string NoBrowser = "no default web browser to open the page in";
    public const string NoResult = "no overlay result: the slot holds an html page";

    /// <summary>The live app's answer to --html until it renders pages. Windows-only.</summary>
    public const string Unsupported = "html overlays are not supported yet";

    public static string NoHistory(HtmlNavigation navigation) =>
        $"no page to go {(navigation == HtmlNavigation.Forward ? "forward" : "back")} to";
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
