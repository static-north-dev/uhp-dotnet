using System.Text.Json;

namespace Uhp.Core.Serialization;

internal sealed class UhpHarnessJsonConverter : UhpJsonConverter<UhpHarness>
{
    protected override UhpHarness Parse(JsonElement value) => UhpJson.ParseHarness(value);

    protected override JsonElement Serialize(UhpHarness value) => UhpJson.SerializeHarness(value);
}
