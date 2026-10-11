using System.Text.Json.Serialization;

namespace AgTerm.Core.Protocol;

/// <summary>One caller-provided button of an ask dialog, in caller order.</summary>
public sealed record ControlAskButton
{
    public const int MaxButtons = 6;

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

    /// <summary>One ASCII letter, stored lower-case by the dispatcher.</summary>
    [JsonPropertyName("hotkey")]
    public string? Hotkey { get; init; }
}

/// <summary>The state or terminal outcome ask.result reports.</summary>
[JsonConverter(typeof(ControlAskOutcomeConverter))]
public enum ControlAskOutcome
{
    Pending,
    Answered,
    Escaped,
    Cancelled,
}

public sealed class ControlAskOutcomeConverter : WireEnumConverter<ControlAskOutcome>
{
    public ControlAskOutcomeConverter() : base(
        (ControlAskOutcome.Pending, "pending"),
        (ControlAskOutcome.Answered, "answered"),
        (ControlAskOutcome.Escaped, "escaped"),
        (ControlAskOutcome.Cancelled, "cancelled"))
    {
    }
}

/// <summary>The nested ask.result payload. escaped is the user's dismissal, cancelled an
/// administrative end (ask.cancel, owner teardown). Upstream's reason field carries only
/// presentation-lost, which belongs to remote presentation and is not ported.</summary>
public sealed record ControlAskResult
{
    [JsonPropertyName("result")]
    public required ControlAskOutcome Result { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("index")]
    public int? Index { get; init; }

    public static ControlAskResult Pending() => new() { Result = ControlAskOutcome.Pending };

    public static ControlAskResult Escaped() => new() { Result = ControlAskOutcome.Escaped };

    public static ControlAskResult Cancelled() => new() { Result = ControlAskOutcome.Cancelled };

    public static ControlAskResult Answered(string id, string label, int index) =>
        new() { Result = ControlAskOutcome.Answered, Id = id, Label = label, Index = index };
}

/// <summary>A session node's pending terminal question. pane is the covered pane's current role,
/// absent for a session-wide question.</summary>
public sealed record ControlSessionAsk
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("pane")]
    public string? Pane { get; init; }
}

public enum ControlAskStyle
{
    Terminal,
    Gui,
}

public enum ControlAskAlignment
{
    Left,
    Center,
    Right,
}

public static class ControlAskStyles
{
    public static ControlAskStyle? FromWire(string wire) => wire switch
    {
        "terminal" => ControlAskStyle.Terminal,
        "gui" => ControlAskStyle.Gui,
        _ => null,
    };
}

public static class ControlAskAlignments
{
    public static ControlAskAlignment? FromWire(string wire) => wire switch
    {
        "left" => ControlAskAlignment.Left,
        "center" => ControlAskAlignment.Center,
        "right" => ControlAskAlignment.Right,
        _ => null,
    };
}
