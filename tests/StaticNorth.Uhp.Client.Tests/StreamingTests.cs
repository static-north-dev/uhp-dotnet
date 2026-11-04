using System.Net;
using System.Net.Http.Headers;
using TUnit.Core;
using TUnit.Assertions;
using SkipTestException = TUnit.Core.Exceptions.SkipTestException;
using StaticNorth.Uhp.Client;
using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class StreamingTests
{
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(7)]
    [Arguments(4096)]
    public async Task HandlesArbitraryFragmentsCommentsCrLfAndMultilineData(int chunkSize)
    {
        var unknown = "data: {\"type\":\"future.progress\",\ndata: \"sequence_number\":1,\"vendor\":{\"retained\":42}}\n\n";
        using var bytes = new PacketStream([": keepalive\r\n\r\n" + TestData.Frame(TestData.Created()).Replace("\n", "\r\n", StringComparison.Ordinal) + unknown + TestData.Frame(TestData.Terminal(2))], chunkSize);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        var updates = await CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello")));
        await Assert.That(updates.Count).IsEqualTo(3);
        await Assert.That(updates[0]).IsAssignableTo<UhpResponseCreated>();
        await Assert.That(updates[1]).IsAssignableTo<UhpUnknownEvent>();
        await Assert.That(updates[1].Raw.GetProperty("vendor").GetProperty("retained").GetInt32()).IsEqualTo(42);
        await Assert.That(updates[2]).IsAssignableTo<UhpResponseCompleted>();
        await Assert.That(handler.Posts).IsEqualTo(1);
        await Assert.That(bytes.WasDisposed).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(2)]
    public async Task GapsAndDuplicatesPreserveRecoveryInformation(long next)
    {
        using var bytes = new PacketStream([TestData.Frame(TestData.Created()) + TestData.Frame(TestData.Terminal(next))]);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        var error = (await Assert.That((Func<Task>)(() => CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello") { IdempotencyKey = "original" })))).ThrowsExactly<UhpStreamException>())!;
        await Assert.That(error.ResponseId).IsEqualTo("resp_test");
        await Assert.That(error.LastSequenceNumber).IsEqualTo(0L);
        await Assert.That(error.IdempotencyKey).IsEqualTo("original");
        await Assert.That(error.InnerException).IsAssignableTo<UhpProtocolException>();
        await Assert.That(handler.Posts).IsEqualTo(1);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task EofAndTransportFailuresNeverResubmitOrCancel(bool transportFailure)
    {
        using var bytes = new PacketStream([TestData.Frame(TestData.Created())], failAtEnd: transportFailure);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        var error = (await Assert.That((Func<Task>)(() => CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello"))))).ThrowsExactly<UhpStreamException>())!;
        await Assert.That(error.ResponseId).IsEqualTo("resp_test");
        await Assert.That(error.LastSequenceNumber).IsEqualTo(0L);
        var recovered = await client.GetResponseAsync(error.ResponseId!);
        await Assert.That(recovered.Status).IsEqualTo(UhpResponseStatus.Completed);
        await Assert.That(handler.Posts).IsEqualTo(1);
        await Assert.That(handler.Requests).IsEqualTo(3);
    }

    [Test]
    [Arguments("data: invalid\n\n")]
    [Arguments("data: {\"type\":\"future\",\"sequence_number\":0}\n\n")]
    [Arguments("")]
    public async Task FailureBeforeCreatedHasNoInventedResponseId(string data)
    {
        using var bytes = new PacketStream([data]);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        var error = (await Assert.That((Func<Task>)(() => CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello") { IdempotencyKey = "recover-me" })))).ThrowsExactly<UhpStreamException>())!;
        await Assert.That(error.ResponseId).IsNull();
        await Assert.That(error.LastSequenceNumber).IsNull();
        await Assert.That(error.IdempotencyKey).IsEqualTo("recover-me");
    }

    [Test]
    public async Task ErrorEventIsNonterminalAndCancelledStatusIsAuthoritative()
    {
        var errorEvent = """{"type":"error","sequence_number":1,"code":"provider_warning","message":"Continuing","param":null}""";
        using var bytes = new PacketStream([TestData.Frame(TestData.Created()) + TestData.Frame(errorEvent) + TestData.Frame(TestData.Terminal(2, "cancelled", "response.failed"))]);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        var updates = await CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello")));
        await Assert.That(updates[1]).IsAssignableTo<UhpErrorEvent>();
        await Assert.That(((UhpResponseFailed)updates[2]).Response.Status).IsEqualTo(UhpResponseStatus.Cancelled);
    }

    [Test]
    public async Task UnknownTerminalStatusRemainsInspectable()
    {
        using var bytes = new PacketStream([TestData.Frame(TestData.Created()) + TestData.Frame(TestData.Terminal(1, "vendor_terminal", "response.failed"))]);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        var updates = await CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello")));
        var terminal = (UhpResponseFailed)updates[1];
        await Assert.That(terminal.Response.Status.Value).IsEqualTo("vendor_terminal");
        await Assert.That(terminal.Response.Raw.GetProperty("status").GetString()).IsEqualTo("vendor_terminal");
    }

    [Test]
    public async Task SingleUseSurvivesEarlyDisposalAndConcurrentEnumeration()
    {
        using var bytes = new PacketStream([TestData.Frame(TestData.Created())], blockAtEnd: true);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        var stream = client.StreamAsync(new UhpTaskRequest("Hello"));
        await using (var reader = stream.GetAsyncEnumerator())
        {
            await Assert.That(() => stream.GetAsyncEnumerator()).ThrowsExactly<InvalidOperationException>();
            await Assert.That(await reader.MoveNextAsync()).IsTrue();
        }

        await Assert.That(() => stream.GetAsyncEnumerator()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(handler.Posts).IsEqualTo(1);
        await Assert.That(bytes.WasDisposed).IsTrue();
    }

    [Test]
    public async Task LocalCancellationRemainsOperationCanceledAndSendsNoRemoteCancel()
    {
        using var bytes = new PacketStream([TestData.Frame(TestData.Created())], blockAtEnd: true);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        using var cancellation = new CancellationTokenSource();
        await using var reader = client.StreamAsync(new UhpTaskRequest("Hello")).GetAsyncEnumerator(cancellation.Token);
        await Assert.That(await reader.MoveNextAsync()).IsTrue();
        cancellation.Cancel();
        await Assert.That((Func<Task>)(async () => await reader.MoveNextAsync())).Throws<OperationCanceledException>();
        await Assert.That(handler.Posts).IsEqualTo(1);
    }

    [Test]
    public async Task CancellationBeforeEnumerationIssuesNoPost()
    {
        using var bytes = new PacketStream([]);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That((Func<Task>)(() => CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello"), cancellation.Token)))).Throws<OperationCanceledException>();
        await Assert.That(handler.Posts).IsEqualTo(0);
    }

    [Test]
    [NotInParallel]
    public async Task KeepaliveBytesResetInactivityTimeout()
    {
        using var bytes = new PacketStream([TestData.Frame(TestData.Created()), ": ping\n\n", ": ping\n\n", ": ping\n\n", TestData.Frame(TestData.Terminal())], delayMilliseconds: 40);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new() { StreamInactivityTimeout = TimeSpan.FromMilliseconds(150) });
        var updates = await CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello")));
        await Assert.That(updates.Count).IsEqualTo(2);
    }

    [Test]
    [NotInParallel]
    public async Task InactivityIsDiagnosableAndDoesNotCancelRemoteWork()
    {
        using var bytes = new PacketStream([TestData.Frame(TestData.Created())], blockAtEnd: true);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new() { StreamInactivityTimeout = TimeSpan.FromMilliseconds(50) });
        var error = (await Assert.That((Func<Task>)(() => CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello"))))).ThrowsExactly<UhpStreamException>())!;
        await Assert.That(error.InnerException).IsAssignableTo<TimeoutException>();
        await Assert.That(error.ResponseId).IsEqualTo("resp_test");
        await Assert.That(handler.Posts).IsEqualTo(1);
    }

    [Test]
    public async Task EveryKnownEventFamilyRoundTripsWithoutReflection()
    {
        var response = UhpJson.ParseResponse(TestData.Element(TestData.Response));
        var item = UhpOutputItem.TextMessage("msg_1", "text");
        var part = TestData.Element("{\"type\":\"output_text\",\"text\":\"text\"}");
        UhpEvent[] events = [new UhpResponseCreated(0, response), new UhpResponseInProgress(1, response), new UhpResponseCompleted(2, response), new UhpResponseIncomplete(3, response), new UhpResponseFailed(4, response), new UhpOutputItemAdded(5, item, 0), new UhpOutputItemDone(6, item, 0), new UhpTextDelta(7, "t", "msg_1", 0, 0), new UhpTextDone(8, "text", "msg_1", 0, 0), new UhpContentPartAdded(9, part, "msg_1", 0, 0), new UhpContentPartDone(10, part, "msg_1", 0, 0), new UhpReasoningSummaryPartAdded(11, part, "msg_1", 0, 0), new UhpReasoningSummaryPartDone(12, part, "msg_1", 0, 0), new UhpReasoningSummaryDelta(13, "summary", "msg_1", 0, 0), new UhpToolArgumentsDelta(14, "{", "tool_1", 1), new UhpToolArgumentsDone(15, "{}", "tool_1", 1), new UhpAnnotationAdded(16, TestData.Element("{\"type\":\"future_annotation\"}"), "msg_1", 0, 0, 0), new UhpErrorEvent(17, "warning", "Continuing")];
        foreach (var update in events)
        {
            var parsed = UhpEventJson.Parse(UhpEventJson.Serialize(update));
            await Assert.That(parsed.GetType()).IsEqualTo(update.GetType());
            await Assert.That(parsed.Type).IsEqualTo(update.Type);
            await Assert.That(parsed.SequenceNumber).IsEqualTo(update.SequenceNumber);
        }
    }

    private static ScriptedHandler Handler(Stream stream) => new((message, _) =>
    {
        if (message.Method == HttpMethod.Get)
        {
            return Task.FromResult(ScriptedHandler.Json(message.RequestUri!.AbsolutePath.EndsWith("/uhp", StringComparison.Ordinal) ? TestData.Discovery(idempotency: true) : TestData.Response));
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        };
        response.Headers.Add("UHP-Version", UhpProtocolVersions.Current);
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return Task.FromResult(response);
    });
    private static HttpClient Http(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://example.test"),
        Timeout = Timeout.InfiniteTimeSpan
    };
    private static async Task<List<UhpEvent>> CollectAsync(IAsyncEnumerable<UhpEvent> stream)
    {
        var events = new List<UhpEvent>();
        await foreach (var update in stream)
        {
            events.Add(update);
        }

        return events;
    }
}
