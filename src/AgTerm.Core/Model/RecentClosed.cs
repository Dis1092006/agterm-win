using System.Text.Json;
using System.Text.Json.Serialization;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

[JsonConverter(typeof(RecentClosedKindConverter))]
public enum RecentClosedKind { Session, Workspace }

public sealed class RecentClosedKindConverter : WireEnumConverter<RecentClosedKind>
{
    public RecentClosedKindConverter() : base(
        (RecentClosedKind.Session, "session"), (RecentClosedKind.Workspace, "workspace"))
    {
    }
}

/// <summary>One Open Recent entry: a closed session or workspace snapshot plus display metadata.</summary>
public sealed record RecentClosedItem
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(UppercaseGuidConverter))]
    public required Guid Id { get; init; }

    [JsonPropertyName("kind")]
    public required RecentClosedKind Kind { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    /// <summary>Encoded as seconds since the Apple reference date (2001-01-01T00:00:00Z), matching
    /// the Swift JSONEncoder default so the local file stays cross-readable.</summary>
    [JsonPropertyName("closedAt")]
    [JsonConverter(typeof(AppleReferenceDateConverter))]
    public DateTimeOffset ClosedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("session")]
    public RecentClosedSession? Session { get; init; }

    [JsonPropertyName("workspace")]
    public RecentClosedWorkspace? Workspace { get; init; }
}

public sealed record RecentClosedSession
{
    [JsonPropertyName("workspaceID")]
    [JsonConverter(typeof(UppercaseGuidConverter))]
    public required Guid WorkspaceId { get; init; }

    [JsonPropertyName("workspaceName")]
    public required string WorkspaceName { get; init; }

    [JsonPropertyName("workspaceIndex")]
    public required int WorkspaceIndex { get; init; }

    [JsonPropertyName("sessionIndex")]
    public required int SessionIndex { get; init; }

    [JsonPropertyName("snapshot")]
    public required SessionSnapshot Snapshot { get; init; }
}

public sealed record RecentClosedWorkspace
{
    [JsonPropertyName("snapshot")]
    public required WorkspaceSnapshot Snapshot { get; init; }

    [JsonPropertyName("selectedSessionID")]
    [JsonConverter(typeof(UppercaseGuidConverter))]
    public Guid? SelectedSessionId { get; init; }
}

public sealed record RecentClosedState
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentVersion;

    [JsonPropertyName("items")]
    public RecentClosedItem[] Items { get; init; } = Array.Empty<RecentClosedItem>();
}

/// <summary>
/// The Open Recent (recently closed sessions/workspaces) file store: a bounded newest-first list of
/// snapshots keyed by the closed object's id, deduplicated so one object holds one entry.
/// </summary>
public sealed class RecentClosedStore
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _fileName;
    private readonly int _limit;

    private string FilePath => Path.Combine(_directory, _fileName);

    public RecentClosedStore(string? directory = null, string fileName = "recent-closed.json", int limit = 20)
    {
        _directory = directory ?? PersistenceStore.DefaultDirectory;
        _fileName = fileName;
        _limit = Math.Max(1, limit);
    }

    public List<RecentClosedItem> Load()
    {
        lock (_gate)
        {
            try
            {
                var state = ControlJson.Deserialize<RecentClosedState>(File.ReadAllText(FilePath));
                if (state is null || state.Version != RecentClosedState.CurrentVersion)
                    return new List<RecentClosedItem>();
                return state.Items.Take(_limit).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
                                          or NotSupportedException)
            {
                return new List<RecentClosedItem>();
            }
        }
    }

    public void Record(RecentClosedItem item)
    {
        lock (_gate)
        {
            var items = Load();
            items.RemoveAll(existing =>
            {
                if (existing.Id == item.Id) return true;
                return (existing.Kind, item.Kind) switch
                {
                    (RecentClosedKind.Session, RecentClosedKind.Session) =>
                        existing.Session?.Snapshot.Id == item.Session?.Snapshot.Id,
                    (RecentClosedKind.Workspace, RecentClosedKind.Workspace) =>
                        existing.Workspace?.Snapshot.Id == item.Workspace?.Snapshot.Id,
                    _ => false,
                };
            });
            items.Insert(0, item);
            Save(items.Take(_limit).ToList());
        }
    }

    public void Remove(Guid id)
    {
        lock (_gate) Save(Load().Where(item => item.Id != id).ToList());
    }

    public void Clear()
    {
        lock (_gate) Save(new List<RecentClosedItem>());
    }

    private void Save(List<RecentClosedItem> items)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, ControlJson.Serialize(new RecentClosedState { Items = items.ToArray() }));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a transient disk error must not bring down the model (matches upstream's swallow+log).
        }
    }
}

/// <summary>Swift JSONEncoder's default Date coding: a double of seconds since 2001-01-01T00:00:00Z.</summary>
public sealed class AppleReferenceDateConverter : JsonConverter<DateTimeOffset>
{
    private static readonly DateTimeOffset ReferenceDate =
        new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReferenceDate.AddSeconds(reader.GetDouble());

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteNumberValue((value - ReferenceDate).TotalSeconds);
}
