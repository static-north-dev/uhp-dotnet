using System.Text.Json;
using StaticNorth.Uhp.Core;
using TUnit.Assertions;
using TUnit.Core;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class ProtocolTests
{
    [Test]
    [Arguments("", false)]
    [Arguments("valid-KEY!~", true)]
    [Arguments("with space", false)]
    [Arguments("x\n", false)]
    [Arguments("\u00a0", false)]
    [Arguments(null, false)]
    public async Task IdempotencyKeysRequirePrintableAscii(string? key, bool valid)
    {
        await Assert.That(UhpProtocol.IsValidIdempotencyKey(key)).IsEqualTo(valid);
    }

    [Test]
    [Arguments("application/json", true)]
    [Arguments("APPLICATION/JSON; charset=utf-8", true)]
    [Arguments(" application/json ; charset=utf-8", true)]
    [Arguments("application/jsonx", false)]
    [Arguments(null, false)]
    public async Task JsonContentTypesAreParsedWithoutSubstringMatching(string? contentType, bool valid)
    {
        await Assert.That(UhpProtocol.IsJsonContentType(contentType)).IsEqualTo(valid);
    }

    [Test]
    public async Task HarnessMetadataUsesOrdinalWireNamesAndSnapshotsCallerValues()
    {
        var metadata = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["Harness_Id"] = TestData.Element("\"vendor\"")
        };
        var request = new UhpTaskRequest("text") { HarnessId = "typed", Metadata = metadata };
        var json = UhpJson.SerializeRequest(request, false);
        metadata.Clear();

        await Assert.That(json.GetProperty("metadata").GetProperty("harness_id").GetString()).IsEqualTo("typed");
        await Assert.That(json.GetProperty("metadata").GetProperty("Harness_Id").GetString()).IsEqualTo("vendor");
    }
}
