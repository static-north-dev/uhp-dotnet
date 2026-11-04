using System.Text.Json;

namespace Uhp.Core.Serialization;

internal sealed class UhpHarnessModelsJsonConverter : UhpJsonConverter<UhpHarnessModels>
{
    protected override UhpHarnessModels Parse(JsonElement value) => UhpJson.ParseHarnessModels(value);

    protected override JsonElement Serialize(UhpHarnessModels value) => UhpJson.SerializeHarnessModels(value);
}
