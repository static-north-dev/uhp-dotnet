using System.Net;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class SharingApiTests
{
    [Test]
    public async Task PublicViewIsNarrowReadOnlyAndRevocableUnderAMountPrefix()
    {
        await using var server = await ServerTestHost.StartAsync(sharing: true);
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var response = await client.RunAsync(new("task"));
        var share = await client.ShareSessionAsync(response.SessionId!);
        await Assert
            .That(share.Url.StartsWith("/api/harness/v1/shared/", StringComparison.Ordinal))
            .IsTrue();
        using var readable = await http.GetAsync(share.Url);
        await Assert
            .That(readable.StatusCode)
            .IsEqualTo(HttpStatusCode.OK);
        var body = await readable.Content.ReadAsStringAsync();
        await Assert
            .That(body.Contains("Fixture response.", StringComparison.Ordinal))
            .IsTrue();
        await Assert
            .That(body.Contains("fixture-alice", StringComparison.Ordinal))
            .IsFalse();
        using var write = await http.PostAsync(share.Url, null);
        await Assert
            .That(write.IsSuccessStatusCode)
            .IsFalse();
        using var stolen = new HttpRequestMessage(HttpMethod.Get, "v1/harnesses");
        stolen.Headers.Authorization = new("Bearer", share.Id);
        using var refused = await http.SendAsync(stolen);
        await Assert
            .That(refused.StatusCode)
            .IsEqualTo(HttpStatusCode.Unauthorized);
        await client.RevokeSessionShareAsync(response.SessionId!);
        using var revoked = await http.GetAsync(share.Url);
        await Assert
            .That(revoked.StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeletionRevokesShareAndAnotherPrincipalCannotMintIt()
    {
        await using var server = await ServerTestHost.StartAsync(sessions: true, sharing: true);
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var other = await ServerTestHost.ConnectAsync(http, "bob");
        var response = await client.RunAsync(new("task"));
        await Assert
            .That((Func<Task>)(async () => await other.ShareSessionAsync(response.SessionId!)))
            .ThrowsExactly<UhpApiException>();
        var share = await client.ShareSessionAsync(response.SessionId!);
        await client.DeleteSessionAsync(response.SessionId!);
        using var deleted = await http.GetAsync(share.Url);
        await Assert
            .That(deleted.StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
    }
}
