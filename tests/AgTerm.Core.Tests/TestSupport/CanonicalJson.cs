using System.Text;
using System.Text.Json;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.TestSupport;

/// <summary>
/// Canonical (sorted-keys, compact) JSON — the C# analogue of the Swift tests'
/// JSONEncoder+sortedKeys golden-wire helper. Also the value-equality workhorse for ported
/// assertions like `decoded == original`: two protocol values are equal iff their canonical
/// JSON forms are byte-identical (serialization is deterministic).
/// </summary>
public static class CanonicalJson
{
    public static string Of<T>(T value) => Sort(ControlJson.Serialize(value));

    public static string Sort(string json)
    {
        using var doc = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               {
                   Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
               }))
        {
            WriteSorted(doc.RootElement, writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Asserts that a value's JSON round-trip is lossless AND (via canonical form)
    /// that decoded == original — the ported shape of Swift's Codable equality checks.</summary>
    public static void AssertRoundTrips<T>(T value)
    {
        var json = ControlJson.Serialize(value);
        var decoded = ControlJson.Deserialize<T>(json);
        Assert.Equal(Of(value), Of(decoded));
    }

    private static void WriteSorted(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteSorted(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteSorted(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
