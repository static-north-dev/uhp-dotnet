using System.Net;
using System.Text;
using TUnit.Core;
using TUnit.Assertions;
using SkipTestException = TUnit.Core.Exceptions.SkipTestException;
using StaticNorth.Uhp.Client;
using StaticNorth.Uhp.ConsoleSample;
using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class ServerTests
{
    [Test]
    [Arguments("2026-10-19")]
    [Arguments("2026-09-28")]
    [Arguments("2026-09-12")]
    public async Task SharedSampleExercisesClientServerLifecycle(string version)
    {
        await using var server = await ServerTestHost.StartAsync(version);
        using var http = server.CreateHttp();
        var output = new List<string>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await Scenario.RunAsync(http, "fixture-alice", "chrn_fixture", null, output.Add, deadline.Token);
        await Assert
            .That(output.Contains("continuation: pass"))
            .IsTrue();
        await Assert
            .That(output.Contains("cancel-response: pass"))
            .IsTrue();
        await Assert
            .That(output.Contains("cancel-session: pass"))
            .IsTrue();
        await Assert
            .That(server.Backend.SubmissionCount)
            .IsEqualTo(5);
        await Assert
            .That(server.Backend.CancellationCount)
            .IsEqualTo(2);
    }

    [Test]
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
        await Assert
            .That(terminal.Status)
            .IsEqualTo(UhpResponseStatus.Completed);
        await Assert
            .That(server.Backend.SubmissionCount)
            .IsEqualTo(1);
        await Assert
            .That(server.Backend.CancellationCount)
            .IsEqualTo(0);
    }

    [Test]
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

        await Assert
            .That(server.Backend.SubmissionCount)
            .IsEqualTo(1);
        local.Cancel();
        await Assert
            .That((Func<Task>)(() => running))
            .Throws<OperationCanceledException>();
        var response = await WaitTerminalAsync(client, server.Backend.LatestResponseId);
        await Assert
            .That(response.Status)
            .IsEqualTo(UhpResponseStatus.Completed);
        await Assert
            .That(server.Backend.CancellationCount)
            .IsEqualTo(0);
    }

    [Test]
    public async Task CrossPrincipalReadContinueAndBothCancelScopesReturn404()
    {
        await using var server = await ServerTestHost.StartAsync();
        using var aliceHttp = server.CreateHttp();
        using var bobHttp = server.CreateHttp();
        var alice = await ServerTestHost.ConnectAsync(aliceHttp);
        var bob = await ServerTestHost.ConnectAsync(bobHttp, "bob");
        var result = await alice.RunAsync(new UhpTaskRequest("Hello"));
        var get = (await Assert
            .That((Func<Task>)(() => bob.GetResponseAsync(result.Id)))
            .ThrowsExactly<UhpApiException>())!;
        var continuation = (await Assert
            .That((Func<Task>)(() => bob.RunAsync(new UhpTaskRequest("Continue") { PreviousResponseId = result.Id })))
            .ThrowsExactly<UhpApiException>())!;
        var cancel = (await Assert
            .That((Func<Task>)(() => bob.CancelResponseAsync(result.Id)))
            .ThrowsExactly<UhpApiException>())!;
        var session = (await Assert
            .That((Func<Task>)(() => bob.CancelSessionAsync(result.SessionId!)))
            .ThrowsExactly<UhpApiException>())!;
        foreach (var error in new[]
        {
            get,
            continuation,
            cancel,
            session
        }

        )
        {
            await Assert
                .That(error.StatusCode)
                .IsEqualTo(HttpStatusCode.NotFound);
        }

        await Assert
            .That(server.Backend.SubmissionCount)
            .IsEqualTo(1);
        await Assert
            .That(server.Backend.CancellationCount)
            .IsEqualTo(0);
    }

    [Test]
    public async Task ConcurrentSameKeyReplayExecutesOnceAndKeysArePrincipalScoped()
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var request = new UhpTaskRequest("slow text")
        {
            IdempotencyKey = "same-key"
        };
        var results = await Task.WhenAll(client.RunAsync(request), client.RunAsync(request));
        await Assert
            .That(results[1].Id)
            .IsEqualTo(results[0].Id);
        await Assert
            .That(server.Backend.SubmissionCount)
            .IsEqualTo(1);
        var reused = (await Assert
            .That((Func<Task>)(() => client.RunAsync(new UhpTaskRequest("different") { IdempotencyKey = "same-key" })))
            .ThrowsExactly<UhpApiException>())!;
        await Assert
            .That(reused.Code)
            .IsEqualTo("idempotency_key_reused");
        using var bobHttp = server.CreateHttp();
        var bob = await ServerTestHost.ConnectAsync(bobHttp, "bob");
        var independent = await bob.RunAsync(request);
        await Assert
            .That(independent.Id)
            .IsNotEqualTo(results[0].Id);
        await Assert
            .That(server.Backend.SubmissionCount)
            .IsEqualTo(2);
    }

    [Test]
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

        var busy = (await Assert
            .That((Func<Task>)(() => client.RunAsync(new UhpTaskRequest("Another turn") { PreviousResponseId = first.Id })))
            .ThrowsExactly<UhpApiException>())!;
        await Assert
            .That(busy.Code)
            .IsEqualTo("session_busy");
        await client.CancelResponseAsync(id!);
        await WaitTerminalAsync(client, id!);
        var unstored = await client.RunAsync(new UhpTaskRequest("Hello") { Store = false });
        var missing = (await Assert
            .That((Func<Task>)(() => client.GetResponseAsync(unstored.Id)))
            .ThrowsExactly<UhpApiException>())!;
        await Assert
            .That(missing.Code)
            .IsEqualTo("response_not_found");
    }

    [Test]
    [Arguments("{", "application/json", 400)]
    [Arguments("null", "application/json", 400)]
    [Arguments("{\"input\":\"Hi\",\"max_step\":-1}", "application/json", 400)]
    [Arguments("{\"input\":\"Hi\"}", "text/plain", 415)]
    public async Task BindingAndValiantFailuresReturnUhpEnvelopes(string body, string contentType, int expected)
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/responses")
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        };
        message.Headers.Authorization = new("Bearer", "fixture-alice");
        using var response = await http.SendAsync(message);
        await Assert
            .That((int)response.StatusCode)
            .IsEqualTo(expected);
        await Assert
            .That(response.Headers
                .GetValues("UHP-Version")
                .Single())
            .IsEqualTo(UhpProtocolVersions.Current);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert
            .That(UhpJson.ParseError(document.RootElement).Code)
            .IsEqualTo("invalid_input");
        await Assert
            .That(server.Backend.SubmissionCount)
            .IsEqualTo(0);
    }

    [Test]
    public async Task DiscoveryIsAnonymousAuthFailuresAreUhpAndUnrelatedRoutesRemainIndependent()
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        using var discovery = await http.GetAsync("v1/uhp");
        await Assert
            .That(discovery.StatusCode)
            .IsEqualTo(HttpStatusCode.OK);
        using var unauthorized = await http.GetAsync("v1/harnesses");
        await Assert
            .That(unauthorized.StatusCode)
            .IsEqualTo(HttpStatusCode.Unauthorized);
        using var body = System.Text.Json.JsonDocument.Parse(await unauthorized.Content.ReadAsStringAsync());
        await Assert
            .That(UhpJson.ParseError(body.RootElement).Type)
            .IsEqualTo("authentication_error");
        using var health = await http.GetAsync("/health");
        await Assert
            .That(await health.Content.ReadAsStringAsync())
            .IsEqualTo("healthy");
        await Assert
            .That(health.Headers.Contains("UHP-Version"))
            .IsFalse();
    }

    [Test]
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
        await Assert
            .That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.OK);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = UhpJson.ParseResponse(document.RootElement);
        await Assert
            .That(result.Metadata!["ignored_fields"].GetArrayLength())
            .IsEqualTo(2);
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
