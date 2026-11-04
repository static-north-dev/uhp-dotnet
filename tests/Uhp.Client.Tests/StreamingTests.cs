using System.Net;
using System.Net.Http.Headers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uhp.Client;
using Uhp.Core;

namespace Uhp.Client.Tests;

[TestClass]
public sealed class StreamingTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(7)]
    [DataRow(4096)]
    public async Task HandlesArbitraryFragmentsCommentsCrLfAndMultilineData(int chunkSize)
    {
        var unknown = "data: {\"type\":\"future.progress\",\ndata: \"sequence_number\":1,\"vendor\":{\"retained\":42}}\n\n";
        using var bytes = new PacketStream([": keepalive\r\n\r\n" + TestData.Frame(TestData.Created()).Replace("\n", "\r\n", StringComparison.Ordinal) + unknown + TestData.Frame(TestData.Terminal(2))], chunkSize);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        var updates = await CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello")));
        Assert.AreEqual(3, updates.Count);
        Assert.IsInstanceOfType<UhpResponseCreated>(updates[0]);
        Assert.IsInstanceOfType<UhpUnknownEvent>(updates[1]);
        Assert.AreEqual(42, updates[1].Raw.GetProperty("vendor").GetProperty("retained").GetInt32());
        Assert.IsInstanceOfType<UhpResponseCompleted>(updates[2]);
        Assert.AreEqual(1, handler.Posts);
        Assert.IsTrue(bytes.WasDisposed);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    public async Task GapsAndDuplicatesPreserveRecoveryInformation(long next)
    {
        using var bytes = new PacketStream([TestData.Frame(TestData.Created()) + TestData.Frame(TestData.Terminal(next))]);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        var error = await Assert.ThrowsExactlyAsync<UhpStreamException>(() => CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello") { IdempotencyKey = "original" })));
        Assert.AreEqual("resp_test", error.ResponseId);
        Assert.AreEqual(0L, error.LastSequenceNumber);
        Assert.AreEqual("original", error.IdempotencyKey);
        Assert.IsInstanceOfType<UhpProtocolException>(error.InnerException);
        Assert.AreEqual(1, handler.Posts);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task EofAndTransportFailuresNeverResubmitOrCancel(bool transportFailure)
    {
        using var bytes = new PacketStream([TestData.Frame(TestData.Created())], failAtEnd: transportFailure);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        var error = await Assert.ThrowsExactlyAsync<UhpStreamException>(() => CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello"))));
        Assert.AreEqual("resp_test", error.ResponseId);
        Assert.AreEqual(0L, error.LastSequenceNumber);
        var recovered = await client.GetResponseAsync(error.ResponseId!);
        Assert.AreEqual(UhpResponseStatus.Completed, recovered.Status);
        Assert.AreEqual(1, handler.Posts);
        Assert.AreEqual(3, handler.Requests);
    }

    [TestMethod]
    [DataRow("data: invalid\n\n")]
    [DataRow("data: {\"type\":\"future\",\"sequence_number\":0}\n\n")]
    [DataRow("")]
    public async Task FailureBeforeCreatedHasNoInventedResponseId(string data)
    {
        using var bytes = new PacketStream([data]);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        var error = await Assert.ThrowsExactlyAsync<UhpStreamException>(() => CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello") { IdempotencyKey = "recover-me" })));
        Assert.IsNull(error.ResponseId);
        Assert.IsNull(error.LastSequenceNumber);
        Assert.AreEqual("recover-me", error.IdempotencyKey);
    }

    [TestMethod]
    public async Task ErrorEventIsNonterminalAndCancelledStatusIsAuthoritative()
    {
        var errorEvent = """{"type":"error","sequence_number":1,"code":"provider_warning","message":"Continuing","param":null}""";
        using var bytes = new PacketStream([TestData.Frame(TestData.Created()) + TestData.Frame(errorEvent) + TestData.Frame(TestData.Terminal(2, "cancelled", "response.failed"))]);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        var updates = await CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello")));
        Assert.IsInstanceOfType<UhpErrorEvent>(updates[1]);
        Assert.AreEqual(UhpResponseStatus.Cancelled, ((UhpResponseFailed)updates[2]).Response.Status);
    }

    [TestMethod]
    public async Task UnknownTerminalStatusRemainsInspectable()
    {
        using var bytes = new PacketStream([TestData.Frame(TestData.Created()) + TestData.Frame(TestData.Terminal(1, "vendor_terminal", "response.failed"))]);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        var updates = await CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello")));
        var terminal = (UhpResponseFailed)updates[1];
        Assert.AreEqual("vendor_terminal", terminal.Response.Status.Value);
        Assert.AreEqual("vendor_terminal", terminal.Response.Raw.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task SingleUseSurvivesEarlyDisposalAndConcurrentEnumeration()
    {
        using var bytes = new PacketStream([TestData.Frame(TestData.Created())], blockAtEnd: true);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        var stream = client.StreamAsync(new UhpTaskRequest("Hello"));

        await using (var reader = stream.GetAsyncEnumerator())
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => stream.GetAsyncEnumerator());
            Assert.IsTrue(await reader.MoveNextAsync());
        }

        Assert.ThrowsExactly<InvalidOperationException>(() => stream.GetAsyncEnumerator());
        Assert.AreEqual(1, handler.Posts);
        Assert.IsTrue(bytes.WasDisposed);
    }

    [TestMethod]
    public async Task LocalCancellationRemainsOperationCanceledAndSendsNoRemoteCancel()
    {
        using var bytes = new PacketStream([TestData.Frame(TestData.Created())], blockAtEnd: true);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        using var cancellation = new CancellationTokenSource();
        await using var reader = client.StreamAsync(new UhpTaskRequest("Hello")).GetAsyncEnumerator(cancellation.Token);
        Assert.IsTrue(await reader.MoveNextAsync());
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await reader.MoveNextAsync());
        Assert.AreEqual(1, handler.Posts);
    }

    [TestMethod]
    public async Task CancellationBeforeEnumerationIssuesNoPost()
    {
        using var bytes = new PacketStream([]);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello"), cancellation.Token)));
        Assert.AreEqual(0, handler.Posts);
    }

    [TestMethod]
    public async Task KeepaliveBytesResetInactivityTimeout()
    {
        using var bytes = new PacketStream([TestData.Frame(TestData.Created()), ": ping\n\n", ": ping\n\n", ": ping\n\n", TestData.Frame(TestData.Terminal())], delayMilliseconds: 40);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new() { StreamInactivityTimeout = TimeSpan.FromMilliseconds(150) });
        var updates = await CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello")));
        Assert.AreEqual(2, updates.Count);
    }

    [TestMethod]
    public async Task InactivityIsDiagnosableAndDoesNotCancelRemoteWork()
    {
        using var bytes = new PacketStream([TestData.Frame(TestData.Created())], blockAtEnd: true);
        using var handler = Handler(bytes);
        using var http = Http(handler);
        var client = await UhpClient.ConnectAsync(http, new() { StreamInactivityTimeout = TimeSpan.FromMilliseconds(50) });
        var error = await Assert.ThrowsExactlyAsync<UhpStreamException>(() => CollectAsync(client.StreamAsync(new UhpTaskRequest("Hello"))));
        Assert.IsInstanceOfType<TimeoutException>(error.InnerException);
        Assert.AreEqual("resp_test", error.ResponseId);
        Assert.AreEqual(1, handler.Posts);
    }

    [TestMethod]
    public void EveryKnownEventFamilyRoundTripsWithoutReflection()
    {
        var response = UhpJson.ParseResponse(TestData.Element(TestData.Response));
        var item = UhpOutputItem.TextMessage("msg_1", "text");
        var part = TestData.Element("{\"type\":\"output_text\",\"text\":\"text\"}");
        UhpEvent[] events =
        [
            new UhpResponseCreated(0, response), new UhpResponseInProgress(1, response), new UhpResponseCompleted(2, response),
            new UhpResponseIncomplete(3, response), new UhpResponseFailed(4, response), new UhpOutputItemAdded(5, item, 0),
            new UhpOutputItemDone(6, item, 0), new UhpTextDelta(7, "t", "msg_1", 0, 0), new UhpTextDone(8, "text", "msg_1", 0, 0),
            new UhpContentPartAdded(9, part, "msg_1", 0, 0), new UhpContentPartDone(10, part, "msg_1", 0, 0),
            new UhpReasoningSummaryPartAdded(11, part, "msg_1", 0, 0), new UhpReasoningSummaryPartDone(12, part, "msg_1", 0, 0),
            new UhpReasoningSummaryDelta(13, "summary", "msg_1", 0, 0), new UhpToolArgumentsDelta(14, "{", "tool_1", 1),
            new UhpToolArgumentsDone(15, "{}", "tool_1", 1), new UhpAnnotationAdded(16, TestData.Element("{\"type\":\"future_annotation\"}"), "msg_1", 0, 0, 0),
            new UhpErrorEvent(17, "warning", "Continuing")
        ];

        foreach (var update in events)
        {
            var parsed = UhpEventJson.Parse(UhpEventJson.Serialize(update));
            Assert.AreEqual(update.GetType(), parsed.GetType());
            Assert.AreEqual(update.Type, parsed.Type);
            Assert.AreEqual(update.SequenceNumber, parsed.SequenceNumber);
        }
    }

    private static ScriptedHandler Handler(Stream stream) => new((message, _) =>
    {
        if (message.Method == HttpMethod.Get)
        {
            return Task.FromResult(ScriptedHandler.Json(message.RequestUri!.AbsolutePath.EndsWith("/uhp", StringComparison.Ordinal) ? TestData.Discovery(idempotency: true) : TestData.Response));
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        response.Headers.Add("UHP-Version", UhpProtocolVersions.Current);
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return Task.FromResult(response);
    });

    private static HttpClient Http(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://example.test"), Timeout = Timeout.InfiniteTimeSpan };

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
