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
public sealed record Snapshot : IJsonOnDeserialized
{
    /// <summary>Bumped when the on-disk shape changes; a mismatch makes the loader start fresh.</summary>
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    [JsonRequired]
    public int Version { get; init; } = CurrentVersion;

    [JsonPropertyName("selectedSessionID")]
    [JsonConverter(typeof(LossyUuidConverter))]
    public Guid? SelectedSessionId { get; init; }

    [JsonPropertyName("workspaces")]
    public required WorkspaceSnapshot[] Workspaces { get; init; }

    /// <summary>The window's sidebar width in points, or null for the default.</summary>
    [JsonPropertyName("sidebarWidth")]
    [JsonConverter(typeof(LossyDoubleConverter))]
    public double? SidebarWidth { get; init; }

    /// <summary>Whether the window's sidebar is shown, or null for the default (shown).</summary>
    [JsonPropertyName("sidebarVisible")]
    [JsonConverter(typeof(LossyBoolConverter))]
    public bool? SidebarVisible { get; init; }

    /// <summary>Which view the sidebar renders, or null for the default (tree). LOSSY decode: an
    /// unknown raw value (a newer build) drops to null instead of wiping the tree.</summary>
    [JsonPropertyName("sidebarMode")]
    [JsonConverter(typeof(LossySidebarModeConverter))]
    public SidebarMode? SidebarMode { get; init; }

    private Guid[]? _focusedWorkspaceIds;
    private bool? _focusEnabled;
    private Guid? _legacyFocusedWorkspaceId;

    /// <summary>The workspaces marked in the sidebar focus set, in tree order; null = nothing marked.
    /// LOSSY decode: a malformed list reads as empty, which also keeps the legacy migration off,
    /// because the key is present.</summary>
    [JsonPropertyName("focusedWorkspaceIDs")]
    [JsonConverter(typeof(LossyFocusIdsConverter))]
    public Guid[]? FocusedWorkspaceIds { get => _focusedWorkspaceIds; init => _focusedWorkspaceIds = value; }

    /// <summary>Whether the focus filter applies to the marked set; null = off. Persisted apart so
    /// turning the filter off keeps the members.</summary>
    [JsonPropertyName("focusEnabled")]
    [JsonConverter(typeof(LossyBoolConverter))]
    public bool? FocusEnabled { get => _focusEnabled; init => _focusEnabled = value; }

    /// <summary>The single-workspace focus key from before the focus set existed. Read-only: it
    /// serializes as absent, so a migrated snapshot never writes it back.</summary>
    [JsonPropertyName("focusedWorkspaceID")]
    [JsonConverter(typeof(LossyUuidConverter))]
    public Guid? LegacyFocusedWorkspaceId { get => null; init => _legacyFocusedWorkspaceId = value; }

    void IJsonOnDeserialized.OnDeserialized()
    {
        // the legacy key implied the filter was on: it becomes a one-member ENABLED set, but only
        // when the set key is absent (a malformed set reads as empty, not absent).
        if (_focusedWorkspaceIds is null && _legacyFocusedWorkspaceId is { } legacy)
        {
            _focusedWorkspaceIds = new[] { legacy };
            _focusEnabled = true;
        }
    }

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
    [JsonConverter(typeof(LossyBoolConverter))]
    public bool? Collapsed { get; init; }

    /// <summary>The workspace's own terminal theme (Windows-only). LOSSY decode: a value that is
    /// not a string, or a blank one, reads as null, because a strict failure here would make the
    /// loader discard every workspace over one cosmetic field.</summary>
    [JsonPropertyName("theme")]
    [JsonConverter(typeof(LossyThemeNameConverter))]
    public string? Theme { get; init; }

    /// <summary>The workspace's own sidebar icon name (Windows-only). Decoded as leniently as
    /// Theme, for the same reason.</summary>
    [JsonPropertyName("icon")]
    [JsonConverter(typeof(LossyThemeNameConverter))]
    public string? Icon { get; init; }
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
    [JsonConverter(typeof(LossyStringConverter))]
    public string? CustomName { get; init; }

    [JsonPropertyName("cwd")]
    public required string Cwd { get; init; }

    /// <summary>Whether the session was shown as a vertical split; the split pane re-spawns fresh.</summary>
    [JsonPropertyName("isSplit")]
    [JsonConverter(typeof(LossyBoolConverter))]
    public bool? IsSplit { get; init; }

    /// <summary>The shown split's divider direction (horizontal = top/bottom); null or a value this
    /// build does not know restores the legacy left/right arrangement.</summary>
    [JsonPropertyName("splitAxis")]
    [JsonConverter(typeof(LossySplitAxisConverter))]
    public SplitAxis? SplitAxis { get; init; }

    [JsonPropertyName("fontSize")]
    [JsonConverter(typeof(LossyDoubleConverter))]
    public double? FontSize { get; init; }

    /// <summary>The split (right) pane's working directory so each pane restores its OWN cwd.</summary>
    [JsonPropertyName("splitCwd")]
    [JsonConverter(typeof(LossyStringConverter))]
    public string? SplitCwd { get; init; }

    /// <summary>The split divider's left-pane fraction; null restores the even default.</summary>
    [JsonPropertyName("splitRatio")]
    [JsonConverter(typeof(LossyDoubleConverter))]
    public double? SplitRatio { get; init; }

    [JsonPropertyName("flagged")]
    [JsonConverter(typeof(LossyBoolConverter))]
    public bool? Flagged { get; init; }

    /// <summary>What the session is FOR (session.context). LOSSY decode: a value that fails the
    /// context rules reads as null instead of failing the session; a padded one is trimmed.</summary>
    [JsonPropertyName("context")]
    [JsonConverter(typeof(LossyContextConverter))]
    public string? Context { get; init; }

    /// <summary>The main pane's foreground argv at the last clean quit (restore-running-command).</summary>
    [JsonPropertyName("foregroundCommand")]
    [JsonConverter(typeof(LossyStringArrayConverter))]
    public string[]? ForegroundCommand { get; init; }

    [JsonPropertyName("splitForegroundCommand")]
    [JsonConverter(typeof(LossyStringArrayConverter))]
    public string[]? SplitForegroundCommand { get; init; }

    /// <summary>The command the session was created with (session.new --command), re-run on restore.</summary>
    [JsonPropertyName("initialCommand")]
    [JsonConverter(typeof(LossyStringConverter))]
    public string? InitialCommand { get; init; }

    /// <summary>Whether a --command session holds its surface after the command exits.</summary>
    [JsonPropertyName("commandWait")]
    [JsonConverter(typeof(LossyBoolConverter))]
    public bool? CommandWait { get; init; }

    /// <summary>The split pane's creation command, the split analogue of initialCommand.</summary>
    [JsonPropertyName("splitInitialCommand")]
    [JsonConverter(typeof(LossyStringConverter))]
    public string? SplitInitialCommand { get; init; }

    [JsonPropertyName("splitCommandWait")]
    [JsonConverter(typeof(LossyBoolConverter))]
    public bool? SplitCommandWait { get; init; }
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
    [JsonConverter(typeof(LossyStringConverter))]
    public string? RestoreCommand { get; init; }

    [JsonPropertyName("splitRestoreCommand")]
    [JsonConverter(typeof(LossyStringConverter))]
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

/// <summary>Shared by the lossy scalar converters: consumes the WHOLE value whatever its shape, so a
/// bad value never leaves the outer object half parsed.</summary>
internal static class LossyJson
{
    public static JsonElement Element(ref Utf8JsonReader reader)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return doc.RootElement.Clone();
    }
}

/// <summary>Decodes a bool LOSSILY: any other JSON type reads as null.</summary>
public sealed class LossyBoolConverter : JsonConverter<bool?>
{
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var element = LossyJson.Element(ref reader);
        return element.ValueKind is JsonValueKind.True or JsonValueKind.False ? element.GetBoolean() : null;
    }

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value is { } v) writer.WriteBooleanValue(v);
        else writer.WriteNullValue();
    }
}

/// <summary>Decodes a split axis LOSSILY: any other JSON type or an unknown name reads as null.</summary>
public sealed class LossySplitAxisConverter : JsonConverter<SplitAxis?>
{
    public override SplitAxis? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var element = LossyJson.Element(ref reader);
        return element.ValueKind == JsonValueKind.String && SplitAxes.TryParse(element.GetString(), out var axis)
            ? axis
            : null;
    }

    public override void Write(Utf8JsonWriter writer, SplitAxis? value, JsonSerializerOptions options)
    {
        if (value is { } v) writer.WriteStringValue(SplitAxes.Wire(v));
        else writer.WriteNullValue();
    }
}

/// <summary>Decodes a number LOSSILY: any other JSON type reads as null.</summary>
public sealed class LossyDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var element = LossyJson.Element(ref reader);
        return element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value) ? value : null;
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value is { } v) writer.WriteNumberValue(v);
        else writer.WriteNullValue();
    }
}

/// <summary>Decodes a string LOSSILY: any other JSON type reads as null.</summary>
public sealed class LossyStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var element = LossyJson.Element(ref reader);
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

/// <summary>Decodes a session context LOSSILY and checks its CONTENT: a wrong JSON type or a value
/// that fails Session.ValidateContext reads as null; a valid one is returned trimmed.</summary>
public sealed class LossyContextConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var element = LossyJson.Element(ref reader);
        if (element.ValueKind != JsonValueKind.String) return null;
        return Session.ValidateContext(element.GetString()!).Value;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

/// <summary>Decodes a string list LOSSILY: a wrong JSON type, or any non-string element, drops the
/// whole list to null.</summary>
public sealed class LossyStringArrayConverter : JsonConverter<string[]?>
{
    public override string[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var element = LossyJson.Element(ref reader);
        if (element.ValueKind != JsonValueKind.Array) return null;
        var result = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) return null;
            result.Add(item.GetString()!);
        }
        return result.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, string[]? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }
        writer.WriteStartArray();
        foreach (var item in value) writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}

/// <summary>Decodes a UUID LOSSILY: a malformed string or another JSON type reads as null. Encodes as
/// the uppercase canonical string, like Swift Foundation.</summary>
public sealed class LossyUuidConverter : JsonConverter<Guid?>
{
    public override Guid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var element = LossyJson.Element(ref reader);
        return element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var id) ? id : null;
    }

    public override void Write(Utf8JsonWriter writer, Guid? value, JsonSerializerOptions options)
    {
        if (value is { } id) writer.WriteStringValue(id.ToString("D").ToUpperInvariant());
        else writer.WriteNullValue();
    }
}

/// <summary>Decodes the sidebar mode LOSSILY: an unknown raw value or another JSON type reads as null.</summary>
public sealed class LossySidebarModeConverter : JsonConverter<SidebarMode?>
{
    public override SidebarMode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var element = LossyJson.Element(ref reader);
        return element.ValueKind == JsonValueKind.String
            ? element.GetString() switch { "tree" => SidebarMode.Tree, "flagged" => SidebarMode.Flagged, _ => null }
            : null;
    }

    public override void Write(Utf8JsonWriter writer, SidebarMode? value, JsonSerializerOptions options)
    {
        if (value is { } mode) writer.WriteStringValue(mode == SidebarMode.Flagged ? "flagged" : "tree");
        else writer.WriteNullValue();
    }
}

/// <summary>Decodes the focus set LOSSILY, but a present-yet-unusable list reads as EMPTY rather than
/// null: the snapshot's legacy migration fires only when the key is absent.</summary>
public sealed class LossyFocusIdsConverter : JsonConverter<Guid[]?>
{
    private readonly LossyGuidArrayConverter _inner = new();

    public override Guid[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => _inner.Read(ref reader, typeToConvert, options) ?? Array.Empty<Guid>();

    public override void Write(Utf8JsonWriter writer, Guid[]? value, JsonSerializerOptions options)
        => _inner.Write(writer, value, options);
}
