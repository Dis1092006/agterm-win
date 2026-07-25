using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

/// <summary>
/// Reads and writes the app Snapshot as JSON on disk. The storage directory is a constructor
/// parameter (defaulting to the agterm state dir) so tests can point it at a temp dir.
///
/// Recovery contract: a missing file, corrupt JSON, or a version mismatch all resolve to a default
/// empty Snapshot — Load() never throws or crashes out to the caller. Save() writes atomically
/// (temp file then replace).
/// </summary>
public sealed class PersistenceStore
{
    private readonly string _directory;
    private readonly string _fileName;

    private string FilePath => Path.Combine(_directory, _fileName);

    /// <summary>Creates a store rooted at the directory. The file name defaults to workspaces.json;
    /// a per-window store overrides it to target windows\&lt;id&gt;.json.</summary>
    public PersistenceStore(string? directory = null, string fileName = "workspaces.json")
    {
        _directory = directory ?? DefaultDirectory;
        _fileName = fileName;
    }

    /// <summary>%AGTERM_STATE_DIR% when set, else %LOCALAPPDATA%\agterm — the Windows analogue of
    /// ~/Library/Application Support/agterm (spec §3).</summary>
    public static string DefaultDirectory =>
        Environment.GetEnvironmentVariable("AGTERM_STATE_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agterm");

    /// <summary>Loads the snapshot, recovering a default empty snapshot on any failure (missing file,
    /// unreadable data, corrupt JSON, or version mismatch).</summary>
    public Snapshot Load()
    {
        Snapshot? snapshot;
        try
        {
            snapshot = ControlJson.Deserialize<Snapshot>(File.ReadAllText(FilePath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or System.Text.Json.JsonException or NotSupportedException)
        {
            snapshot = null;
        }
        if (snapshot is null || snapshot.Version != Snapshot.CurrentVersion) return Empty();
        return snapshot;
    }

    /// <summary>
    /// Writes the snapshot atomically: encodes to a temp file in the same directory and renames it
    /// into place, so a crashed write never leaves a half-written destination. Creates the directory
    /// if needed.
    /// </summary>
    public void Save(Snapshot snapshot)
    {
        Directory.CreateDirectory(_directory);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, ControlJson.Serialize(snapshot));
        File.Move(temp, FilePath, overwrite: true);
    }

    private static Snapshot Empty() => new() { Workspaces = Array.Empty<WorkspaceSnapshot>() };
}
