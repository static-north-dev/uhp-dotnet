using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uhp.Core;

namespace Uhp.Client.Tests;

[TestClass]
public sealed class SerializationTests
{
    [TestMethod]
    public void TextMessageArrayUsesTheProtocolShapeAndCopiesInput()
    {
        var messages = new List<UhpUserTextMessage> { new("First"), new("Second") };
        var request = new UhpTaskRequest(messages);
        messages.Clear();
        var wire = UhpJson.SerializeRequest(request, true);
        Assert.AreEqual(2, wire.GetProperty("input").GetArrayLength());
        Assert.AreEqual("user", wire.GetProperty("input")[0].GetProperty("role").GetString());
        Assert.AreEqual("input_text", wire.GetProperty("input")[0].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.AreEqual("Second", wire.GetProperty("input")[1].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.IsFalse(wire.TryGetProperty("store", out _));
    }

    [TestMethod]
    public void PreservesUnknownOutputStatusAndExtensionsAfterDocumentDisposal()
    {
        var response = UhpJson.ParseResponse(TestData.Element("""{"id":"resp_unknown","object":"response","created_at":1795000000,"status":"vendor_waiting","model":"substituted","output":[{"type":"future_item","output":{"future":true},"content":42,"vendor":{"value":42}},{"type":"function_call","call_id":"call_1","arguments":"{}"},{"type":"function_call_output","call_id":"call_1","output":"done"}],"metadata":{"session_id":"hsess_1","requested_model":"asked","ignored_fields":["include"]},"vendor":{"retained":true}}"""));
        Assert.AreEqual("vendor_waiting", response.Status.Value);
        Assert.IsFalse(response.Status.IsTerminal);
        Assert.IsNull(response.Usage);
        Assert.AreEqual("future_item", response.Output[0].Type);
        Assert.AreEqual(42, response.Output[0].Raw.GetProperty("vendor").GetProperty("value").GetInt32());
        Assert.AreEqual(response.Output[1].CallId, response.Output[2].CallId);
        Assert.AreEqual("asked", response.Metadata!["requested_model"].GetString());
        var roundtrip = UhpJson.SerializeResponse(response);
        Assert.IsTrue(roundtrip.GetProperty("vendor").GetProperty("retained").GetBoolean());
        Assert.AreEqual("future_item", roundtrip.GetProperty("output")[0].GetProperty("type").GetString());
    }

    [TestMethod]
    [DataRow("completed")]
    [DataRow("incomplete")]
    [DataRow("failed")]
    [DataRow("cancelled")]
    public void PreservesEveryTerminalStatusAndPartialOutput(string status)
    {
        var raw = TestData.Response.Replace("\"completed\"", "\"" + status + "\"", StringComparison.Ordinal).Replace("\"output\":[]", "\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"partial\"}]}]", StringComparison.Ordinal);
        var result = UhpJson.ParseResponse(TestData.Element(raw));
        Assert.IsTrue(result.Status.IsTerminal);
        Assert.AreEqual(status, result.Status.Value);
        Assert.AreEqual("partial", result.Output[0].Text);
    }

    [TestMethod]
    public void ReadsMixedCaseHarnessFieldsAndDoesNotConvertTimestampUnits()
    {
        var harness = UhpJson.ParseHarness(TestData.Element("""{"id":"chrn_1","name":"Test","base":"future-runtime","baseLabel":"Runtime","defaultModel":"model","maxStep":4,"timeoutSeconds":30,"createdAt":1795000000123,"plugins":[{"future":true}]}"""));
        Assert.AreEqual("future-runtime", harness.Base);
        Assert.AreEqual("model", harness.DefaultModel);
        Assert.AreEqual(4L, harness.MaxStep);
        Assert.AreEqual(1_795_000_000_123, harness.CreatedAt);
        Assert.AreEqual(1_795_000_000, UhpJson.ParseResponse(TestData.Element(TestData.Response)).CreatedAt);
        Assert.IsTrue(UhpJson.SerializeHarness(harness).GetProperty("plugins")[0].GetProperty("future").GetBoolean());
        Assert.AreEqual(0, UhpJson.ParseHarnessList(TestData.Element("{\"harnesses\":[]}")).Count);
    }

    [TestMethod]
    public void ReadsBothCatalogSurfacesAndMissingCapabilities()
    {
        var catalog = UhpJson.ParseModelCatalog(TestData.Element("""{"backends":{"a":{"default":"m","models":[{"id":"m","available":false,"future":1}]}}}"""));
        Assert.IsFalse(catalog.Backends["a"].Models[0].Available);
        Assert.AreEqual("m", catalog.Backends["a"].Default);
        var harness = UhpJson.ParseHarnessModels(TestData.Element("""{"models":[{"id":"m","available":true}],"default":"m","fallback":"m"}"""));
        Assert.AreEqual("m", harness.Default);
        Assert.AreEqual("m", harness.Fallback);
        var discovery = UhpJson.ParseDiscovery(TestData.Element(TestData.Discovery().Replace("\"idempotency\":false", "\"future\":true", StringComparison.Ordinal)));
        Assert.IsFalse(discovery.Capabilities.Idempotency);
        Assert.IsTrue(discovery.Capabilities["future"]);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("null")]
    [DataRow("{\"id\":null}")]
    public void MalformedSuccessfulResponseIsAProtocolError(string json) => Assert.ThrowsExactly<UhpProtocolException>(() => UhpJson.ParseResponse(TestData.Element(json)));

    [TestMethod]
    public void DiscoveryValidatesIdentityAndDefaultMembership()
    {
        Assert.ThrowsExactly<UhpProtocolException>(() => UhpJson.ParseDiscovery(TestData.Element(TestData.Discovery().Replace("uhp.discovery", "other", StringComparison.Ordinal))));
        Assert.ThrowsExactly<UhpProtocolException>(() => UhpJson.ParseDiscovery(TestData.Element(TestData.Discovery().Replace("\"versions\":[\"2026-10-19\"]", "\"versions\":[]", StringComparison.Ordinal))));
    }
}
