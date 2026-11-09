using System.Text.Json;
using System.Text.Json.Serialization;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Keymap;

/// <summary>
/// Run counts for keymap.conf commands, keyed by name, behind the most-used group of the title bar
/// custom commands menu. The name is the durable key: CustomCommand.Id is minted on every parse, and the
/// parser rejects a second command line with a name already taken. Upstream CustomCommandUsage (68e17937).
/// </summary>
public sealed class CustomCommandUsage
{
    public const int CurrentVersion = 1;

    // declared before version: upstream writes with sorted keys
    [JsonPropertyName("counts")]
    public Dictionary<string, int> Counts { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    /// <summary>Count one run of the command.</summary>
    public void Record(CustomCommand command) => Record(command.Name);

    /// <summary>Count one run of the command with this name: the runner knows a command only by its
    /// resolved invocation.</summary>
    public void Record(string commandName) =>
        Counts[commandName] = Counts.GetValueOrDefault(commandName) + 1;

    /// <summary>The most-run of the commands, at most limit, in descending count with ties kept in the
    /// given order. A command never run is left out, and a count whose command is no longer in the keymap
    /// takes no slot.</summary>
    public IReadOnlyList<CustomCommand> MostUsed(IReadOnlyList<CustomCommand> commands, int limit) =>
        commands
            .Select((command, index) => (Count: Counts.GetValueOrDefault(command.Name), Index: index, Command: command))
            .Where(entry => entry.Count > 0)
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Index)
            .Take(Math.Max(0, limit))
            .Select(entry => entry.Command)
            .ToArray();
}

/// <summary>
/// The on-disk home of <see cref="CustomCommandUsage"/>: &lt;stateDir&gt;/custom-command-usage.json, read
/// tolerantly (a missing, corrupt or foreign-version file counts as empty) and written atomically, like
/// <see cref="RecentClosedStore"/>. Upstream CustomCommandUsageStore.
/// </summary>
public sealed class CustomCommandUsageStore
{
    private readonly Lock _gate = new();
    private readonly string _directory;
    private readonly string _fileName;

    private string FilePath => Path.Combine(_directory, _fileName);

    public CustomCommandUsageStore(string? directory = null, string fileName = "custom-command-usage.json")
    {
        _directory = directory ?? PersistenceStore.DefaultDirectory;
        _fileName = fileName;
    }

    public CustomCommandUsage Load()
    {
        lock (_gate)
        {
            try
            {
                var usage = ControlJson.Deserialize<CustomCommandUsage>(File.ReadAllText(FilePath));
                return usage is { Version: CustomCommandUsage.CurrentVersion } ? usage : new CustomCommandUsage();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
                                          or NotSupportedException)
            {
                return new CustomCommandUsage();
            }
        }
    }

    /// <summary>Count one run of the command and persist the result.</summary>
    public void Record(CustomCommand command) => Record(command.Name);

    /// <summary>Count one run of the command with this name and persist the result.</summary>
    public void Record(string commandName)
    {
        lock (_gate)
        {
            var usage = Load();
            usage.Record(commandName);
            Save(usage);
        }
    }

    private void Save(CustomCommandUsage usage)
    {
        var sorted = new CustomCommandUsage { Version = usage.Version };
        foreach (var (name, count) in usage.Counts.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            sorted.Counts[name] = count;
        try
        {
            Directory.CreateDirectory(_directory);
            var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, ControlJson.Serialize(sorted));
            AtomicFile.Replace(temp, FilePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a lost count must not fail the command run (upstream logs and swallows)
        }
    }
}

/// <summary>
/// The title bar custom commands menu rows. Past <see cref="MostUsedLimit"/> commands, the most-used ones
/// lead, still in keymap order, then a separator, then the rest in keymap order. Counts decide only which
/// rows lead, never their order. Upstream customCommandsPopover.
/// </summary>
public sealed record CustomCommandMenuGroups(IReadOnlyList<CustomCommand> Leading, IReadOnlyList<CustomCommand> Rest)
{
    public const int MostUsedLimit = 5;

    public static CustomCommandMenuGroups From(IReadOnlyList<CustomCommand> commands, CustomCommandUsage usage)
    {
        var leadingIds = commands.Count > MostUsedLimit
            ? usage.MostUsed(commands, MostUsedLimit).Select(command => command.Id).ToHashSet()
            : [];
        return new CustomCommandMenuGroups(
            commands.Where(command => leadingIds.Contains(command.Id)).ToArray(),
            commands.Where(command => !leadingIds.Contains(command.Id)).ToArray());
    }
}
