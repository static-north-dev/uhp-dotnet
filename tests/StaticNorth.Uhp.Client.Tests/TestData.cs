using System.Text.Json;
using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Client.Tests;

internal static class TestData
{
    public const string Response = """{"id":"resp_test","object":"response","created_at":1795000000,"status":"completed","model":"actual-model","output":[],"metadata":{"session_id":"hsess_test"}}""";
    public static string Discovery(string version = UhpProtocolVersions.Current, bool idempotency = false) => """{"object":"uhp.discovery","protocol":"uhp","versions":["$version"],"default_version":"$version","conformance_class":"core","capabilities":{"streaming":true,"sessions":true,"cancellation":true,"idempotency":$idempotency}}""".Replace("$version", version, StringComparison.Ordinal).Replace("$idempotency", idempotency.ToString().ToLowerInvariant(), StringComparison.Ordinal);
    public static string Created(long sequence = 0) => "{\"type\":\"response.created\",\"sequence_number\":" + sequence + ",\"response\":" + Response.Replace("completed", "in_progress", StringComparison.Ordinal) + "}";
    public static string Terminal(long sequence = 1, string status = "completed", string type = "response.completed") => "{\"type\":\"" + type + "\",\"sequence_number\":" + sequence + ",\"response\":" + Response.Replace("completed", status, StringComparison.Ordinal) + "}";
    public static string Frame(string data) => "data: " + data + "\n\n";
    public static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
