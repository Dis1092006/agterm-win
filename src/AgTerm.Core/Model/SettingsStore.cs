using AgTerm.Core.Protocol;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgTerm.Core.Model;

/// <summary>
/// Reads and writes AppSettings as settings.json in the same directory as the workspace snapshot
/// (so the %AGTERM_STATE_DIR% override applies). Mirrors PersistenceStore's recovery contract:
/// a missing file or corrupt JSON resolves to the default settings with the app's default theme
/// seeded (a fresh install opens on the agterm theme); an existing file decodes as-is (an absent
/// theme key stays null = engine built-in, so an existing user is never silently re-themed).
/// Load() keeps startup recovery. Editing uses ReadStrict()/Update(), which never replace an
/// unreadable file with defaults. All writers to the same path share an in-process lock.
/// </summary>
public sealed class SettingsStore
{
    private const string FileName = "settings.json";
    private static readonly ConcurrentDictionary<string, Lock> Gates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly string[] ThemeFields = ["theme", "darkTheme", "followSystemAppearance"];
    // A retired key the model no longer reads, removed from the file when the field that replaced it is written
    private static readonly Dictionary<string, string> RetiredFields = new(StringComparer.Ordinal)
    {
        ["sidebarThemeStyle"] = "sidebarThemeColors",
    };

    private readonly string _directory;
    private readonly Lock _gate;

    public string FilePath { get; }

    public SettingsStore(string? directory = null)
    {
        _directory = Path.GetFullPath(directory ?? PersistenceStore.DefaultDirectory);
        FilePath = Path.Combine(_directory, FileName);
        _gate = Gates.GetOrAdd(FilePath, static _ => new Lock());
    }

    public AppSettings Load()
    {
        try
        {
            return ReadStrict().Settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or System.Text.Json.JsonException or NotSupportedException)
        {
            return SeededDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            var current = ReadStrictCore();
            var document = current.Document();
            foreach (var field in ControlJson.Options.GetTypeInfo(typeof(AppSettings)).Properties
                .Where(property => property.Get is not null).Select(property => property.Name))
                document.Remove(field);
            foreach (var field in ToDocument(settings))
                document[field.Key] = field.Value?.DeepClone();
            Write(current, document);
        }
    }

    public SettingsSnapshot ReadStrict()
    {
        lock (_gate) return ReadStrictCore();
    }

    /// <summary>For commands whose intent is applied to the latest file, not an open editor draft.</summary>
    public SettingsSnapshot UpdateCurrent(Func<AppSettings, AppSettings> change)
    {
        lock (_gate) return Update(ReadStrictCore(), change);
    }

    /// <summary>Merge only fields changed from the editor's snapshot. Theme slots form one group.
    /// Concurrent edits to that group or the same field require an explicit reload by the caller.</summary>
    public SettingsSnapshot Update(SettingsSnapshot baseline, Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(change);
        if (!Gates.Comparer.Equals(baseline.Path, FilePath))
            throw new ArgumentException("The settings snapshot belongs to another file.", nameof(baseline));

        lock (_gate)
        {
            var before = ToDocument(baseline.Settings);
            var after = ToDocument(change(baseline.Settings));
            var changed = before.Select(entry => entry.Key).Union(after.Select(entry => entry.Key))
                .Where(field => !JsonNode.DeepEquals(before[field], after[field])).ToHashSet(StringComparer.Ordinal);
            if (ThemeFields.Any(changed.Contains))
                changed.UnionWith(ThemeFields);

            var current = ReadStrictCore();
            var latest = ToDocument(current.Settings);
            var conflicts = changed.Where(field =>
                !JsonNode.DeepEquals(latest[field], before[field]) &&
                !JsonNode.DeepEquals(latest[field], after[field])).Order(StringComparer.Ordinal).ToArray();
            if (conflicts.Length > 0)
                throw new SettingsConflictException(current, conflicts);

            if (changed.Count == 0) return current;
            var document = current.Document();
            foreach (var field in changed)
            {
                if (after.TryGetPropertyValue(field, out var value))
                    document[field] = value?.DeepClone();
                else
                    document.Remove(field);
                if (RetiredFields.TryGetValue(field, out var retired))
                    document.Remove(retired);
            }
            return Write(current, document);
        }
    }

    private SettingsSnapshot ReadStrictCore()
    {
        string content;
        try { content = File.ReadAllText(FilePath); }
        catch (FileNotFoundException) { return new(FilePath, null, SeededDefault()); }
        catch (DirectoryNotFoundException) { return new(FilePath, null, SeededDefault()); }
        if (JsonNode.Parse(content) is not JsonObject)
            throw new JsonException("settings.json must contain a JSON object.");
        var settings = ControlJson.Deserialize<AppSettings>(content)
            ?? throw new JsonException("settings.json must contain a JSON object.");
        return new(FilePath, content, settings);
    }

    private SettingsSnapshot Write(SettingsSnapshot current, JsonObject document)
    {
        var content = document.ToJsonString(ControlJson.Options);
        var settings = ControlJson.Deserialize<AppSettings>(content)
            ?? throw new JsonException("settings.json must contain a JSON object.");
        Directory.CreateDirectory(_directory);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, content);
            // External editors do not honor our lock. Detect a changed revision before the rename;
            // this is not a filesystem compare-and-swap against an arbitrary external writer.
            if (ReadStrictCore().Content != current.Content)
                throw new SettingsConflictException(ReadStrictCore(), ["settings.json"]);
            AtomicFile.Replace(temp, FilePath);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        return new(FilePath, content, settings);
    }

    private static JsonObject ToDocument(AppSettings settings) =>
        JsonNode.Parse(ControlJson.Serialize(settings))!.AsObject();

    private static AppSettings SeededDefault() => new() { Theme = AppSettings.DefaultTheme };
}

public sealed class SettingsSnapshot
{
    internal SettingsSnapshot(string path, string? content, AppSettings settings)
    {
        Path = path;
        Content = content;
        Settings = settings with { HiddenInterfaceElements = settings.HiddenInterfaceElements?.ToArray().AsReadOnly() };
    }

    public AppSettings Settings { get; }
    internal string Path { get; }
    internal string? Content { get; }
    internal JsonObject Document() => JsonNode.Parse(Content ?? ControlJson.Serialize(Settings))!.AsObject();
}

public sealed class SettingsConflictException(SettingsSnapshot current, IReadOnlyList<string> fields)
    : IOException($"Settings changed outside this edit: {string.Join(", ", fields)}. Reload before trying again.")
{
    public SettingsSnapshot Current { get; } = current;
    public IReadOnlyList<string> Fields { get; } = fields;
}
