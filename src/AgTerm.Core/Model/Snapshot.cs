using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

/// <summary>Which view the sidebar renders: the workspace tree or the flat flagged list. Per-window
/// UI state, persisted in the snapshot (absent decodes as tree).</summary>
[JsonConverter(typeof(SidebarModeConverter))]
public enum SidebarMode { Tree, Flagged }

public sealed class SidebarModeConverter : WireEnumConverter<SidebarMode>
{
    public SidebarModeConverter() : base((SidebarMode.Tree, "tree"), (SidebarMode.Flagged, "flagged"))
    {
    }
}

/// <summary>
/// The persisted form of the whole app state (one snapshot per window): a plain value tree that
/// mirrors the live model but carries no live Session/Workspace references.
///
/// Forward-compat contract: the snapshot itself HAS a version (a mismatch makes the loader start
/// fresh), but the per-session/window fields do not — they are all optional and a missing key
/// decodes as the default, so an old file on disk never fails the load and wipes the tree.
/// </summary>
public sealed record Snapshot
{
    /// <summary>Bumped when the on-disk shape changes; a mismatch makes the loader start fresh.</summary>
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    [JsonRequired]
    public int Version { get; init; } = CurrentVersion;

    [JsonPropertyName("selectedSessionID")]
    [JsonConverter(typeof(UppercaseGuidConverter))]
    public Guid? SelectedSessionId { get; init; }

    [JsonPropertyName("workspaces")]
    public required WorkspaceSnapshot[] Workspaces { get; init; }

    /// <summary>The window's sidebar width in points, or null for the default.</summary>
    [JsonPropertyName("sidebarWidth")]
    public double? SidebarWidth { get; init; }

    /// <summary>Whether the window's sidebar is shown, or null for the default (shown).</summary>
    [JsonPropertyName("sidebarVisible")]
    public bool? SidebarVisible { get; init; }

    /// <summary>Which view the sidebar renders, or null for the default (tree). Strict decode:
    /// an unknown raw value fails the snapshot, like upstream.</summary>
    [JsonPropertyName("sidebarMode")]
    public SidebarMode? SidebarMode { get; init; }

    /// <summary>The workspace the sidebar tree is focused on, or null for the full tree.</summary>
    [JsonPropertyName("focusedWorkspaceID")]
    [JsonConverter(typeof(UppercaseGuidConverter))]
    public Guid? FocusedWorkspaceId { get; init; }

    /// <summary>
    /// Most-recently-selected session ids, front = current, so the MRU switcher's order survives a
    /// relaunch. LOSSY decode: a present-but-invalid list drops to null instead of failing the whole
    /// snapshot and wiping every workspace over a non-essential field.
    /// </summary>
    [JsonPropertyName("sessionRecency")]
    [JsonConverter(typeof(LossyGuidArrayConverter))]
    public Guid[]? SessionRecency { get; init; }
}

/// <summary>One persisted workspace: its identity, name, and ordered sessions.</summary>
public sealed record WorkspaceSnapshot
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(UppercaseGuidConverter))]
    public required Guid Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("sessions")]
    public required SessionSnapshot[] Sessions { get; init; }

    /// <summary>Whether the sidebar row was collapsed. Only a collapsed workspace writes it (true);
    /// an expanded one omits it, so an all-expanded tree serializes identically to a legacy snapshot.</summary>
    [JsonPropertyName("collapsed")]
    public bool? Collapsed { get; init; }

    /// <summary>The workspace's own terminal theme (Windows-only). LOSSY decode: a value that is
    /// not a string, or a blank one, reads as null, because a strict failure here would make the
    /// loader discard every workspace over one cosmetic field.</summary>
    [JsonPropertyName("theme")]
    [JsonConverter(typeof(LossyThemeNameConverter))]
    public string? Theme { get; init; }
}

/// <summary>
/// One persisted session. Cwd is the live currentCwd, or the initialCwd when no PWD report has
/// arrived yet. Every field but id/cwd is optional so a snapshot written before the field existed
/// still decodes instead of failing the load and wiping the saved tree.
/// </summary>
public sealed record SessionSnapshot
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(UppercaseGuidConverter))]
    public required Guid Id { get; init; }

    [JsonPropertyName("customName")]
    public string? CustomName { get; init; }

    [JsonPropertyName("cwd")]
    public required string Cwd { get; init; }

    /// <summary>Whether the session was shown as a vertical split; the split pane re-spawns fresh.</summary>
    [JsonPropertyName("isSplit")]
    public bool? IsSplit { get; init; }

    [JsonPropertyName("fontSize")]
    public double? FontSize { get; init; }

    /// <summary>The split (right) pane's working directory so each pane restores its OWN cwd.</summary>
    [JsonPropertyName("splitCwd")]
    public string? SplitCwd { get; init; }

    /// <summary>The split divider's left-pane fraction; null restores the even default.</summary>
    [JsonPropertyName("splitRatio")]
    public double? SplitRatio { get; init; }

    [JsonPropertyName("flagged")]
    public bool? Flagged { get; init; }

    /// <summary>The main pane's foreground argv at the last clean quit (restore-running-command).</summary>
    [JsonPropertyName("foregroundCommand")]
    public string[]? ForegroundCommand { get; init; }

    [JsonPropertyName("splitForegroundCommand")]
    public string[]? SplitForegroundCommand { get; init; }

    /// <summary>The command the session was created with (session.new --command), re-run on restore.</summary>
    [JsonPropertyName("initialCommand")]
    public string? InitialCommand { get; init; }

    /// <summary>Whether a --command session holds its surface after the command exits.</summary>
    [JsonPropertyName("commandWait")]
    public bool? CommandWait { get; init; }

    /// <summary>
    /// The session's background watermark, or null for none. LOSSY decode: a present-but-invalid
    /// spec (an unknown kind/fit/position after a downgrade, or a hand-edit typo) drops to null
    /// instead of failing the entire SessionSnapshot and wiping every workspace.
    /// </summary>
    [JsonPropertyName("backgroundWatermark")]
    [JsonConverter(typeof(LossyWatermarkConverter))]
    public BackgroundWatermark? BackgroundWatermark { get; init; }

    /// <summary>The main pane's restore-command override (session.restore); wins over
    /// foregroundCommand and initialCommand on the next launch. Tri-state (null / "" / command).</summary>
    [JsonPropertyName("restoreCommand")]
    public string? RestoreCommand { get; init; }

    [JsonPropertyName("splitRestoreCommand")]
    public string? SplitRestoreCommand { get; init; }
}

/// <summary>Decodes a watermark LOSSILY: any invalid value becomes null instead of throwing (see
/// SessionSnapshot.BackgroundWatermark). Encoding is the normal strict form.</summary>
public sealed class LossyWatermarkConverter : JsonConverter<BackgroundWatermark?>
{
    public override BackgroundWatermark? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Consume the whole value regardless of validity so the outer object keeps parsing.
        using var doc = JsonDocument.ParseValue(ref reader);
        try
        {
            return doc.RootElement.Deserialize((JsonTypeInfo<BackgroundWatermark>)options.GetTypeInfo(typeof(BackgroundWatermark)));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, BackgroundWatermark? value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, (JsonTypeInfo<BackgroundWatermark?>)options.GetTypeInfo(typeof(BackgroundWatermark)));
}

/// <summary>Decodes a theme name LOSSILY (see WorkspaceSnapshot.Theme). Encoding writes the
/// string.</summary>
public sealed class LossyThemeNameConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Consume the whole value regardless of its kind so the outer object keeps parsing.
        using var doc = JsonDocument.ParseValue(ref reader);
        return doc.RootElement.ValueKind == JsonValueKind.String
            && doc.RootElement.GetString() is { } name
            && !string.IsNullOrWhiteSpace(name)
                ? name
                : null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}

/// <summary>Decodes a UUID list LOSSILY (see Snapshot.SessionRecency): a malformed uuid or a wrong
/// JSON type drops the whole list to null. Encodes as uppercase canonical strings.</summary>
public sealed class LossyGuidArrayConverter : JsonConverter<Guid[]?>
{
    public override Guid[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        var result = new List<Guid>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String || !Guid.TryParse(element.GetString(), out var id))
                return null;
            result.Add(id);
        }
        return result.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, Guid[]? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }
        writer.WriteStartArray();
        foreach (var id in value)
            writer.WriteStringValue(id.ToString("D").ToUpperInvariant());
        writer.WriteEndArray();
    }
}
