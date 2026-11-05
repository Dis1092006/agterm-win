using System.Text.Json.Serialization;

namespace AgTerm.Core.Protocol;

/// <summary>The hooks.list read-back: where the file was read from, what failed to parse, and one
/// row per hook.</summary>
public sealed record ControlHooks
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("diagnostics")]
    public required ControlKeymapDiagnostic[] Diagnostics { get; init; }

    [JsonPropertyName("hooks")]
    public required ControlHookEntry[] Hooks { get; init; }
}

/// <summary>One hook's definition and live state. RunningPid/ElapsedSeconds are absent while idle;
/// LastFailure is absent until a run fails and again after the next clean run (a reload keeps it).
/// Retired is true for a hook removed from the file whose child is still running; it lists after the
/// live rows so a hung script stays visible.</summary>
public sealed record ControlHookEntry
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("line")]
    public required int Line { get; init; }

    [JsonPropertyName("runningPid")]
    public int? RunningPid { get; init; }

    [JsonPropertyName("elapsedSeconds")]
    public double? ElapsedSeconds { get; init; }

    [JsonPropertyName("pending")]
    public int Pending { get; init; }

    [JsonPropertyName("dropped")]
    public ulong Dropped { get; init; }

    [JsonPropertyName("lastFailure")]
    public string? LastFailure { get; init; }

    [JsonPropertyName("retired")]
    public bool? Retired { get; init; }
}
