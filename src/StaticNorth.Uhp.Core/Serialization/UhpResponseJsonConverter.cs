using System.Text.Json;

namespace StaticNorth.Uhp.Core.Serialization;

internal sealed class UhpResponseJsonConverter : UhpJsonConverter<UhpResponse>
{
    protected override UhpResponse Parse(JsonElement value) => UhpJson.ParseResponse(value);

    protected override void WriteValue(Utf8JsonWriter writer, UhpResponse value) => UhpJsonWriter.WriteResponse(writer, value);
}
