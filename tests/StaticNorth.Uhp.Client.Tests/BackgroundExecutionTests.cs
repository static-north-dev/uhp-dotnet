using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class BackgroundExecutionTests
{
    [Test]
    [Arguments("2026-10-19")]
    [Arguments("2026-09-28")]
    [Arguments("2026-09-12")]
    public async Task BackgroundAcceptanceCanBeReadAndCancelled(string version)
    {
        await using var server = await ServerTestHost.StartAsync(version);
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var accepted = await client.SubmitBackgroundAsync(new("hold"));
        await Assert.That(accepted.Status).IsEqualTo(UhpResponseStatus.InProgress);
        await Assert.That((await client.GetResponseAsync(accepted.Id)).Status).IsEqualTo(UhpResponseStatus.InProgress);
        await client.CancelResponseAsync(accepted.Id);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!(await client.GetResponseAsync(accepted.Id, timeout.Token)).Status.IsTerminal)
        {
            await Task.Delay(10, timeout.Token);
        }

        await Assert.That((await client.GetResponseAsync(accepted.Id)).Status).IsEqualTo(UhpResponseStatus.Cancelled);
    }

    [Test]
    public async Task ReplayedBackgroundKeyWaitsForOriginalTerminalResult()
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var request = new UhpTaskRequest("hold") { IdempotencyKey = "background-replay" };
        var first = await client.SubmitBackgroundAsync(request);
        var replay = client.SubmitBackgroundAsync(request);
        await Task.Delay(50);
        await Assert.That(replay.IsCompleted).IsFalse();
        await client.CancelResponseAsync(first.Id);
        var final = await replay.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(final.Id).IsEqualTo(first.Id);
        await Assert.That(final.Status).IsEqualTo(UhpResponseStatus.Cancelled);
        await Assert.That(server.Backend.SubmissionCount).IsEqualTo(1);
    }

    [Test]
    public async Task BackgroundWithoutStorageDoesNotInventReadback()
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        var client = await ServerTestHost.ConnectAsync(http);
        var accepted = await client.SubmitBackgroundAsync(new("hold") { Store = false });
        await Assert.That(accepted.Status).IsEqualTo(UhpResponseStatus.InProgress);
        var error = (await Assert.That((Func<Task>)(async () => await client.GetResponseAsync(accepted.Id))).ThrowsExactly<UhpApiException>())!;
        await Assert.That(error.Code).IsEqualTo("response_not_found");
        await client.CancelResponseAsync(accepted.Id);
    }

    [Test]
    public async Task UnknownRequestFieldsAreReportedWithoutAbsentReservedFields()
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/responses")
        {
            Content = new StringContent("""{"input":"Hello","vendor_setting":{"value":42},"another_setting":true}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new("Bearer", "fixture-alice");
        using var response = await http.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var ignored = document.RootElement.GetProperty("metadata").GetProperty("ignored_fields");
        await Assert.That(ignored.GetArrayLength()).IsEqualTo(2);
        await Assert.That(ignored[0].GetString()).IsEqualTo("vendor_setting");
        await Assert.That(ignored[1].GetString()).IsEqualTo("another_setting");
    }

    [Test]
    [Arguments("\"yes\"")]
    [Arguments("null")]
    [Arguments("42")]
    public async Task MalformedBackgroundFlagDoesNotAcceptWork(string flag)
    {
        await using var server = await ServerTestHost.StartAsync();
        using var http = server.CreateHttp();
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/responses")
        {
            Content = new StringContent("{\"input\":\"Hello\",\"background\":" + flag + "}", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new("Bearer", "fixture-alice");
        using var response = await http.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(server.Backend.SubmissionCount).IsEqualTo(0);
    }
}
