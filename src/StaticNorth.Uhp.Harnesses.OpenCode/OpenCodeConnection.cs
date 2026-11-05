using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text.Json;

namespace StaticNorth.Uhp.Harnesses.OpenCode;

/// <summary>Connects to a dedicated, trusted OpenCode server.</summary>
/// <remarks>The caller owns the HTTP client and should disable automatic redirects. Use one caller per native session.</remarks>
public sealed class OpenCodeConnection(HttpClient client)
{
    private const string Sessions = "session";
    private static readonly TimeSpan AbortTimeout = TimeSpan.FromSeconds(5);

    public async Task<bool> HasModelAsync(string directory, string provider, string model, CancellationToken ct)
    {
        var catalog = await ReadAsync(HttpMethod.Get, "provider", directory, null, ct)
            .ConfigureAwait(false);
        var connected = false;

        foreach (var candidate in catalog.GetProperty("connected"u8).EnumerateArray())
        {
            if (candidate.ValueEquals(provider))
            {
                connected = true;
                break;
            }
        }

        if (!connected)
        {
            return false;
        }

        foreach (var candidate in catalog.GetProperty("all"u8).EnumerateArray())
        {
            if (candidate.GetProperty("id"u8).ValueEquals(provider))
            {
                return candidate.GetProperty("models"u8).TryGetProperty(model, out _);
            }
        }

        return false;
    }

    public async Task<string> CreateSessionAsync(string directory, CancellationToken ct)
    {
        using var body = new StringContent("""{"title":"UHP session","permission":[{"permission":"*","pattern":"*","action":"deny"}]}""",
            System.Text.Encoding.UTF8, "application/json");
        var session = await ReadAsync(HttpMethod.Post, Sessions, directory, body, ct)
            .ConfigureAwait(false);
        var id = session.GetProperty("id"u8).GetString();
        return !string.IsNullOrWhiteSpace(id) && id.StartsWith("ses", StringComparison.Ordinal)
            ? id : throw new IOException("OpenCode returned an invalid session ID.");
    }

    public async Task DeleteSessionAsync(string directory, string sessionId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Delete, SessionPath(sessionId), directory, null, ct)
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            var deleted = await ReadResponseAsync(response, ct)
                .ConfigureAwait(false);

            if (deleted.ValueKind != JsonValueKind.True)
            {
                throw new IOException("OpenCode refused to delete the session.");
            }
        }
    }

    /// <summary>Streams a text turn. Returns false after a confirmed interruption. Other failures leave session state uncertain.</summary>
    public async Task<bool> RunTurnAsync(string directory, string sessionId, string provider, string model, string prompt,
        string? instructions, Func<string, ValueTask> textDelta, CancellationToken ct)
    {
        var submitted = false;

        try
        {
            var session = SessionPath(sessionId);
            using var body = PromptBody(provider, model, prompt, instructions);
            using var response = await SendAsync(HttpMethod.Get, "event", directory, null, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentType?.MediaType != "text/event-stream")
            {
                throw new IOException("OpenCode did not open an event stream.");
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(ct)
                .ConfigureAwait(false);
            using var stop = new CancellationTokenSource();
            await using var events = SseParser
                .Create(stream, static (_, data) => JsonElement.Parse(data))
                .EnumerateAsync(stop.Token)
                .GetAsyncEnumerator(stop.Token);
            var opening = events.MoveNextAsync().AsTask();

            try
            {
                if (!await opening.WaitAsync(ct).ConfigureAwait(false) ||
                    !events.Current.Data.GetProperty("type"u8).ValueEquals("server.connected"u8))
                {
                    throw new IOException("OpenCode did not acknowledge the event subscription.");
                }
            }
            catch
            {
                await stop.CancelAsync().ConfigureAwait(false);
                await ((Task)opening).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                throw;
            }

            ct.ThrowIfCancellationRequested();
            var output = new OpenCodeTextStream(sessionId, textDelta);
            var reading = output.ReadAsync(events);
            submitted = true;
            var turn = ReadAsync(HttpMethod.Post, session + "/message", directory, body, stop.Token);
            JsonElement reply;

            try
            {
                reply = await turn.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (await AbortAndConfirmAsync(directory, session, sessionId, turn).ConfigureAwait(false))
                {
                    return false;
                }

                throw;
            }
            catch
            {
                await AbortAndConfirmAsync(directory, session, sessionId, turn).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await stop.CancelAsync().ConfigureAwait(false);
                await reading.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                await ((Task)turn).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            if (reading.IsFaulted)
            {
                await reading.ConfigureAwait(false);
            }

            await output.CompleteAsync(reply, provider, model).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested && !submitted)
        {
            return false;
        }
    }

    private async Task<bool> AbortAndConfirmAsync(string directory, string session, string sessionId, Task<JsonElement> turn)
    {
        using var timeout = new CancellationTokenSource(AbortTimeout);
        var ct = timeout.Token;

        try
        {
            while (true)
            {
                var aborted = await ReadAsync(HttpMethod.Post, session + "/abort", directory, null, ct)
                    .ConfigureAwait(false);

                if (turn.IsCompleted)
                {
                    return turn.IsCompletedSuccessfully && aborted.ValueKind == JsonValueKind.True &&
                        OpenCodeTextStream.IsAssistantReply(await turn.ConfigureAwait(false), sessionId);
                }

                // An abort can race the arrival of the POST. Keep observing the same request until it settles.
                var delay = Task.Delay(TimeSpan.FromMilliseconds(100), ct);
                await Task.WhenAny(turn, delay).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<JsonElement> ReadAsync(HttpMethod method, string path, string directory, HttpContent? body, CancellationToken ct)
    {
        using var response = await SendAsync(method, path, directory, body, ct)
            .ConfigureAwait(false);
        return await ReadResponseAsync(response, ct)
            .ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string directory, HttpContent? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path + "?directory=" + Uri.EscapeDataString(directory));
        request.Content = body;
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
    }

    private static async Task<JsonElement> ReadResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync(OpenCodeJsonContext.Default.JsonElement, ct)
            .ConfigureAwait(false);
    }

    private static string SessionPath(string id) => id.StartsWith("ses", StringComparison.Ordinal)
        ? Sessions + "/" + Uri.EscapeDataString(id)
        : throw new ArgumentException("Expected an OpenCode session ID.", nameof(id));

    private static ByteArrayContent PromptBody(string provider, string model, string prompt, string? instructions)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteStartObject("model");
            json.WriteString("providerID", provider);
            json.WriteString("modelID", model);
            json.WriteEndObject();

            if (instructions is not null)
            {
                json.WriteString("system", instructions);
            }

            json.WriteStartArray("parts");
            json.WriteStartObject();
            json.WriteString("type", "text");
            json.WriteString("text", prompt);
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
        }

        var content = new ByteArrayContent(buffer.GetBuffer(), 0, checked((int)buffer.Length));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }
}
