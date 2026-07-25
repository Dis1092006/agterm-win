using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgTerm.Core.Protocol;

/// <summary>
/// Serializes an enum as its fixed wire string, mirroring a Swift raw-value enum: an unknown
/// string fails to decode (JsonException) instead of mapping to a default — callers that need
/// LOSSY behavior (e.g. the persisted watermark) catch that failure themselves.
/// </summary>
public abstract class WireEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    private readonly Dictionary<T, string> _toWire;
    private readonly Dictionary<string, T> _fromWire;

    protected WireEnumConverter(params (T Value, string Wire)[] map)
    {
        _toWire = map.ToDictionary(p => p.Value, p => p.Wire);
        _fromWire = map.ToDictionary(p => p.Wire, p => p.Value, StringComparer.Ordinal);
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var wire = reader.GetString();
        if (wire is null || !_fromWire.TryGetValue(wire, out var value))
            throw new JsonException($"unknown {typeof(T).Name} raw value: {wire}");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(_toWire[value]);
}

/// <summary>
/// Encodes a Guid the way Swift Foundation encodes UUID: the uppercase canonical string.
/// Decoding is case-insensitive.
/// </summary>
public sealed class UppercaseGuidConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Guid.Parse(reader.GetString() ?? throw new JsonException("expected a UUID string"));

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("D").ToUpperInvariant());
}
