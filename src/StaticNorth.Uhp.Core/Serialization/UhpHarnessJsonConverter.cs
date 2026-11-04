using System.Text.Json;

namespace StaticNorth.Uhp.Core.Serialization;

internal sealed class UhpHarnessJsonConverter : UhpJsonConverter<UhpHarness>
{
    protected override UhpHarness Parse(JsonElement value) => UhpJson.ParseHarness(value);

    protected override void WriteValue(Utf8JsonWriter writer, UhpHarness value) => UhpJsonWriter.WriteHarness(writer, value);
}
