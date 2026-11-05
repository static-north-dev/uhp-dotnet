using System.Net;
using System.Text.Json;
using StaticNorth.Uhp.Core;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class SessionApiTests
{
    [Test]
    public async Task PaginationEndsExplicitlyWhenTheLastPageIsExactlyFull()
    {
        await using var server = await ServerTestHost.StartAsync(sessions: true);
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        await client.RunAsync(new("first"));
        await client.RunAsync(new("second"));
        var first = await client.ListSessionsAsync(new() { Limit = 1 });
        await Assert
            .That(first.Sessions.Length)
            .IsEqualTo(1);
        await Assert
            .That(first.NextCursor)
            .IsNotNull();
        var last = await client.ListSessionsAsync(new() { Limit = 1, Cursor = first.NextCursor });
        await Assert
            .That(last.Sessions.Length)
            .IsEqualTo(1);
        await Assert
            .That(last.NextCursor)
            .IsNull();
        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/sessions?limit=1&cursor=" + first.NextCursor);
        request.Headers.Authorization = new("Bearer", "fixture-alice");
        using var response = await http.SendAsync(request);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        await Assert
            .That(document.RootElement.GetProperty("next_cursor").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task SessionHistoryPreservesOrderedResponseIds()
    {
        await using var server = await ServerTestHost.StartAsync(sessions: true);
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var first = await client.RunAsync(new("first"));
        var second = await client.RunAsync(new("second") { PreviousResponseId = first.Id });
        var session = await client.GetSessionAsync(first.SessionId!);
        var turns = (await client.GetSessionTurnsAsync(session.Id)).Turns!;
        await Assert
            .That(turns.Length)
            .IsEqualTo(2);
        await Assert
            .That(turns[0].Id)
            .IsEqualTo(first.Id);
        await Assert
            .That(turns[1].Id)
            .IsEqualTo(second.Id);
        await Assert
            .That(turns[1].Status)
            .IsEqualTo("completed");
        await Assert
            .That(turns[1].CompletedAt)
            .IsNotNull();
    }

    [Test]
    public async Task DeletingSessionStopsWorkAndRemovesReadback()
    {
        await using var server = await ServerTestHost.StartAsync(sessions: true);
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var accepted = await client.SubmitBackgroundAsync(new("hold"));
        var deleted = await client.DeleteSessionAsync(accepted.SessionId!);
        await Assert
            .That(deleted.IsDeleted)
            .IsTrue();
        await Assert
            .That((await client.ListSessionsAsync()).Sessions.Length)
            .IsEqualTo(0);
        var error = (await Assert
            .That((Func<Task>)(async () => await client.GetResponseAsync(accepted.Id)))
            .ThrowsExactly<UhpApiException>())!;
        await Assert
            .That(error.Code)
            .IsEqualTo("response_not_found");
    }

    [Test]
    public async Task DeletingResponseLeavesExecutionRunningUntilExplicitCancel()
    {
        await using var server = await ServerTestHost.StartAsync(sessions: true);
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var accepted = await client.SubmitBackgroundAsync(new("hold"));
        await client.DeleteResponseAsync(accepted.Id);
        await Assert
            .That((await client.GetSessionAsync(accepted.SessionId!)).Status)
            .IsEqualTo("in_progress");
        await Assert
            .That(server.Backend.CancellationCount)
            .IsEqualTo(0);
        await client.CancelSessionAsync(accepted.SessionId!);
    }

    [Test]
    public async Task SessionOperationsArePrincipalScoped()
    {
        await using var server = await ServerTestHost.StartAsync(sessions: true);
        using var http = server.CreateHttp();
        var alice = await ServerTestHost.ConnectAsync(http);
        var bob = await ServerTestHost.ConnectAsync(http, "bob");
        var response = await alice.RunAsync(new("private"));
        await Assert
            .That((await bob.ListSessionsAsync()).Sessions.Length)
            .IsEqualTo(0);
        await Assert
            .That((Func<Task>)(async () => await bob.GetSessionAsync(response.SessionId!)))
            .ThrowsExactly<UhpApiException>();
        await Assert
            .That((Func<Task>)(async () => await bob.GetSessionTurnsAsync(response.SessionId!)))
            .ThrowsExactly<UhpApiException>();
        await Assert
            .That((Func<Task>)(async () => await bob.DeleteSessionAsync(response.SessionId!)))
            .ThrowsExactly<UhpApiException>();
        await Assert
            .That((await alice.GetSessionAsync(response.SessionId!)).Id)
            .IsEqualTo(response.SessionId);
    }

    [Test]
    public async Task InvalidQueryUsesTheUhpEnvelope()
    {
        await using var server = await ServerTestHost.StartAsync(sessions: true);
        using var http = server.CreateHttp();
        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/sessions?limit=invalid");
        request.Headers.Authorization = new("Bearer", "fixture-alice");
        using var response = await http.SendAsync(request);
        await Assert
            .That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.BadRequest);
        await Assert
            .That(response.Headers.Contains(UhpProtocol.VersionHeader))
            .IsTrue();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        await Assert
            .That(UhpJson.ParseError(document.RootElement).Code)
            .IsEqualTo(UhpProtocol.InvalidInput);
    }

    [Test]
    public async Task DeletedResponseNoLongerExposesItsInput()
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var response = await client.RunAsync(new("input"));
        var input = await client.GetResponseInputItemsAsync(response.Id);
        await Assert
            .That(input.Data!.Length)
            .IsEqualTo(1);
        await client.DeleteResponseAsync(response.Id);
        await Assert
            .That((Func<Task>)(async () => await client.GetResponseInputItemsAsync(response.Id)))
            .ThrowsExactly<UhpApiException>();
    }

    [Test]
    public async Task UnadvertisedSessionListingIsRejectedBeforeHttp()
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        await Assert
            .That((Func<Task>)(async () => await client.ListSessionsAsync()))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task GeneratedContractsOwnNestedExtensionData()
    {
        Wire.MemoryRecord record;

        using (var document = JsonDocument.Parse("""{"id":"r","memory_id":"m","type":"x.future","trust":"untrusted","content":[{"type":"x.widget","vendor":{"nested":42}}],"vendor":{"retained":true}}"""))
        {
            record = document.RootElement.Deserialize(Wire.UhpContractJsonContext.Default.MemoryRecord)!;
        }

        await Assert
            .That(record.Content![0].AdditionalData!["vendor"]
                .GetProperty("nested")
                .GetInt32())
            .IsEqualTo(42);
        await Assert
            .That(record.AdditionalData!["vendor"]
                .GetProperty("retained")
                .GetBoolean())
            .IsTrue();
    }
}
