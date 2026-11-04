using System.Net;
using System.Text.Json;
using TUnit.Core;
using TUnit.Assertions;
using SkipTestException = TUnit.Core.Exceptions.SkipTestException;
using StaticNorth.Uhp.Client;
using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class ClientTests
{
    [Test]
    [Arguments("2026-10-19")]
    [Arguments("2026-09-28")]
    [Arguments("2026-09-12")]
    public async Task DiscoversEachCompatibilityDateBeforeResolvingCredentials(string version)
    {
        var tokens = 0;
        using var handler = new ScriptedHandler(async (message, _) =>
        {
            if (message.RequestUri!.AbsolutePath.EndsWith("/uhp", StringComparison.Ordinal))
            {
                await Assert.That(message.Headers.Authorization).IsNull();
                await Assert.That(message.Headers.Contains("UHP-Version")).IsFalse();
                await Assert.That(tokens).IsEqualTo(0);
                await Assert.That(message.RequestUri.AbsoluteUri).IsEqualTo("https://example.test/api/harness/v1/uhp");
                return ScriptedHandler.Json(TestData.Discovery(version), version);
            }

            await Assert.That(message.Headers.Authorization!.Parameter).IsEqualTo("token-1");
            await Assert.That(message.Headers.GetValues("UHP-Version").Single()).IsEqualTo(version);
            return ScriptedHandler.Json(TestData.Response, version);
        });
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/api/harness")
        };
        var client = await UhpClient.ConnectAsync(http, new UhpClientOptions { GetBearerTokenAsync = _ => ValueTask.FromResult<string>("token-" + ++tokens) });
        await Assert.That(client.ProtocolVersion).IsEqualTo(version);
        var result = await client.RunAsync(new UhpTaskRequest("Hello"));
        await Assert.That(result.Id).IsEqualTo("resp_test");
        await Assert.That(tokens).IsEqualTo(1);
        await Assert.That(http.DefaultRequestHeaders.Authorization).IsNull();
    }

    [Test]
    public async Task PrefersNewestOverlapAndRediscoversVersionSpecificCapabilities()
    {
        var calls = 0;
        using var handler = new ScriptedHandler(async (message, _) =>
        {
            calls++;
            await Assert.That(message.Headers.Authorization).IsNull();
            await Assert.That(!message.Headers.Contains("UHP-Version")).IsEqualTo(calls == 1);
            var json = TestData.Discovery("2026-09-12", calls == 2).Replace("[\"2026-09-12\"]", "[\"2026-09-12\",\"2026-10-19\"]", StringComparison.Ordinal);
            return ScriptedHandler.Json(json, calls == 1 ? "2026-09-12" : "2026-10-19");
        });
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/api/harness/")
        };
        var client = await UhpClient.ConnectAsync(http, new UhpClientOptions { GetBearerTokenAsync = _ => throw new InvalidOperationException("Discovery resolved credentials.") });
        await Assert.That(client.ProtocolVersion).IsEqualTo("2026-10-19");
        await Assert.That(client.Discovery.Capabilities.Idempotency).IsTrue();
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    [Arguments("2030-01-01", null)]
    [Arguments("2026-09-12", "2026-10-19")]
    public async Task RejectsNoOverlapOrUnavailablePinnedDate(string advertised, string? pinned)
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(ScriptedHandler.Json(TestData.Discovery(advertised), advertised)));
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test")
        };
        await Assert.That((Func<Task>)(() => UhpClient.ConnectAsync(http, new UhpClientOptions { ProtocolVersion = pinned }))).ThrowsExactly<UhpProtocolException>();
        await Assert.That(handler.Posts).IsEqualTo(0);
    }

    [Test]
    [Arguments("http://example.test")]
    [Arguments("https://user:password@example.test")]
    [Arguments("https://example.test/?q=1")]
    [Arguments("https://example.test/#fragment")]
    public async Task RejectsUnsafeDeploymentRoots(string address)
    {
        using var handler = new ScriptedHandler((_, _) => throw new InvalidOperationException("Unexpected request."));
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(address)
        };
        await Assert.That((Func<Task>)(() => UhpClient.ConnectAsync(http, new UhpClientOptions()))).ThrowsExactly<ArgumentException>();
        await Assert.That(handler.Requests).IsEqualTo(0);
    }

    [Test]
    public async Task RejectsDefaultCredentialsAndUnknownPinnedVersionLocally()
    {
        using var handler = new ScriptedHandler((_, _) => throw new InvalidOperationException("Unexpected request."));
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test")
        };
        http.DefaultRequestHeaders.Authorization = new("Bearer", "secret");
        await Assert.That((Func<Task>)(() => UhpClient.ConnectAsync(http, new UhpClientOptions()))).ThrowsExactly<ArgumentException>();
        http.DefaultRequestHeaders.Authorization = null;
        await Assert.That((Func<Task>)(() => UhpClient.ConnectAsync(http, new UhpClientOptions { ProtocolVersion = "2026-10-04" }))).ThrowsExactly<ArgumentException>();
        await Assert.That(handler.Requests).IsEqualTo(0);
    }

    [Test]
    [Arguments("/api/harness")]
    [Arguments("/api/harness/")]
    public async Task PreservesDeploymentPrefixAndEscapesIdentifiers(string prefix)
    {
        using var handler = new ScriptedHandler(async (message, _) =>
        {
            if (message.RequestUri!.AbsolutePath.EndsWith("/uhp", StringComparison.Ordinal))
            {
                return ScriptedHandler.Json(TestData.Discovery());
            }

            await Assert.That(message.RequestUri.AbsoluteUri).IsEqualTo("https://example.test/api/harness/v1/responses/id%2F%25%3F%23");
            return ScriptedHandler.Json(TestData.Response);
        });
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test" + prefix)
        };
        var client = await UhpClient.ConnectAsync(http, new());
        await client.GetResponseAsync("id/%?#");
    }

    [Test]
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
            await Assert.That(body.GetProperty("stream").GetBoolean()).IsFalse();
            await Assert.That(body.GetProperty("store").GetBoolean()).IsFalse();
            await Assert.That(body.GetProperty("max_step").GetInt64()).IsEqualTo(0L);
            await Assert.That(body.GetProperty("previous_response_id").GetString()).IsEqualTo("resp_previous");
            await Assert.That(body.GetProperty("metadata").GetProperty("harness_id").GetString()).IsEqualTo("chrn_configured");
            await Assert.That(body.GetProperty("metadata").GetProperty("trace").GetString()).IsEqualTo("custom");
            await Assert.That(body.TryGetProperty("model", out _)).IsFalse();
            await Assert.That(body.TryGetProperty("max_output_tokens", out _)).IsFalse();
            await Assert.That(body.TryGetProperty("tools", out _)).IsFalse();
            await Assert.That(body.TryGetProperty("include", out _)).IsFalse();
            await Assert.That(message.Headers.GetValues("Idempotency-Key").Single()).IsEqualTo("unique-key");
            await Assert.That(body.TryGetProperty("idempotency_key", out _)).IsFalse();
            return ScriptedHandler.Json(TestData.Response);
        });
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test")
        };
        var client = await UhpClient.ConnectAsync(http, new());
        await client.RunAsync(new UhpTaskRequest("Hello") { HarnessId = "chrn_configured", Store = false, MaxStep = 0, PreviousResponseId = "resp_previous", IdempotencyKey = "unique-key", Metadata = new Dictionary<string, JsonElement> { ["trace"] = TestData.Element("\"custom\"") } });
    }

    [Test]
    public async Task RejectsConflictingHarnessOrUnsupportedIdempotencyWithoutSubmitting()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(ScriptedHandler.Json(TestData.Discovery())));
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test")
        };
        var client = await UhpClient.ConnectAsync(http, new());
        await Assert.That((Func<Task>)(() => client.RunAsync(new UhpTaskRequest("Hello") { HarnessId = "typed", Metadata = new Dictionary<string, JsonElement> { ["harness_id"] = TestData.Element("\"raw\"") } }))).ThrowsExactly<ArgumentException>();
        await Assert.That((Func<Task>)(() => client.RunAsync(new UhpTaskRequest("Hello") { IdempotencyKey = "key" }))).ThrowsExactly<InvalidOperationException>();
        await Assert.That(handler.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task PreservesVersionDriftErrorAndSupportedAlternatives()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(ScriptedHandler.Json(TestData.Discovery())));
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test")
        };
        var client = await UhpClient.ConnectAsync(http, new());
        using var drift = new ScriptedHandler((message, _) => Task.FromResult(message.RequestUri!.AbsolutePath.EndsWith("/uhp", StringComparison.Ordinal) ? ScriptedHandler.Json(TestData.Discovery()) : ScriptedHandler.Json("""{"error":{"type":"invalid_request_error","code":"unsupported_protocol_version","message":"Removed.","param":null,"detail":{"supported":["2026-09-12"]}}}""", "2026-09-12", HttpStatusCode.BadRequest)));
        using var driftHttp = new HttpClient(drift)
        {
            BaseAddress = new Uri("https://example.test")
        };
        client = await UhpClient.ConnectAsync(driftHttp, new());
        var error = (await Assert.That((Func<Task>)(() => client.GetResponseAsync("resp_test"))).ThrowsExactly<UhpApiException>())!;
        await Assert.That(error.Code).IsEqualTo("unsupported_protocol_version");
        await Assert.That(error.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(error.HasVersionMismatch).IsTrue();
        await Assert.That(error.Detail!.Value.GetProperty("supported")[0].GetString()).IsEqualTo("2026-09-12");
    }

    [Test]
    [Arguments("session_busy", 409)]
    [Arguments("rate_limited", 429)]
    [Arguments("quota_exhausted", 429)]
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
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test")
        };
        var client = await UhpClient.ConnectAsync(http, new());
        var error = (await Assert.That((Func<Task>)(() => client.RunAsync(new UhpTaskRequest("Hello")))).ThrowsExactly<UhpApiException>())!;
        await Assert.That(error.Code).IsEqualTo(code);
        await Assert.That(error.RetryAfter).IsEqualTo("2");
        await Assert.That(error.Param).IsEqualTo("input");
        await Assert.That(handler.Posts).IsEqualTo(1);
    }

    [Test]
    public async Task ProxyErrorRetainsStatusWithoutExposingItsBody()
    {
        const string secret = "private-proxy-body";
        using var handler = new ScriptedHandler((message, _) => Task.FromResult(message.RequestUri!.AbsolutePath.EndsWith("/uhp", StringComparison.Ordinal) ? ScriptedHandler.Json(TestData.Discovery()) : new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent(secret) }));
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test")
        };
        var client = await UhpClient.ConnectAsync(http, new());
        var error = (await Assert.That((Func<Task>)(() => client.RunAsync(new UhpTaskRequest("Hello")))).ThrowsExactly<UhpApiException>())!;
        await Assert.That(error.StatusCode).IsEqualTo(HttpStatusCode.BadGateway);
        await Assert.That(error.ToString().Contains(secret, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task SuccessfulVersionMismatchIsAProtocolFailure()
    {
        using var handler = new ScriptedHandler((message, _) => Task.FromResult(message.RequestUri!.AbsolutePath.EndsWith("/uhp", StringComparison.Ordinal) ? ScriptedHandler.Json(TestData.Discovery()) : ScriptedHandler.Json(TestData.Response, "2026-09-12")));
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test")
        };
        var client = await UhpClient.ConnectAsync(http, new());
        await Assert.That((Func<Task>)(() => client.GetResponseAsync("resp_test"))).ThrowsExactly<UhpProtocolException>();
    }

    [Test]
    public async Task UncertainPostPreservesKeyAndCauseWithoutRetrying()
    {
        var cause = new HttpRequestException("Disconnected");
        using var handler = new ScriptedHandler((message, _) => message.Method == HttpMethod.Get ? Task.FromResult(ScriptedHandler.Json(TestData.Discovery(idempotency: true))) : throw cause);
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test")
        };
        var client = await UhpClient.ConnectAsync(http, new());
        var error = (await Assert.That((Func<Task>)(() => client.RunAsync(new UhpTaskRequest("Hello") { IdempotencyKey = "original-key" }))).ThrowsExactly<UhpSubmissionException>())!;
        await Assert.That(error.InnerException).IsSameReferenceAs(cause);
        await Assert.That(error.IdempotencyKey).IsEqualTo("original-key");
        await Assert.That(error.ExecutionOutcomeUnknown).IsTrue();
        await Assert.That(handler.Posts).IsEqualTo(1);
    }

    [Test]
    public async Task TokenProviderFailureRemainsLocalAndClientDoesNotOwnHttpClient()
    {
        var cause = new InvalidOperationException("Credential unavailable");
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(ScriptedHandler.Json(TestData.Discovery())));
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test")
        };
        var client = await UhpClient.ConnectAsync(http, new() { GetBearerTokenAsync = _ => throw cause });
        var error = (await Assert.That((Func<Task>)(() => client.RunAsync(new UhpTaskRequest("Hello")))).ThrowsExactly<InvalidOperationException>())!;
        await Assert.That(error).IsSameReferenceAs(cause);
        await Assert.That(handler.Posts).IsEqualTo(0);
        using var stillUsable = await http.GetAsync("v1/uhp");
        await Assert.That(stillUsable.IsSuccessStatusCode).IsTrue();
    }
}
