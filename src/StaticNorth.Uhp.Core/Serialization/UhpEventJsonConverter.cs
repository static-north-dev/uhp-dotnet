using System.Text.Json;

namespace StaticNorth.Uhp.Core.Serialization;

internal sealed class UhpEventJsonConverter : UhpJsonConverter<UhpEvent>
{
    protected override UhpEvent Parse(JsonElement value) => UhpEventJson.Parse(value);

    protected override void WriteValue(Utf8JsonWriter writer, UhpEvent value) => UhpEventJson.Write(writer, value);
}
