using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uhp.Client;
using Uhp.Core;

namespace Uhp.Client.Tests;

[TestClass]
public sealed class ClientTests
{
    [TestMethod]
    [DataRow("2026-10-19")]
    [DataRow("2026-09-28")]
    [DataRow("2026-09-12")]
    public async Task DiscoversEachCompatibilityDateBeforeResolvingCredentials(string version)
    {
        var tokens = 0;
        using var handler = new ScriptedHandler((message, _) =>
        {
            if (message.RequestUri!.AbsolutePath.EndsWith("/uhp", StringComparison.Ordinal))
            {
                Assert.IsNull(message.Headers.Authorization);
                Assert.IsFalse(message.Headers.Contains("UHP-Version"));
                Assert.AreEqual(0, tokens);
                Assert.AreEqual("https://example.test/api/harness/v1/uhp", message.RequestUri.AbsoluteUri);
                return Task.FromResult(ScriptedHandler.Json(TestData.Discovery(version), version));
            }

            Assert.AreEqual("token-1", message.Headers.Authorization!.Parameter);
            Assert.AreEqual(version, message.Headers.GetValues("UHP-Version").Single());
            return Task.FromResult(ScriptedHandler.Json(TestData.Response, version));
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/harness") };
        var client = await UhpClient.ConnectAsync(http, new UhpClientOptions { GetBearerTokenAsync = _ => ValueTask.FromResult<string>("token-" + ++tokens) });
        Assert.AreEqual(version, client.ProtocolVersion);
        var result = await client.RunAsync(new UhpTaskRequest("Hello"));
        Assert.AreEqual("resp_test", result.Id);
        Assert.AreEqual(1, tokens);
        Assert.IsNull(http.DefaultRequestHeaders.Authorization);
    }

    [TestMethod]
    public async Task PrefersNewestOverlapAndRediscoversVersionSpecificCapabilities()
    {
        var calls = 0;
        using var handler = new ScriptedHandler((message, _) =>
        {
            calls++;
            Assert.IsNull(message.Headers.Authorization);
            Assert.AreEqual(calls == 1, !message.Headers.Contains("UHP-Version"));
            var json = TestData.Discovery("2026-09-12", calls == 2).Replace("[\"2026-09-12\"]", "[\"2026-09-12\",\"2026-10-19\"]", StringComparison.Ordinal);
            return Task.FromResult(ScriptedHandler.Json(json, calls == 1 ? "2026-09-12" : "2026-10-19"));
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/harness/") };
        var client = await UhpClient.ConnectAsync(http, new UhpClientOptions { GetBearerTokenAsync = _ => throw new AssertFailedException("Discovery resolved credentials.") });
        Assert.AreEqual("2026-10-19", client.ProtocolVersion);
        Assert.IsTrue(client.Discovery.Capabilities.Idempotency);
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    [DataRow("2030-01-01", null)]
    [DataRow("2026-09-12", "2026-10-19")]
    public async Task RejectsNoOverlapOrUnavailablePinnedDate(string advertised, string? pinned)
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(ScriptedHandler.Json(TestData.Discovery(advertised), advertised)));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        await Assert.ThrowsExactlyAsync<UhpProtocolException>(() => UhpClient.ConnectAsync(http, new UhpClientOptions { ProtocolVersion = pinned }));
        Assert.AreEqual(0, handler.Posts);
    }

    [TestMethod]
    [DataRow("http://example.test")]
    [DataRow("https://user:password@example.test")]
    [DataRow("https://example.test/?q=1")]
    [DataRow("https://example.test/#fragment")]
    public async Task RejectsUnsafeDeploymentRoots(string address)
    {
        using var handler = new ScriptedHandler((_, _) => throw new AssertFailedException("Unexpected request."));
        using var http = new HttpClient(handler) { BaseAddress = new Uri(address) };
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => UhpClient.ConnectAsync(http, new UhpClientOptions()));
        Assert.AreEqual(0, handler.Requests);
    }

    [TestMethod]
    public async Task RejectsDefaultCredentialsAndUnknownPinnedVersionLocally()
    {
        using var handler = new ScriptedHandler((_, _) => throw new AssertFailedException("Unexpected request."));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        http.DefaultRequestHeaders.Authorization = new("Bearer", "secret");
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => UhpClient.ConnectAsync(http, new UhpClientOptions()));
        http.DefaultRequestHeaders.Authorization = null;
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => UhpClient.ConnectAsync(http, new UhpClientOptions { ProtocolVersion = "2026-10-04" }));
        Assert.AreEqual(0, handler.Requests);
    }

    [TestMethod]
    [DataRow("/api/harness")]
    [DataRow("/api/harness/")]
    public async Task PreservesDeploymentPrefixAndEscapesIdentifiers(string prefix)
    {
        using var handler = new ScriptedHandler((message, _) =>
        {
            if (message.RequestUri!.AbsolutePath.EndsWith("/uhp", StringComparison.Ordinal))
            {
                return Task.FromResult(ScriptedHandler.Json(TestData.Discovery()));
            }

            Assert.AreEqual("https://example.test/api/harness/v1/responses/id%2F%25%3F%23", message.RequestUri.AbsoluteUri);
            return Task.FromResult(ScriptedHandler.Json(TestData.Response));
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test" + prefix) };
        var client = await UhpClient.ConnectAsync(http, new());
        await client.GetResponseAsync("id/%?#");
    }

    [TestMethod]
    public async Task SerializesOmittedDefaultsExplicitFalseZeroContinuationAndMetadata()
    {
        using var handler = new ScriptedHandler(async (message, cancellationToken) =>
        {
            if (message.Method == HttpMethod.Get)
            {
                return ScriptedHandler.Json(TestData.Discovery(idempotency: true));
            }

            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync());
            var body = document.RootElement;
            Assert.IsFalse(body.GetProperty("stream").GetBoolean());
            Assert.IsFalse(body.GetProperty("store").GetBoolean());
            Assert.AreEqual(0L, body.GetProperty("max_step").GetInt64());
            Assert.AreEqual("resp_previous", body.GetProperty("previous_response_id").GetString());
            Assert.AreEqual("chrn_configured", body.GetProperty("metadata").GetProperty("harness_id").GetString());
            Assert.AreEqual("custom", body.GetProperty("metadata").GetProperty("trace").GetString());
            Assert.IsFalse(body.TryGetProperty("model", out _));
            Assert.IsFalse(body.TryGetProperty("max_output_tokens", out _));
            Assert.IsFalse(body.TryGetProperty("tools", out _));
            Assert.IsFalse(body.TryGetProperty("include", out _));
            Assert.AreEqual("unique-key", message.Headers.GetValues("Idempotency-Key").Single());
            Assert.IsFalse(body.TryGetProperty("idempotency_key", out _));
            return ScriptedHandler.Json(TestData.Response);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var client = await UhpClient.ConnectAsync(http, new());
        await client.RunAsync(new UhpTaskRequest("Hello")
        {
            HarnessId = "chrn_configured",
            Store = false,
            MaxStep = 0,
            PreviousResponseId = "resp_previous",
            IdempotencyKey = "unique-key",
            Metadata = new Dictionary<string, JsonElement> { ["trace"] = TestData.Element("\"custom\"") }
        });
    }

    [TestMethod]
    public async Task RejectsConflictingHarnessOrUnsupportedIdempotencyWithoutSubmitting()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(ScriptedHandler.Json(TestData.Discovery())));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var client = await UhpClient.ConnectAsync(http, new());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.RunAsync(new UhpTaskRequest("Hello")
        {
            HarnessId = "typed",
            Metadata = new Dictionary<string, JsonElement> { ["harness_id"] = TestData.Element("\"raw\"") }
        }));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.RunAsync(new UhpTaskRequest("Hello") { IdempotencyKey = "key" }));
        Assert.AreEqual(0, handler.Posts);
    }

    [TestMethod]
    public async Task PreservesVersionDriftErrorAndSupportedAlternatives()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(ScriptedHandler.Json(TestData.Discovery())));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var client = await UhpClient.ConnectAsync(http, new());
        using var drift = new ScriptedHandler((message, _) => Task.FromResult(message.RequestUri!.AbsolutePath.EndsWith("/uhp", StringComparison.Ordinal)
            ? ScriptedHandler.Json(TestData.Discovery())
            : ScriptedHandler.Json("""{"error":{"type":"invalid_request_error","code":"unsupported_protocol_version","message":"Removed.","param":null,"detail":{"supported":["2026-09-12"]}}}""", "2026-09-12", HttpStatusCode.BadRequest)));
        using var driftHttp = new HttpClient(drift) { BaseAddress = new Uri("https://example.test") };
        client = await UhpClient.ConnectAsync(driftHttp, new());
        var error = await Assert.ThrowsExactlyAsync<UhpApiException>(() => client.GetResponseAsync("resp_test"));
        Assert.AreEqual("unsupported_protocol_version", error.Code);
        Assert.AreEqual(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.IsTrue(error.HasVersionMismatch);
        Assert.AreEqual("2026-09-12", error.Detail!.Value.GetProperty("supported")[0].GetString());
    }

    [TestMethod]
    [DataRow("session_busy", 409)]
    [DataRow("rate_limited", 429)]
    [DataRow("quota_exhausted", 429)]
    public async Task PreservesTypedApiErrorsAndRetryAfter(string code, int status)
    {
        using var handler = new ScriptedHandler((message, _) =>
        {
            if (message.RequestUri!.AbsolutePath.EndsWith("/uhp", StringComparison.Ordinal))
            {
                return Task.FromResult(ScriptedHandler.Json(TestData.Discovery()));
            }

            var response = ScriptedHandler.Json("""{"error":{"type":"rate_limit_error","code":"$code","message":"Wait.","param":"input","detail":{"retry_after_ms":50}}}""".Replace("$code", code, StringComparison.Ordinal), status: (HttpStatusCode)status);
            response.Headers.Add("Retry-After", "2");
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var client = await UhpClient.ConnectAsync(http, new());
        var error = await Assert.ThrowsExactlyAsync<UhpApiException>(() => client.RunAsync(new UhpTaskRequest("Hello")));
        Assert.AreEqual(code, error.Code);
        Assert.AreEqual("2", error.RetryAfter);
        Assert.AreEqual("input", error.Param);
        Assert.AreEqual(1, handler.Posts);
    }

    [TestMethod]
    public async Task ProxyErrorRetainsStatusWithoutExposingItsBody()
    {
        const string secret = "private-proxy-body";
        using var handler = new ScriptedHandler((message, _) => Task.FromResult(message.RequestUri!.AbsolutePath.EndsWith("/uhp", StringComparison.Ordinal)
            ? ScriptedHandler.Json(TestData.Discovery())
            : new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent(secret) }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var client = await UhpClient.ConnectAsync(http, new());
        var error = await Assert.ThrowsExactlyAsync<UhpApiException>(() => client.RunAsync(new UhpTaskRequest("Hello")));
        Assert.AreEqual(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.IsFalse(error.ToString().Contains(secret, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SuccessfulVersionMismatchIsAProtocolFailure()
    {
        using var handler = new ScriptedHandler((message, _) => Task.FromResult(message.RequestUri!.AbsolutePath.EndsWith("/uhp", StringComparison.Ordinal)
            ? ScriptedHandler.Json(TestData.Discovery()) : ScriptedHandler.Json(TestData.Response, "2026-09-12")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var client = await UhpClient.ConnectAsync(http, new());
        await Assert.ThrowsExactlyAsync<UhpProtocolException>(() => client.GetResponseAsync("resp_test"));
    }

    [TestMethod]
    public async Task UncertainPostPreservesKeyAndCauseWithoutRetrying()
    {
        var cause = new HttpRequestException("Disconnected");
        using var handler = new ScriptedHandler((message, _) => message.Method == HttpMethod.Get
            ? Task.FromResult(ScriptedHandler.Json(TestData.Discovery(idempotency: true))) : throw cause);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var client = await UhpClient.ConnectAsync(http, new());
        var error = await Assert.ThrowsExactlyAsync<UhpSubmissionException>(() => client.RunAsync(new UhpTaskRequest("Hello") { IdempotencyKey = "original-key" }));
        Assert.AreSame(cause, error.InnerException);
        Assert.AreEqual("original-key", error.IdempotencyKey);
        Assert.IsTrue(error.ExecutionOutcomeUnknown);
        Assert.AreEqual(1, handler.Posts);
    }

    [TestMethod]
    public async Task TokenProviderFailureRemainsLocalAndClientDoesNotOwnHttpClient()
    {
        var cause = new InvalidOperationException("Credential unavailable");
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(ScriptedHandler.Json(TestData.Discovery())));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var client = await UhpClient.ConnectAsync(http, new() { GetBearerTokenAsync = _ => throw cause });
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.RunAsync(new UhpTaskRequest("Hello")));
        Assert.AreSame(cause, error);
        Assert.AreEqual(0, handler.Posts);
        using var stillUsable = await http.GetAsync("v1/uhp");
        Assert.IsTrue(stillUsable.IsSuccessStatusCode);
    }
}
