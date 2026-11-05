using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgTerm.Core.Model;

/// <summary>The fixed palette a sidebar tint picks from. The wire and snapshot spelling is the
/// lowercase name (upstream TintColor, f3fea57b).</summary>
public enum TintColor
{
    Red,
    Orange,
    Yellow,
    Green,
    Blue,
    Purple,
    Gray,
}

public static class TintColors
{
    private static readonly TintColor[] All = Enum.GetValues<TintColor>();

    /// <summary>The accepted spelling for help and rejection text, derived so it cannot go stale.</summary>
    public static string ValidNamesList { get; } = string.Join("|", All.Select(Wire));

    public static string Wire(TintColor color) => color.ToString().ToLowerInvariant();

    /// <summary>Reads an exact wire name. Callers that accept any case lowercase first, as upstream
    /// does.</summary>
    public static bool TryParse(string? raw, out TintColor color)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(Wire(candidate), raw, StringComparison.Ordinal))
            {
                color = candidate;
                return true;
            }
        }
        color = default;
        return false;
    }
}

/// <summary>The parts of a sidebar row a tint can color (upstream TintPart). A set of them is a
/// flags value; the wire order is icon, then name.</summary>
[Flags]
public enum TintParts
{
    None = 0,
    Icon = 1,
    Name = 2,
}

/// <summary>
/// A session or workspace row's tint: one color shown on one or more parts of the row. Parts are
/// never empty (none means the icon), so two tints showing the same thing are equal however their
/// parts were listed. Snapshot encoding is <c>{"color":"red","on":["icon"]}</c>.
/// </summary>
[JsonConverter(typeof(SidebarTintConverter))]
public sealed record SidebarTint
{
    private const TintParts AllParts = TintParts.Icon | TintParts.Name;

    public SidebarTint(TintColor color, TintParts parts = TintParts.Icon)
    {
        Color = color;
        var known = parts & AllParts;
        Parts = known == TintParts.None ? TintParts.Icon : known;
    }

    public TintColor Color { get; }

    public TintParts Parts { get; }

    /// <summary>The accepted placement spelling for help and rejection text.</summary>
    public const string ValidPartNamesList = "icon,name";

    /// <summary>The parts as wire names, in icon, name order.</summary>
    public string[] PartNames => PartsToNames(Parts);

    public string ColorName => TintColors.Wire(Color);

    /// <summary>Whether the tint colors <paramref name="part"/> of the row.</summary>
    public bool Shows(TintParts part) => (Parts & part) == part;

    /// <summary>The tint a row with <paramref name="current"/> gets for <paramref name="color"/>: its
    /// own parts kept, an untinted row starting on the icon. A null color clears.</summary>
    public static SidebarTint? Recolor(SidebarTint? current, TintColor? color) =>
        color is { } c ? new SidebarTint(c, current?.Parts ?? TintParts.Icon) : null;

    /// <summary>Shows <paramref name="current"/>'s color on <paramref name="parts"/> instead. An untinted
    /// row stays untinted, so no row holds a placement it does not show.</summary>
    public static SidebarTint? Place(SidebarTint? current, TintParts parts) =>
        current is null ? null : new SidebarTint(current.Color, parts);

    /// <summary>Reads a wire placement list: each part trimmed of spaces and lowercased. Null when the
    /// list is empty or names an unknown part.</summary>
    public static TintParts? ParseParts(IEnumerable<string> raw)
    {
        var parts = TintParts.None;
        var any = false;
        foreach (var item in raw)
        {
            any = true;
            var part = PartFromName(TrimSpaces(item).ToLowerInvariant());
            if (part == TintParts.None) return null;
            parts |= part;
        }
        return any ? parts : null;
    }

    internal static TintParts PartFromName(string name) => name switch
    {
        "icon" => TintParts.Icon,
        "name" => TintParts.Name,
        _ => TintParts.None,
    };

    internal static string[] PartsToNames(TintParts parts)
    {
        var names = new List<string>(2);
        if ((parts & TintParts.Icon) != 0) names.Add("icon");
        if ((parts & TintParts.Name) != 0) names.Add("name");
        return names.ToArray();
    }

    // Swift's .whitespaces: spaces and tabs, not line breaks.
    private static string TrimSpaces(string value)
    {
        static bool IsSpace(char c) =>
            c == '\t' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;
        var start = 0;
        var end = value.Length;
        while (start < end && IsSpace(value[start])) start++;
        while (end > start && IsSpace(value[end - 1])) end--;
        return value[start..end];
    }
}

/// <summary>The strict tint encoding. An unknown or missing color throws, so a lossy caller drops the
/// tint; unknown parts are skipped and a missing, empty or mistyped list reads as the icon.</summary>
public sealed class SidebarTintConverter : JsonConverter<SidebarTint>
{
    public override SidebarTint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return FromElement(doc.RootElement) ?? throw new JsonException("invalid sidebar tint");
    }

    /// <summary>The tint an element encodes, or null when it has no known color.</summary>
    public static SidebarTint? FromElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("color", out var colorElement)
            || colorElement.ValueKind != JsonValueKind.String
            || !TintColors.TryParse(colorElement.GetString(), out var color))
            return null;
        var parts = TintParts.None;
        if (element.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.Array
            && on.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String))
        {
            foreach (var item in on.EnumerateArray())
                parts |= SidebarTint.PartFromName(item.GetString()!);
        }
        return new SidebarTint(color, parts);
    }

    public override void Write(Utf8JsonWriter writer, SidebarTint value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("color", value.ColorName);
        writer.WriteStartArray("on");
        foreach (var name in value.PartNames) writer.WriteStringValue(name);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

/// <summary>The snapshot field form: an invalid tint reads as null instead of failing the session
/// or workspace around it.</summary>
public sealed class LossySidebarTintConverter : JsonConverter<SidebarTint?>
{
    public override bool HandleNull => true;

    public override SidebarTint? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return SidebarTintConverter.FromElement(doc.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, SidebarTint? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }
        new SidebarTintConverter().Write(writer, value, options);
    }
}
