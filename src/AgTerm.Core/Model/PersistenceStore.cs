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

    /// <summary>%AGTERM_STATE_DIR% when set, else ~\.agterm-win — this port's own state dir
    /// (see PLATFORM-NOTES; upstream uses ~/Library/Application Support/agterm, spec §3). The name
    /// differs from upstream's so an agterm install on the same machine never shares state files or
    /// a derived pipe name with this port.
    ///
    /// Under a test run the real user state dir is refused: a test that reaches the implicit default
    /// would overwrite the developer's own workspaces. Tests must name a directory explicitly (a temp
    /// dir per test, or %AGTERM_STATE_DIR%).</summary>
    public static string DefaultDirectory =>
        Environment.GetEnvironmentVariable("AGTERM_STATE_DIR")
        ?? (IsTestRun
            ? throw new InvalidOperationException(
                "the default agterm state directory is not available under a test run: pass an "
                + "explicit directory or set AGTERM_STATE_DIR, so the test cannot write to the "
                + "real user state directory")
            : RealUserDirectory);

    /// <summary>
    /// The real per-user state dir, bypassing the test-run guard. This is the rendezvous input for
    /// deriving the control pipe name — a pure string derivation that touches no files — so it stays
    /// available under a test run, where DefaultDirectory deliberately throws. Do NOT use it to read
    /// or write state.
    /// </summary>
    public static string RealUserDirectory =>
        Path.Combine(HomeDirectory, AppDirectoryName);

    /// <summary>The state/config dir name, under the home directory. Distinct from upstream's
    /// "agterm" so the two applications never collide.</summary>
    public const string AppDirectoryName = ".agterm-win";

    /// <summary>The user's home directory, falling back to the temp dir when it cannot be
    /// resolved (the analogue of upstream's no-HOME → /tmp fallback).</summary>
    private static string HomeDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } home
            ? home
            : Path.GetTempPath();

    /// <summary>True when a test host loaded this assembly. Detected from the loaded assemblies
    /// rather than an env var, so every test project is covered without per-project setup.</summary>
    private static bool IsTestRun =>
        AppDomain.CurrentDomain.GetAssemblies().Any(IsTestAssembly);

    private static bool IsTestAssembly(System.Reflection.Assembly assembly)
    {
        var name = assembly.GetName().Name;
        return name is not null
            && (name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("testhost", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Microsoft.TestPlatform", StringComparison.OrdinalIgnoreCase));
    }

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
        AtomicFile.Replace(temp, FilePath);
    }

    private static Snapshot Empty() => new() { Workspaces = Array.Empty<WorkspaceSnapshot>() };
}
