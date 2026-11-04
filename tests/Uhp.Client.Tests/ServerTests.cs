using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uhp.Client;
using Uhp.ConsoleSample;
using Uhp.Core;

namespace Uhp.Client.Tests;

[TestClass]
public sealed class ServerTests
{
    [TestMethod]
    [DataRow("2026-10-19")]
    [DataRow("2026-09-28")]
    [DataRow("2026-09-12")]
    public async Task SharedSampleExercisesClientServerLifecycle(string version)
    {
        await using var server = await ServerTestHost.StartAsync(version);
        using var http = server.CreateHttp();
        var output = new List<string>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await Scenario.RunAsync(http, "fixture-alice", "chrn_fixture", null, output.Add, deadline.Token);
        Assert.IsTrue(output.Contains("continuation: pass"));
        Assert.IsTrue(output.Contains("cancel-response: pass"));
        Assert.IsTrue(output.Contains("cancel-session: pass"));
        Assert.AreEqual(5, server.Backend.SubmissionCount);
        Assert.AreEqual(2, server.Backend.CancellationCount);
    }

    [TestMethod]
    public async Task StreamDisposalDetachesReaderWhileStoredWorkCompletes()
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        string? id = null;

        await foreach (var update in client.StreamAsync(new UhpTaskRequest("slow text") { Store = true }))
        {
            id = ((UhpResponseCreated)update).Response.Id;
            break;
        }

        var terminal = await WaitTerminalAsync(client, id!);
        Assert.AreEqual(UhpResponseStatus.Completed, terminal.Status);
        Assert.AreEqual(1, server.Backend.SubmissionCount);
        Assert.AreEqual(0, server.Backend.CancellationCount);
    }

    [TestMethod]
    public async Task NonstreamingDisconnectDoesNotCancelExecution()
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        using var local = new CancellationTokenSource();
        var running = client.RunAsync(new UhpTaskRequest("slow text") { Store = true }, local.Token);

        for (var attempt = 0; server.Backend.SubmissionCount == 0 && attempt < 100; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.AreEqual(1, server.Backend.SubmissionCount);
        local.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => running);
        var response = await WaitTerminalAsync(client, server.Backend.LatestResponseId);
        Assert.AreEqual(UhpResponseStatus.Completed, response.Status);
        Assert.AreEqual(0, server.Backend.CancellationCount);
    }

    [TestMethod]
    public async Task CrossPrincipalReadContinueAndBothCancelScopesReturn404()
    {
        await using var server = await ServerTestHost.StartAsync();
        using var aliceHttp = server.CreateHttp();
        using var bobHttp = server.CreateHttp();
        var alice = await ServerTestHost.ConnectAsync(aliceHttp);
        var bob = await ServerTestHost.ConnectAsync(bobHttp, "bob");
        var result = await alice.RunAsync(new UhpTaskRequest("Hello"));
        var get = await Assert.ThrowsExactlyAsync<UhpApiException>(() => bob.GetResponseAsync(result.Id));
        var continuation = await Assert.ThrowsExactlyAsync<UhpApiException>(() => bob.RunAsync(new UhpTaskRequest("Continue") { PreviousResponseId = result.Id }));
        var cancel = await Assert.ThrowsExactlyAsync<UhpApiException>(() => bob.CancelResponseAsync(result.Id));
        var session = await Assert.ThrowsExactlyAsync<UhpApiException>(() => bob.CancelSessionAsync(result.SessionId!));

        foreach (var error in new[] { get, continuation, cancel, session })
        {
            Assert.AreEqual(HttpStatusCode.NotFound, error.StatusCode);
        }

        Assert.AreEqual(1, server.Backend.SubmissionCount);
        Assert.AreEqual(0, server.Backend.CancellationCount);
    }

    [TestMethod]
    public async Task ConcurrentSameKeyReplayExecutesOnceAndKeysArePrincipalScoped()
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var request = new UhpTaskRequest("slow text") { IdempotencyKey = "same-key" };
        var results = await Task.WhenAll(client.RunAsync(request), client.RunAsync(request));
        Assert.AreEqual(results[0].Id, results[1].Id);
        Assert.AreEqual(1, server.Backend.SubmissionCount);
        var reused = await Assert.ThrowsExactlyAsync<UhpApiException>(() => client.RunAsync(new UhpTaskRequest("different") { IdempotencyKey = "same-key" }));
        Assert.AreEqual("idempotency_key_reused", reused.Code);
        using var bobHttp = server.CreateHttp();
        var bob = await ServerTestHost.ConnectAsync(bobHttp, "bob");
        var independent = await bob.RunAsync(request);
        Assert.AreNotEqual(results[0].Id, independent.Id);
        Assert.AreEqual(2, server.Backend.SubmissionCount);
    }

    [TestMethod]
    public async Task ActiveContinuationReturnsSessionBusyAndStoreFalseLookupIsPreserved()
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var first = await client.RunAsync(new UhpTaskRequest("Hello"));
        string? id = null;

        await foreach (var update in client.StreamAsync(new UhpTaskRequest("hold") { PreviousResponseId = first.Id }))
        {
            id = ((UhpResponseCreated)update).Response.Id;
            break;
        }

        var busy = await Assert.ThrowsExactlyAsync<UhpApiException>(() => client.RunAsync(new UhpTaskRequest("Another turn") { PreviousResponseId = first.Id }));
        Assert.AreEqual("session_busy", busy.Code);
        await client.CancelResponseAsync(id!);
        await WaitTerminalAsync(client, id!);
        var unstored = await client.RunAsync(new UhpTaskRequest("Hello") { Store = false });
        var missing = await Assert.ThrowsExactlyAsync<UhpApiException>(() => client.GetResponseAsync(unstored.Id));
        Assert.AreEqual("response_not_found", missing.Code);
    }

    [TestMethod]
    [DataRow("{", "application/json", 400)]
    [DataRow("null", "application/json", 400)]
    [DataRow("{\"input\":\"Hi\",\"max_step\":-1}", "application/json", 400)]
    [DataRow("{\"input\":\"Hi\"}", "text/plain", 415)]
    public async Task BindingAndValiantFailuresReturnUhpEnvelopes(string body, string contentType, int expected)
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/responses") { Content = new StringContent(body, Encoding.UTF8, contentType) };
        message.Headers.Authorization = new("Bearer", "fixture-alice");
        using var response = await http.SendAsync(message);
        Assert.AreEqual(expected, (int)response.StatusCode);
        Assert.AreEqual(UhpProtocolVersions.Current, response.Headers.GetValues("UHP-Version").Single());
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("invalid_input", UhpJson.ParseError(document.RootElement).Code);
        Assert.AreEqual(0, server.Backend.SubmissionCount);
    }

    [TestMethod]
    public async Task DiscoveryIsAnonymousAuthFailuresAreUhpAndUnrelatedRoutesRemainIndependent()
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        using var discovery = await http.GetAsync("v1/uhp");
        Assert.AreEqual(HttpStatusCode.OK, discovery.StatusCode);
        using var unauthorized = await http.GetAsync("v1/harnesses");
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await unauthorized.Content.ReadAsStringAsync());
        Assert.AreEqual("authentication_error", UhpJson.ParseError(body.RootElement).Type);
        using var health = await http.GetAsync("/health");
        Assert.AreEqual("healthy", await health.Content.ReadAsStringAsync());
        Assert.IsFalse(health.Headers.Contains("UHP-Version"));
    }

    [TestMethod]
    public async Task ReservedFieldsAreAcceptedAndReportedAsIgnored()
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/responses")
        {
            Content = new StringContent("{\"input\":\"Hello\",\"tools\":[],\"include\":[\"anything\"]}", Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new("Bearer", "fixture-alice");
        using var response = await http.SendAsync(message);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = UhpJson.ParseResponse(document.RootElement);
        Assert.AreEqual(2, result.Metadata!["ignored_fields"].GetArrayLength());
    }

    private static async Task<UhpResponse> WaitTerminalAsync(UhpClient client, string id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (true)
        {
            var response = await client.GetResponseAsync(id, timeout.Token);

            if (response.Status.IsTerminal)
            {
                return response;
            }

            await Task.Delay(10, timeout.Token);
        }
    }
}
