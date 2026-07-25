using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgTerm.Core.Protocol;

/// <summary>
/// The one serializer configuration for the control wire and persistence. Mirrors Swift Codable
/// output: property names are declared explicitly per property (camelCase, byte-for-byte), nil
/// optionals are omitted (never emitted as null), output is compact.
/// </summary>
public static class ControlJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
