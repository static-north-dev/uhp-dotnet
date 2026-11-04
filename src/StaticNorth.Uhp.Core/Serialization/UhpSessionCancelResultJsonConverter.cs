using System.Text.Json;

namespace StaticNorth.Uhp.Core.Serialization;

internal sealed class UhpSessionCancelResultJsonConverter : UhpJsonConverter<UhpSessionCancelResult>
{
    protected override UhpSessionCancelResult Parse(JsonElement value) => UhpJson.ParseSessionCancelResult(value);

    protected override void WriteValue(Utf8JsonWriter writer, UhpSessionCancelResult value) => UhpJsonWriter.WriteCancellation(writer, value);
}
