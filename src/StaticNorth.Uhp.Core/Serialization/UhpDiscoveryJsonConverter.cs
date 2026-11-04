using System.Text.Json;

namespace StaticNorth.Uhp.Core.Serialization;

internal sealed class UhpDiscoveryJsonConverter : UhpJsonConverter<UhpDiscovery>
{
    protected override UhpDiscovery Parse(JsonElement value) => UhpJson.ParseDiscovery(value);

    protected override void WriteValue(Utf8JsonWriter writer, UhpDiscovery value) => UhpJsonWriter.WriteDiscovery(writer, value);
}
