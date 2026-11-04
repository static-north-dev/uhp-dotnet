using System.Text.Json;

namespace StaticNorth.Uhp.Core.Serialization;

internal sealed class UhpHarnessModelsJsonConverter : UhpJsonConverter<UhpHarnessModels>
{
    protected override UhpHarnessModels Parse(JsonElement value) => UhpJson.ParseHarnessModels(value);

    protected override void WriteValue(Utf8JsonWriter writer, UhpHarnessModels value) => UhpJsonWriter.WriteHarnessModels(writer, value);
}
