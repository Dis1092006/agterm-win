namespace AgTerm.Core.Model;

public sealed record CatalogSound(string Name, string? FilePath, bool IsUserSound = false)
{
    public bool IsSystemDefault => FilePath is null;
}

/// <summary>Windows filename stems are sound names, not paths. User sounds shadow system files.</summary>
public sealed class SoundCatalog
{
    private readonly string mediaDirectory;
    private readonly Action<string> log;
    public string UserDirectory { get; }

    public SoundCatalog(string stateDirectory, string? mediaDirectory = null, Action<string>? log = null)
    {
        UserDirectory = Path.Combine(stateDirectory, "sounds");
        this.mediaDirectory = mediaDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
        this.log = log ?? (message => System.Diagnostics.Trace.TraceWarning(message));
    }

    public IReadOnlyList<string> StandardNames => Files(mediaDirectory)
        .Select(sound => sound.Name).ToArray();

    public IReadOnlyList<CatalogSound> Sounds
    {
        get
        {
            var sounds = new Dictionary<string, CatalogSound>(StringComparer.OrdinalIgnoreCase);
            foreach (var sound in Files(mediaDirectory).Concat(Files(UserDirectory, user: true)))
                if (!IsAlias(sound.Name)) sounds[sound.Name] = sound;
            return sounds.Values.OrderBy(sound => sound.IsUserSound)
                .ThenBy(sound => sound.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(sound => sound.Name, StringComparer.Ordinal).ToArray();
        }
    }

    public CatalogSound? Resolve(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(['\\', '/', ':']) >= 0)
            return null;
        if (IsAlias(name)) return new("default", null);
        var sounds = Sounds;
        var exact = sounds.FirstOrDefault(sound => string.Equals(sound.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var stem = name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        return sounds.FirstOrDefault(sound => string.Equals(sound.Name, stem, StringComparison.OrdinalIgnoreCase));
    }

    public string UnknownSoundError(string name) =>
        $"unknown sound: {name} (use 'default', 'beep', or one of: {string.Join(", ", StandardNames)})";

    private static bool IsAlias(string name) =>
        name.Equals("default", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("beep", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<CatalogSound> Files(string directory, bool user = false)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
                .Select(path => new CatalogSound(Path.GetFileNameWithoutExtension(path), path, user))
                .OrderBy(sound => sound.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(sound => sound.Name, StringComparer.Ordinal).ToArray();
        }
        catch (DirectoryNotFoundException) { return []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"Could not read sound catalog '{directory}': {ex.Message}");
            return [];
        }
    }
}
