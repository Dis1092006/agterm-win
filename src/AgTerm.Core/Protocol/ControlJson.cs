using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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
        // App-only types such as the private window index still use reflection in managed builds.
        // This feature switch is a link-time constant, so Native AOT never roots that resolver.
        TypeInfoResolver = JsonSerializer.IsReflectionEnabledByDefault
            ? JsonTypeInfoResolver.Combine(ControlJsonContext.Default, new DefaultJsonTypeInfoResolver())
            : ControlJsonContext.Default,
    };

    private static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, TypeInfo<T>());

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize(json, TypeInfo<T>());

    public static T? Deserialize<T>(ReadOnlySpan<byte> json) => JsonSerializer.Deserialize(json, TypeInfo<T>());
}
