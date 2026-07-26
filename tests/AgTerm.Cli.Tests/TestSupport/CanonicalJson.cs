using System.Text;
using System.Text.Json;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.Cli.Tests.TestSupport;

/// <summary>
/// Canonical (sorted-keys, compact) JSON — the value-equality workhorse for ported assertions like
/// `request == ControlRequest(...)`: two protocol values are equal iff their canonical JSON forms
/// are byte-identical (serialization is deterministic). Mirrors the helper in AgTerm.Core.Tests.
/// </summary>
public static class CanonicalJson
{
    public static string Of<T>(T value) => Sort(ControlJson.Serialize(value));

    public static void AssertEqual<T>(T expected, T actual) => Assert.Equal(Of(expected), Of(actual));

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
