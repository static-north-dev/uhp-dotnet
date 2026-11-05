using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace StaticNorth.Uhp.Client.Tests;

internal sealed class OpenCodeHttpFixture : HttpMessageHandler
{
    private const string Reply = """{"info":{"role":"assistant","sessionID":"ses_fixture","parentID":"msg_current","id":"msg_answer","providerID":"fixture","modelID":"demo"},"parts":[{"type":"text","text":"Purple tulips"}]}""";
    private const string ConversationEvents = """
        {"type":"message.updated","properties":{"info":{"role":"user","id":"msg_current","sessionID":"ses_fixture"}}}
        {"type":"message.updated","properties":{"info":{"role":"assistant","id":"msg_stale","sessionID":"ses_fixture","parentID":"msg_old"}}}
        {"type":"message.updated","properties":{"info":{"role":"assistant","id":"msg_answer","sessionID":"ses_fixture","parentID":"msg_current"}}}
        {"type":"message.part.updated","properties":{"part":{"type":"text","id":"prt_other","messageID":"msg_answer","sessionID":"ses_other"}}}
        {"type":"message.part.updated","properties":{"part":{"type":"text","id":"prt_stale","messageID":"msg_stale","sessionID":"ses_fixture"}}}
        {"type":"message.part.updated","properties":{"part":{"type":"reasoning","id":"prt_reasoning","messageID":"msg_answer","sessionID":"ses_fixture"}}}
        {"type":"message.part.delta","properties":{"field":"text","partID":"prt_reasoning","messageID":"msg_answer","sessionID":"ses_fixture","delta":"internal"}}
        {"type":"message.part.updated","properties":{"part":{"type":"text","id":"prt_text","messageID":"msg_answer","sessionID":"ses_fixture"}}}
        {"type":"message.part.delta","properties":{"field":"text","partID":"prt_text","messageID":"msg_answer","sessionID":"ses_other","delta":"other session"}}
        {"type":"message.part.delta","properties":{"field":"text","partID":"prt_text","messageID":"msg_answer","sessionID":"ses_fixture","delta":"Purple"}}
        """;
    private readonly TaskCompletionSource _interrupt = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Pipe? _events;
    private int _aborts;

    public TaskCompletionSource FirstDeltaObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource HoldStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int AbortCount => Volatile.Read(ref _aborts);
    public string? Workspace { get; private set; }
    public bool Deleted { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        var directory = Uri.UnescapeDataString(uri.Query["?directory=".Length..]);

        switch (uri.AbsolutePath)
        {
            case "/provider":
                return Json("""{"connected":["fixture"],"all":[{"id":"fixture","models":{"demo":{}}}]}""");
            case "/session" when request.Method == HttpMethod.Post:
                using (var payload = await ReadBodyAsync(request, ct))
                {
                    var permission = payload.RootElement.GetProperty("permission"u8)[0];

                    if (!permission.GetProperty("action"u8).ValueEquals("deny"u8) || !permission.GetProperty("permission"u8).ValueEquals("*"u8))
                    {
                        throw new InvalidOperationException("A text session must deny tools.");
                    }
                }

                Workspace = directory;
                return Json("""{"id":"ses_fixture"}""");
        }

        if (directory != Workspace)
        {
            throw new InvalidOperationException("OpenCode requests must keep the workspace from session creation.");
        }

        switch (uri.AbsolutePath)
        {
            case "/event":
                _events?.Writer.Complete();
                _events = new Pipe();
                await EmitAsync("""{"type":"server.connected"}""", ct);
                var content = new StreamContent(_events.Reader.AsStream());
                content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
                return new(HttpStatusCode.OK) { Content = content };
            case "/session/ses_fixture/message":
                using (var payload = await ReadBodyAsync(request, ct))
                {
                    var input = payload.RootElement;
                    var model = input.GetProperty("model"u8);

                    if (!model.GetProperty("providerID"u8).ValueEquals("fixture"u8) || !model.GetProperty("modelID"u8).ValueEquals("demo"u8))
                    {
                        throw new InvalidOperationException("Expected the configured OpenCode model.");
                    }

                    var prompt = input.GetProperty("parts"u8)[0].GetProperty("text"u8).GetString();

                    if (prompt == "hold")
                    {
                        HoldStarted.TrySetResult();
                        await _interrupt.Task.WaitAsync(ct);
                    }
                    else if (prompt == "start")
                    {
                        foreach (var line in ConversationEvents.Split('\n'))
                        {
                            await EmitAsync(line, ct);
                        }

                        await FirstDeltaObserved.Task.WaitAsync(ct);
                    }
                }

                return Json(Reply);
            case "/session/ses_fixture/abort" when request.Method == HttpMethod.Post:
                if (Interlocked.Increment(ref _aborts) == 2)
                {
                    _interrupt.TrySetResult();
                }

                return Json("true");
            case "/session/ses_fixture" when request.Method == HttpMethod.Delete:
                Deleted = true;
                return Json("true");
            default:
                return new(HttpStatusCode.NotFound);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _events?.Writer.Complete();
        }

        base.Dispose(disposing);
    }

    private async Task EmitAsync(string json, CancellationToken ct) => await _events!.Writer.WriteAsync(Encoding.UTF8.GetBytes("data: " + json + "\n\n"), ct);

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static async Task<JsonDocument> ReadBodyAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await using var stream = await request.Content!.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }
}
