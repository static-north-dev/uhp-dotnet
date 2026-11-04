using System.Text.Json;

namespace Uhp.Core.Serialization;

internal sealed class UhpDiscoveryJsonConverter : UhpJsonConverter<UhpDiscovery>
{
    protected override UhpDiscovery Parse(JsonElement value) => UhpJson.ParseDiscovery(value);

    protected override JsonElement Serialize(UhpDiscovery value) => UhpJson.SerializeDiscovery(value);
}
