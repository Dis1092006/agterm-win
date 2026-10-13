using System.Text.Json.Serialization;

namespace AgTerm.Core.Protocol;

/// <summary>The message panel in a session's overlay slot, as tree reports it (upstream
/// ControlHudNode). Present only while a HUD is up; the session node's overlay reads false beside
/// it. HUD state is poll-only: no event announces it.</summary>
public sealed record ControlHudNode
{
    /// <summary>As the caller sent it, not normalized.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>Omitted when the caller set none; "" when the caller sent "".</summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    /// <summary>The style name, or none for a static panel. Always present, so a caller reads one
    /// field rather than inferring absence.</summary>
    [JsonPropertyName("spinner")]
    public required string Spinner { get; init; }

    /// <summary>The open's color, verbatim; an update cannot change it.</summary>
    [JsonPropertyName("backgroundColor")]
    public string? BackgroundColor { get; init; }

    /// <summary>The latest open or update's text color, verbatim.</summary>
    [JsonPropertyName("textColor")]
    public string? TextColor { get; init; }

    /// <summary>The width the panel took: measured or the caller's override, both clamped, or the
    /// forced width after session.overlay.resize.</summary>
    [JsonPropertyName("sizePercent")]
    public int? SizePercent { get; init; }

    /// <summary>The measured height; no command sets it.</summary>
    [JsonPropertyName("heightPercent")]
    public int? HeightPercent { get; init; }

    /// <summary>The canonical anchor; an alias the caller sent reads back as its anchor.</summary>
    [JsonPropertyName("position")]
    public required string Position { get; init; }

    /// <summary>The current role of the anchored pane; omitted for a session-wide panel.</summary>
    [JsonPropertyName("pane")]
    public string? Pane { get; init; }

    /// <summary>The configured seconds, 0 when the panel stays; not the time left.</summary>
    [JsonPropertyName("hideAfter")]
    public required double HideAfter { get; init; }

    [JsonPropertyName("markdown")]
    public bool Markdown { get; init; }

    /// <summary>The open's point size; omitted when the panel inherits the session's.</summary>
    [JsonPropertyName("fontSize")]
    public double? FontSize { get; init; }

    [JsonPropertyName("sticky")]
    public bool Sticky { get; init; }

    /// <summary>Upstream decodes an absent key as true.</summary>
    [JsonPropertyName("frame")]
    public bool Frame { get; init; } = true;
}

/// <summary>HUD error strings shared by the hosts; part of the wire contract.</summary>
public static class HudErrors
{
    public const string NoHud = "no hud";
    public const string FullResize = "a hud is always floating: pass --size-percent, not --full";
    public const string NoOverlayResult = "no overlay result: the slot holds a hud";
    public const string InvalidPane = "hud pane must be left or right";
    public const string TextControlCharacters = "hud text must not contain control characters";
}
