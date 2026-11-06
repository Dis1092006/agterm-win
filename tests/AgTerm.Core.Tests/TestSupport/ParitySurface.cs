using System.Text.Json;

namespace AgTerm.Core.Tests.TestSupport;

/// <summary>The parity surface the port must match: the pin snapshot, what parity/adopted.json takes
/// from upstream, and what parity/windows-only.json adds that upstream does not have.</summary>
public static class ParitySurface
{
    private static JsonElement Load(string relativePath)
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(Repo.File(relativePath)));
        return doc.RootElement.Clone();
    }

    private static List<string> ControlNames(JsonElement root, string section) =>
        root.GetProperty("control").GetProperty(section).EnumerateArray()
            .Select(e => e.TryGetProperty("wire", out var wire) ? wire.GetString()! : e.GetProperty("name").GetString()!)
            .ToList();

    public static IReadOnlyList<string> Pin(string section) => ControlNames(Load("parity/surface.json"), section);

    public static IReadOnlyList<string> Upstream(string section) =>
        ControlNames(Load("docs/upstream/surface-v0.40.0.json"), section);

    public static IReadOnlyList<string> Adopted(string section) =>
        Load("parity/adopted.json").GetProperty(section).EnumerateArray()
            .Select(e => e.TryGetProperty("wire", out var wire) ? wire.GetString()! : e.GetProperty("name").GetString()!)
            .ToList();

    /// <summary>The builtin keymap actions the newest upstream snapshot declares, with their default chords.</summary>
    public static IReadOnlyList<(string Wire, string? DefaultChord)> UpstreamKeymapActions() =>
        Load("docs/upstream/surface-v0.40.0.json").GetProperty("keymap").GetProperty("actions").EnumerateArray()
            .Select(a => (a.GetProperty("wire").GetString()!,
                a.GetProperty("defaultChord").ValueKind == JsonValueKind.Null
                    ? null
                    : a.GetProperty("defaultChord").GetString()))
            .ToList();

    public static IReadOnlyList<string> UpstreamPersistence(string section) =>
        Load("docs/upstream/surface-v0.36.0.json").GetProperty("persistence").GetProperty(section)
            .EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToList();

    /// <summary>Persistence fields of the newest upstream snapshot (v0.40.0), for fields adopted after v0.36.0.</summary>
    public static IReadOnlyList<string> LatestUpstreamPersistence(string section) =>
        Load("docs/upstream/surface-v0.40.0.json").GetProperty("persistence").GetProperty(section)
            .EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToList();

    public static IReadOnlyList<string> UpstreamSettings() =>
        Load("docs/upstream/surface-v0.36.0.json").GetProperty("settings")
            .EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToList();

    /// <summary>Names parity/windows-only.json adds to a section; empty when the file has no such
    /// section.</summary>
    public static IReadOnlyList<string> WindowsOnly(string section) =>
        Load("parity/windows-only.json").TryGetProperty(section, out var entries)
            ? entries.EnumerateArray()
                .Select(e => e.TryGetProperty("wire", out var wire) ? wire.GetString()! : e.GetProperty("name").GetString()!)
                .ToList()
            : [];

    /// <summary>Pin plus adopted names of a control section, in upstream declaration order.</summary>
    public static IReadOnlyList<string> Expected(string controlSection, string adoptedSection)
    {
        var wanted = Pin(controlSection).Concat(Adopted(adoptedSection)).ToHashSet(StringComparer.Ordinal);
        return Upstream(controlSection).Where(wanted.Contains).ToList();
    }
}
