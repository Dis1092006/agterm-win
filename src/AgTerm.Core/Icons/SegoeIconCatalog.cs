using System.Globalization;
using System.Reflection;

namespace AgTerm.Core.Icons;

public sealed record SegoeIcon(string Name, int CodePoint);

public static class SegoeIconCatalog
{
    private const string ResourceName = "icons/segoe-fluent-icons.tsv";

    private static readonly Lazy<Loaded> Data = new(Load);

    public static IReadOnlyList<SegoeIcon> All => Data.Value.List;

    public static bool TryGet(string name, out SegoeIcon icon)
    {
        if (Data.Value.ByName.TryGetValue(name, out var found))
        {
            icon = found;
            return true;
        }

        icon = null!;
        return false;
    }

    private sealed record Loaded(IReadOnlyList<SegoeIcon> List, Dictionary<string, SegoeIcon> ByName);

    private static Loaded Load()
    {
        using var stream = typeof(SegoeIconCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"missing embedded resource {ResourceName}");
        using var reader = new StreamReader(stream);

        var list = new List<SegoeIcon>();
        var byName = new Dictionary<string, SegoeIcon>(StringComparer.Ordinal);
        while (reader.ReadLine() is { Length: > 0 } line)
        {
            var tab = line.IndexOf('\t');
            var icon = new SegoeIcon(line[..tab], int.Parse(line[(tab + 1)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            list.Add(icon);
            byName.Add(icon.Name, icon);
        }

        return new Loaded(list, byName);
    }
}
