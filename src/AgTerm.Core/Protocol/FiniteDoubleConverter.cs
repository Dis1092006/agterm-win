using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgTerm.Core.Protocol;

public sealed class FiniteDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDouble();
        return double.IsFinite(value) ? value : throw new JsonException("Expected a finite number.");
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
