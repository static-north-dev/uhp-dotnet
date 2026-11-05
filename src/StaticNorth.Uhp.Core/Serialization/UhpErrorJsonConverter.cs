using System.Text.Json;

namespace StaticNorth.Uhp.Core.Serialization;

internal sealed class UhpErrorJsonConverter : UhpJsonConverter<UhpError>
{
    protected override UhpError Parse(JsonElement value) => UhpJson.ParseErrorValue(value);

    protected override void WriteValue(Utf8JsonWriter writer, UhpError value) => UhpJsonWriter.WriteError(writer, value);
}
