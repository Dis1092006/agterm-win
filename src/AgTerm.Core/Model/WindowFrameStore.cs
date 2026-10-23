using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgTerm.Core.Model;

/// <summary>A window rectangle in physical pixels, in virtual screen coordinates.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>A window's normal (not maximized) bounds and whether it was maximized when it closed.</summary>
public sealed record SavedWindowFrame(PixelRect Bounds, bool Maximized);

/// <summary>
/// Each window's last frame, keyed by window id, in &lt;stateDir&gt;\window-frames.json. Upstream keeps
/// the same per-window frame in UserDefaults; a file in the state dir keeps it isolated by
/// AGTERM_STATE_DIR, and windows.json keeps upstream's shape. An unreadable file reads as no frames,
/// since a lost frame only costs the default window placement.
/// </summary>
public sealed class WindowFrameStore(string? directory = null)
{
    private const string FileName = "window-frames.json";

    private readonly string _directory = directory ?? PersistenceStore.DefaultDirectory;

    public SavedWindowFrame? Load(Guid windowId)
    {
        if (!ReadFile().Windows.TryGetValue(Key(windowId), out var entry)
            || entry.Width <= 0
            || entry.Height <= 0)
            return null;
        return new SavedWindowFrame(new PixelRect(entry.X, entry.Y, entry.Width, entry.Height), entry.Maximized);
    }

    public void Save(Guid windowId, SavedWindowFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var file = ReadFile();
        file.Windows[Key(windowId)] = new FrameEntry
        {
            X = frame.Bounds.X,
            Y = frame.Bounds.Y,
            Width = frame.Bounds.Width,
            Height = frame.Bounds.Height,
            Maximized = frame.Maximized,
        };
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, FileName);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file));
        AtomicFile.Replace(temp, path);
    }

    private FramesFile ReadFile()
    {
        try
        {
            var file = JsonSerializer.Deserialize<FramesFile>(File.ReadAllText(Path.Combine(_directory, FileName)));
            if (file is { Version: 1 })
                return file;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
        }
        return new FramesFile();
    }

    private static string Key(Guid windowId) => windowId.ToString("D").ToUpperInvariant();

    private sealed class FramesFile
    {
        [JsonPropertyName("version")]
        public int Version { get; init; } = 1;

        [JsonPropertyName("windows")]
        public Dictionary<string, FrameEntry> Windows { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FrameEntry
    {
        [JsonPropertyName("x")]
        public int X { get; init; }

        [JsonPropertyName("y")]
        public int Y { get; init; }

        [JsonPropertyName("width")]
        public int Width { get; init; }

        [JsonPropertyName("height")]
        public int Height { get; init; }

        [JsonPropertyName("maximized")]
        public bool Maximized { get; init; }
    }
}
