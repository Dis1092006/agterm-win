using System.Text.Json.Serialization;

namespace AgTerm.Core.Protocol;

/// <summary>One caller-provided choice shown by the native picker.</summary>
public sealed record ControlPickItem
{
    public const int MaxItems = 1_000;

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }
}

/// <summary>The state or terminal outcome pick.result reports.</summary>
[JsonConverter(typeof(ControlPickOutcomeConverter))]
public enum ControlPickOutcome
{
    Pending,
    Picked,
    Custom,
    Cancelled,
}

public sealed class ControlPickOutcomeConverter : WireEnumConverter<ControlPickOutcome>
{
    public ControlPickOutcomeConverter() : base(
        (ControlPickOutcome.Pending, "pending"),
        (ControlPickOutcome.Picked, "picked"),
        (ControlPickOutcome.Custom, "custom"),
        (ControlPickOutcome.Cancelled, "cancelled"))
    {
    }
}

/// <summary>The nested pick.result payload. index is the position in the caller's list, not in
/// the filtered rows.</summary>
public sealed record ControlPickResult
{
    [JsonPropertyName("result")]
    public required ControlPickOutcome Result { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("index")]
    public int? Index { get; init; }

    [JsonPropertyName("query")]
    public string? Query { get; init; }

    public static ControlPickResult Pending() => new() { Result = ControlPickOutcome.Pending };

    public static ControlPickResult Cancelled() => new() { Result = ControlPickOutcome.Cancelled };

    public static ControlPickResult Picked(string id, string label, int index) =>
        new() { Result = ControlPickOutcome.Picked, Id = id, Label = label, Index = index };

    public static ControlPickResult Custom(string query) =>
        new() { Result = ControlPickOutcome.Custom, Query = query };
}
