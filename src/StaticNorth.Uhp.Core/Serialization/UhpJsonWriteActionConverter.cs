using System.Text.Json;
using System.Text.Json.Serialization;

namespace StaticNorth.Uhp.Core.Serialization;

internal sealed class UhpJsonWriteActionConverter : JsonConverter<UhpJsonWriteAction>
{
    public override UhpJsonWriteAction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException("JSON write actions are write-only snapshots.");

    public override void Write(Utf8JsonWriter writer, UhpJsonWriteAction value, JsonSerializerOptions options) => value.Write(writer);
}
