using System.Text.Json;

namespace Uhp.Core.Serialization;

internal sealed class UhpErrorJsonConverter : UhpJsonConverter<UhpError>
{
    protected override UhpError Parse(JsonElement value) => UhpJson.ParseErrorValue(value);

    protected override JsonElement Serialize(UhpError value) => UhpJson.SerializeErrorValue(value);
}
