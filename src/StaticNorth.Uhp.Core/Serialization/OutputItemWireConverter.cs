using System.Text.Json;
using System.Text.Json.Serialization;

namespace StaticNorth.Uhp.Core.Serialization;

// Unknown item kinds may reuse known property names with new shapes; keep the entire item opaque at the wire boundary.
internal sealed class OutputItemWireConverter : JsonConverter<OutputItemWire>
{
    public override OutputItemWire Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var value = document.RootElement;

        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(type.GetString()))
        {
            throw new JsonException("An output item requires a type.");
        }

        return new OutputItemWire { Type = type.GetString()!, RawItem = value.Clone() };
    }

    public override void Write(Utf8JsonWriter writer, OutputItemWire value, JsonSerializerOptions options) => value.RawItem.WriteTo(writer);
}
