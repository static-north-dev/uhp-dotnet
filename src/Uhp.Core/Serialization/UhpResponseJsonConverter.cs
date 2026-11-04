using System.Text.Json;

namespace Uhp.Core.Serialization;

internal sealed class UhpResponseJsonConverter : UhpJsonConverter<UhpResponse>
{
    protected override UhpResponse Parse(JsonElement value) => UhpJson.ParseResponse(value);

    protected override JsonElement Serialize(UhpResponse value) => UhpJson.SerializeResponse(value);
}
