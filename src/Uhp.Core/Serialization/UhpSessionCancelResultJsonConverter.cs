using System.Text.Json;

namespace Uhp.Core.Serialization;

internal sealed class UhpSessionCancelResultJsonConverter : UhpJsonConverter<UhpSessionCancelResult>
{
    protected override UhpSessionCancelResult Parse(JsonElement value) => UhpJson.ParseSessionCancelResult(value);

    protected override JsonElement Serialize(UhpSessionCancelResult value) => UhpJson.SerializeSessionCancelResult(value);
}
