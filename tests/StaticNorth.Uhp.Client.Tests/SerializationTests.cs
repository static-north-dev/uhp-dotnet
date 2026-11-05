using TUnit.Core;
using TUnit.Assertions;
using SkipTestException = TUnit.Core.Exceptions.SkipTestException;
using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class SerializationTests
{
    [Test]
    public async Task TextMessageArrayUsesTheProtocolShapeAndCopiesInput()
    {
        var messages = new List<UhpUserTextMessage>
        {
            new("First"),
            new("Second")
        };
        var request = new UhpTaskRequest(messages);
        messages.Clear();
        var wire = UhpJson.SerializeRequest(request, true);
        await Assert
            .That(wire
                .GetProperty("input")
                .GetArrayLength())
            .IsEqualTo(2);
        await Assert
            .That(wire.GetProperty("input")[0]
                .GetProperty("role")
                .GetString())
            .IsEqualTo("user");
        await Assert
            .That(wire.GetProperty("input")[0].GetProperty("content")[0]
                .GetProperty("type")
                .GetString())
            .IsEqualTo("input_text");
        await Assert
            .That(wire.GetProperty("input")[1].GetProperty("content")[0]
                .GetProperty("text")
                .GetString())
            .IsEqualTo("Second");
        await Assert
            .That(wire.TryGetProperty("store", out _))
            .IsFalse();
    }

    [Test]
    public async Task PreservesUnknownOutputStatusAndExtensionsAfterDocumentDisposal()
    {
        var response = UhpJson.ParseResponse(TestData.Element("""{"id":"resp_unknown","object":"response","created_at":1795000000,"status":"vendor_waiting","model":"substituted","output":[{"type":"future_item","output":{"future":true},"content":42,"vendor":{"value":42}},{"type":"function_call","call_id":"call_1","arguments":"{}"},{"type":"function_call_output","call_id":"call_1","output":"done"}],"metadata":{"session_id":"hsess_1","requested_model":"asked","ignored_fields":["include"]},"vendor":{"retained":true}}"""));
        await Assert
            .That(response.Status.Value)
            .IsEqualTo("vendor_waiting");
        await Assert
            .That(response.Status.IsTerminal)
            .IsFalse();
        await Assert
            .That(response.Usage)
            .IsNull();
        await Assert
            .That(response.Output[0].Type)
            .IsEqualTo("future_item");
        await Assert
            .That(response.Output[0].Raw
                .GetProperty("vendor")
                .GetProperty("value")
                .GetInt32())
            .IsEqualTo(42);
        await Assert
            .That(response.Output[2].CallId)
            .IsEqualTo(response.Output[1].CallId);
        await Assert
            .That(response.Metadata!["requested_model"].GetString())
            .IsEqualTo("asked");
        var roundtrip = UhpJson.SerializeResponse(response);
        await Assert
            .That(roundtrip
                .GetProperty("vendor")
                .GetProperty("retained")
                .GetBoolean())
            .IsTrue();
        await Assert
            .That(roundtrip.GetProperty("output")[0]
                .GetProperty("type")
                .GetString())
            .IsEqualTo("future_item");
    }

    [Test]
    [Arguments("completed")]
    [Arguments("incomplete")]
    [Arguments("failed")]
    [Arguments("cancelled")]
    public async Task PreservesEveryTerminalStatusAndPartialOutput(string status)
    {
        var raw = TestData.Response
            .Replace("\"completed\"", "\"" + status + "\"", StringComparison.Ordinal)
            .Replace("\"output\":[]", "\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"partial\"}]}]", StringComparison.Ordinal);
        var result = UhpJson.ParseResponse(TestData.Element(raw));
        await Assert
            .That(result.Status.IsTerminal)
            .IsTrue();
        await Assert
            .That(result.Status.Value)
            .IsEqualTo(status);
        await Assert
            .That(result.Output[0].Text)
            .IsEqualTo("partial");
    }

    [Test]
    public async Task ReadsMixedCaseHarnessFieldsAndDoesNotConvertTimestampUnits()
    {
        var harness = UhpJson.ParseHarness(TestData.Element("""{"id":"chrn_1","name":"Test","base":"future-runtime","baseLabel":"Runtime","defaultModel":"model","maxStep":4,"timeoutSeconds":30,"createdAt":1795000000123,"plugins":[{"future":true}]}"""));
        await Assert
            .That(harness.Base)
            .IsEqualTo("future-runtime");
        await Assert
            .That(harness.DefaultModel)
            .IsEqualTo("model");
        await Assert
            .That(harness.MaxStep)
            .IsEqualTo(4L);
        await Assert
            .That(harness.CreatedAt)
            .IsEqualTo(1_795_000_000_123);
        await Assert
            .That(UhpJson.ParseResponse(TestData.Element(TestData.Response)).CreatedAt)
            .IsEqualTo(1_795_000_000);
        await Assert
            .That(UhpJson
                .SerializeHarness(harness)
                .GetProperty("plugins")[0]
                .GetProperty("future")
                .GetBoolean())
            .IsTrue();
        await Assert
            .That(UhpJson.ParseHarnessList(TestData.Element("{\"harnesses\":[]}")).Count)
            .IsEqualTo(0);
    }

    [Test]
    public async Task ReadsBothCatalogSurfacesAndMissingCapabilities()
    {
        var catalog = UhpJson.ParseModelCatalog(TestData.Element("""{"backends":{"a":{"default":"m","models":[{"id":"m","available":false,"future":1}]}}}"""));
        await Assert
            .That(catalog.Backends["a"].Models[0].Available)
            .IsFalse();
        await Assert
            .That(catalog.Backends["a"].Default)
            .IsEqualTo("m");
        var harness = UhpJson.ParseHarnessModels(TestData.Element("""{"models":[{"id":"m","available":true}],"default":"m","fallback":"m"}"""));
        await Assert
            .That(harness.Default)
            .IsEqualTo("m");
        await Assert
            .That(harness.Fallback)
            .IsEqualTo("m");
        var discovery = UhpJson.ParseDiscovery(TestData.Element(TestData
            .Discovery()
            .Replace("\"idempotency\":false", "\"future\":true", StringComparison.Ordinal)));
        await Assert
            .That(discovery.Capabilities.Idempotency)
            .IsFalse();
        await Assert
            .That(discovery.Capabilities["future"])
            .IsTrue();
    }

    [Test]
    [Arguments("{}")]
    [Arguments("null")]
    [Arguments("{\"id\":null}")]
    public async Task MalformedSuccessfulResponseIsAProtocolError(string json) => await Assert
        .That(() => UhpJson.ParseResponse(TestData.Element(json)))
        .ThrowsExactly<UhpProtocolException>();
    [Test]
    public async Task DiscoveryValidatesIdentityAndDefaultMembership()
    {
        await Assert
            .That(() => UhpJson.ParseDiscovery(TestData.Element(TestData
                .Discovery()
                .Replace("uhp.discovery", "other", StringComparison.Ordinal))))
            .ThrowsExactly<UhpProtocolException>();
        await Assert
            .That(() => UhpJson.ParseDiscovery(TestData.Element(TestData
                .Discovery()
                .Replace("\"versions\":[\"2026-10-19\"]", "\"versions\":[]", StringComparison.Ordinal))))
            .ThrowsExactly<UhpProtocolException>();
    }
}
