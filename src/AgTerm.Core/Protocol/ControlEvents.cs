using System.Text.Json.Serialization;

namespace AgTerm.Core.Protocol;

/// <summary>Event kinds retained by the app-run control event ring.</summary>
[JsonConverter(typeof(ControlEventKindConverter))]
public enum ControlEventKind
{
    Status,
    Notify,
    SessionCreated,
    SessionClosed,
    /// <summary>The window's selection changed, whoever made it. A cleared selection carries no session.</summary>
    SessionSelected,
    /// <summary>A coalesced signal of structural tree changes.</summary>
    TreeChanged,
    /// <summary>The split pane was shown or hidden (payload status shown/hidden).</summary>
    PaneSplit,
    /// <summary>The scratch terminal was shown or hidden (payload status shown/hidden).</summary>
    PaneScratch,
    /// <summary>The selected pane of a split session settled on another pane.</summary>
    PaneFocused,
    /// <summary>Parse-only: the port has no remote sessions, so these are never emitted.</summary>
    RemoteOpened,
    RemoteClosed,
    /// <summary>Once per app run, after the control pipe accepts connections.</summary>
    AppLaunched,
    /// <summary>A bus message was queued for a pane: payload paneID, from and messageID, never the body.</summary>
    Message,
}

public static class ControlEventKinds
{
    internal static readonly (ControlEventKind Kind, string Wire)[] Map =
    {
        (ControlEventKind.Status, "status"),
        (ControlEventKind.Notify, "notify"),
        (ControlEventKind.SessionCreated, "session.created"),
        (ControlEventKind.SessionClosed, "session.closed"),
        (ControlEventKind.SessionSelected, "session.selected"),
        (ControlEventKind.TreeChanged, "tree.changed"),
        (ControlEventKind.PaneSplit, "pane.split"),
        (ControlEventKind.PaneScratch, "pane.scratch"),
        (ControlEventKind.PaneFocused, "pane.focused"),
        (ControlEventKind.RemoteOpened, "remote.opened"),
        (ControlEventKind.RemoteClosed, "remote.closed"),
        (ControlEventKind.AppLaunched, "app.launched"),
        (ControlEventKind.Message, "message"),
    };

    /// <summary>Every kind, in wire declaration order (the Swift allCases).</summary>
    public static IReadOnlyList<ControlEventKind> All { get; } = Map.Select(p => p.Kind).ToArray();

    /// <summary>The Swift raw-value parse: the wire token, or null for an unknown string.</summary>
    public static ControlEventKind? FromWire(string wire)
    {
        foreach (var (kind, token) in Map)
            if (string.Equals(token, wire, StringComparison.Ordinal)) return kind;
        return null;
    }

    public static string ToWire(this ControlEventKind kind) =>
        Map.First(p => p.Kind == kind).Wire;
}

public sealed class ControlEventKindConverter : WireEnumConverter<ControlEventKind>
{
    public ControlEventKindConverter() : base(ControlEventKinds.Map)
    {
    }
}
/// <summary>
/// Kind-specific event data. Optional fields keep the encoded payload compact while preserving one
/// stable object shape for shell/JSON consumers.
/// </summary>
public sealed record ControlEventPayload
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("pane")]
    public string? Pane { get; init; }

    [JsonPropertyName("blink")]
    public bool? Blink { get; init; }

    [JsonPropertyName("color")]
    public string? Color { get; init; }

    /// <summary>The status event's per-call glyph silhouette. A shape-only change is a real indicator
    /// change, so it DOES emit a status event — without this field a consumer could not explain it.</summary>
    [JsonPropertyName("shape")]
    public string? Shape { get; init; }

    /// <summary>The status event's status before the accepted write; the session.selected event's
    /// previously selected session id. Equal to status when only shape, color, pane or blink changed.</summary>
    [JsonPropertyName("previous")]
    public string? Previous { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("body")]
    public string? Body { get; init; }

    /// <summary>The remote.* host. Parse-only here, the port has no remote sessions.</summary>
    [JsonPropertyName("host")]
    public string? Host { get; init; }

    /// <summary>The pane.focused pane's surface token, the value tree reports as surfaces[].paneID;
    /// absent while that pane's surface does not exist yet.</summary>
    [JsonPropertyName("paneID")]
    public string? PaneId { get; init; }

    /// <summary>The message event's sender address, absent for a sender outside agterm.</summary>
    [JsonPropertyName("from")]
    public string? From { get; init; }

    /// <summary>The message event's message id; the body is never carried.</summary>
    [JsonPropertyName("messageID")]
    public string? MessageId { get; init; }
}

/// <summary>One entry in the event ring.</summary>
public sealed record ControlEvent
{
    [JsonPropertyName("seq")]
    public required ulong Seq { get; init; }

    /// <summary>Unix timestamp (seconds, fractional).</summary>
    [JsonPropertyName("ts")]
    public required double Ts { get; init; }

    [JsonPropertyName("kind")]
    public required ControlEventKind Kind { get; init; }

    [JsonPropertyName("window")]
    public string? Window { get; init; }

    [JsonPropertyName("workspace")]
    public string? Workspace { get; init; }

    [JsonPropertyName("session")]
    public string? Session { get; init; }

    /// <summary>Always encoded, even when empty — one stable object shape.</summary>
    [JsonPropertyName("payload")]
    public ControlEventPayload Payload { get; set; } = new();
}

/// <summary>
/// A page returned by a ring read. Next is the global sequence through which the ring scanned,
/// including filtered-out entries — pass it back as the following after.
/// </summary>
public sealed record ControlEventBatch
{
    [JsonPropertyName("run")]
    [JsonConverter(typeof(UppercaseGuidConverter))]
    public required Guid Run { get; init; }

    [JsonPropertyName("next")]
    public required ulong Next { get; init; }

    [JsonPropertyName("items")]
    public required ControlEvent[] Items { get; init; }
}

/// <summary>Cursor failures that require the caller to choose whether to rebaseline. Never
/// rebaselined silently.</summary>
public enum ControlEventReadError
{
    RunChanged,
    CursorExpired,
    CursorAhead,
}

public static class ControlEventReadErrorMessages
{
    /// <summary>The wire error string, matched verbatim against upstream.</summary>
    public static string Message(this ControlEventReadError error) => error switch
    {
        ControlEventReadError.RunChanged => "event run changed",
        ControlEventReadError.CursorExpired => "event cursor expired",
        ControlEventReadError.CursorAhead => "event cursor is ahead of the current sequence",
        _ => throw new ArgumentOutOfRangeException(nameof(error)),
    };
}

/// <summary>Stable dispatcher validation errors shared with CLI and socket tests.</summary>
public static class ControlEventRequestError
{
    public const string CursorPair = "events.read requires --run and --after together";
    public const string InvalidCursor = "invalid event cursor";
    public const string InvalidRun = "invalid event run id";
    public const string InvalidLimit = "event limit must be between 1 and 1000";

    public static string InvalidKind(string kind) => $"invalid event kind: {kind}";
}
