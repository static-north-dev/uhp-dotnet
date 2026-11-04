using System.Text.Json;
using System.Text.Json.Serialization;

namespace StaticNorth.Uhp.Core.Serialization;

internal sealed class UhpRequestPayloadJsonConverter : JsonConverter<UhpRequestPayload>
{
    public override UhpRequestPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException("Task payloads are write-only snapshots.");

    public override void Write(Utf8JsonWriter writer, UhpRequestPayload value, JsonSerializerOptions options) => UhpRequestJsonWriter.Write(writer, value.Request, value.Stream);
}
