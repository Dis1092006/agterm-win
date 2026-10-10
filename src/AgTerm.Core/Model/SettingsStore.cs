using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

/// <summary>
/// Reads and writes AppSettings as settings.json in the same directory as the workspace snapshot
/// (so the %AGTERM_STATE_DIR% override applies). Mirrors PersistenceStore's recovery contract:
/// a missing file or corrupt JSON resolves to the default settings with the app's default theme
/// seeded (a fresh install opens on the agterm theme); an existing file decodes as-is (an absent
/// theme key stays null = engine built-in, so an existing user is never silently re-themed).
/// Load() never throws to the caller; Save() writes atomically (temp file then rename).
/// </summary>
public sealed class SettingsStore
{
    private const string FileName = "settings.json";

    private readonly string _directory;

    private string FilePath => Path.Combine(_directory, FileName);

    public SettingsStore(string? directory = null)
    {
        _directory = directory ?? PersistenceStore.DefaultDirectory;
    }

    public AppSettings Load()
    {
        try
        {
            return ControlJson.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? SeededDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or System.Text.Json.JsonException or NotSupportedException)
        {
            return SeededDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_directory);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, ControlJson.Serialize(settings));
        AtomicFile.Replace(temp, FilePath);
    }

    private static AppSettings SeededDefault() => new() { Theme = AppSettings.DefaultTheme };
}
