using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using StaticNorth.Uhp.Client.Streaming;
using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Client;

public sealed class UhpClient
{
    private readonly HttpClient _http;
    private readonly Uri _root;
    private readonly Func<CancellationToken, ValueTask<string>>? _getToken;
    private readonly TimeSpan? _inactivityTimeout;

    private UhpClient(HttpClient http, Uri root, UhpClientOptions options, UhpDiscovery discovery, string version)
    {
        _http = http;
        _root = root;
        _getToken = options.GetBearerTokenAsync;
        _inactivityTimeout = options.StreamInactivityTimeout;
        Discovery = discovery;
        ProtocolVersion = version;
    }

    public UhpDiscovery Discovery { get; private set; }
    public string ProtocolVersion { get; private set; }

    public static async Task<UhpClient> ConnectAsync(HttpClient httpClient, UhpClientOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        var address = httpClient.BaseAddress;

        if (address is null || !address.IsAbsoluteUri || (address.Scheme != Uri.UriSchemeHttps && !(address.Scheme == Uri.UriSchemeHttp && address.IsLoopback)) ||
            address.UserInfo.Length > 0 || address.Query.Length > 0 || address.Fragment.Length > 0)
        {
            throw new ArgumentException("BaseAddress must be an absolute HTTPS deployment root, or HTTP on loopback, without credentials, query, or fragment.", nameof(httpClient));
        }

        if (httpClient.DefaultRequestHeaders.Authorization is not null || httpClient.DefaultRequestHeaders.Contains("Cookie") ||
            httpClient.DefaultRequestHeaders.Contains("Proxy-Authorization") || httpClient.DefaultRequestHeaders.Contains(UhpProtocol.VersionHeader))
        {
            throw new ArgumentException("Discovery requires a transport without default credentials or a default UHP-Version header.", nameof(httpClient));
        }

        if (options.ProtocolVersion is { } pinned && !UhpProtocolVersions.Supported.Contains(pinned, StringComparer.Ordinal))
        {
            throw new ArgumentException("The requested protocol date is not supported by this SDK.", nameof(options));
        }

        if (options.StreamInactivityTimeout is { } timeout && (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Stream inactivity timeout must be a positive supported timer duration.");
        }

        var root = new Uri(address.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        var client = new UhpClient(httpClient, root, options, null!, "");
        var discovery = await client.DiscoverAsync(null, ct).ConfigureAwait(false);
        var selected = options.ProtocolVersion ?? UhpProtocolVersions.Supported.FirstOrDefault(version => discovery.Versions.Contains(version, StringComparer.Ordinal));

        if (selected is null || !discovery.Versions.Contains(selected, StringComparer.Ordinal))
        {
            throw new UhpProtocolException("The client and server have no mutually supported requested protocol date.");
        }

        if (selected != discovery.DefaultVersion)
        {
            discovery = await client.DiscoverAsync(selected, ct).ConfigureAwait(false);

            if (!discovery.Versions.Contains(selected, StringComparer.Ordinal))
            {
                throw new UhpProtocolException("Selected-version discovery no longer advertises the negotiated date.");
            }
        }

        client.Discovery = discovery;
        client.ProtocolVersion = selected;
        return client;
    }

    public Task<IReadOnlyList<UhpHarness>> ListHarnessesAsync(CancellationToken ct = default) => GetAsync(UhpProtocol.HarnessesPath, UhpJson.ParseHarnessList, ct);
    public Task<UhpHarness> GetHarnessAsync(string harnessId, CancellationToken ct = default) => GetAsync($"{UhpProtocol.HarnessesPath}/{Segment(harnessId)}", UhpJson.ParseHarness, ct);
    public Task<UhpModelCatalog> GetModelsAsync(CancellationToken ct = default) => GetAsync(UhpProtocol.ModelsPath, UhpJson.ParseModelCatalog, ct);
    public Task<UhpHarnessModels> GetHarnessModelsAsync(string harnessId, CancellationToken ct = default) => GetAsync($"{UhpProtocol.HarnessesPath}/{Segment(harnessId)}/models", UhpJson.ParseHarnessModels, ct);
    public Task<UhpResponse> GetResponseAsync(string responseId, CancellationToken ct = default) => GetAsync($"{UhpProtocol.ResponsesPath}/{Segment(responseId)}", UhpJson.ParseResponse, ct);

    public async Task<UhpResponse> RunAsync(UhpTaskRequest request, CancellationToken ct = default)
    {
        var body = Prepare(request, false);
        using var response = await SendAsync(HttpMethod.Post, UhpProtocol.ResponsesPath, body, request.IdempotencyKey, ProtocolVersion, true, true, ct).ConfigureAwait(false);

        try
        {
            using var document = await ReadJsonAsync(response, ct).ConfigureAwait(false);
            return UhpJson.ParseResponse(document.RootElement);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException || exception is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new UhpSubmissionException(request.IdempotencyKey, exception);
        }
    }

    public IAsyncEnumerable<UhpEvent> StreamAsync(UhpTaskRequest request, CancellationToken ct = default)
    {
        var body = Prepare(request, true);
        return new SingleUseStream(token => ReadEventsAsync(body, request.IdempotencyKey, token), ct);
    }

    public async Task<UhpResponse> CancelResponseAsync(string responseId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"{UhpProtocol.ResponsesPath}/{Segment(responseId)}/cancel", null, null, ProtocolVersion, true, false, ct).ConfigureAwait(false);
        using var document = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        return UhpJson.ParseResponse(document.RootElement);
    }

    public async Task<UhpSessionCancelResult> CancelSessionAsync(string sessionId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"{UhpProtocol.SessionsPath}/{Segment(sessionId)}/cancel", null, null, ProtocolVersion, true, false, ct).ConfigureAwait(false);
        using var document = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        return UhpJson.ParseSessionCancelResult(document.RootElement);
    }

    private async Task<UhpDiscovery> DiscoverAsync(string? version, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, UhpProtocol.DiscoveryPath, null, null, version, false, false, ct).ConfigureAwait(false);
        using var document = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        var discovery = UhpJson.ParseDiscovery(document.RootElement);

        if (version is null && GetVersion(response) != discovery.DefaultVersion)
        {
            throw new UhpProtocolException("Discovery's response version disagrees with its default_version.");
        }

        return discovery;
    }

    private JsonElement Prepare(UhpTaskRequest request, bool stream)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IdempotencyKey is { } key)
        {
            if (!UhpProtocol.IsValidIdempotencyKey(key))
            {
                throw new ArgumentException("Idempotency keys must be nonempty printable ASCII without whitespace.", nameof(request));
            }

            if (!Discovery.Capabilities.Idempotency)
            {
                throw new InvalidOperationException("This server does not advertise task idempotency.");
            }
        }

        return UhpJson.SerializeRequest(request, stream);
    }

    private async Task<T> GetAsync<T>(string path, Func<JsonElement, T> parse, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, null, ProtocolVersion, true, false, ct).ConfigureAwait(false);
        using var document = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        return parse(document.RootElement);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonElement? body, string? key, string? version, bool authenticated, bool submission, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var message = new HttpRequestMessage(method, new Uri(_root, path));

        if (version is not null)
        {
            message.Headers.Add(UhpProtocol.VersionHeader, version);
        }

        if (key is not null)
        {
            message.Headers.Add(UhpProtocol.IdempotencyHeader, key);
        }

        if (authenticated && _getToken is not null)
        {
            var token = await _getToken(ct).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(token))
            {
                message.Headers.Authorization = new AuthenticationHeaderValue(UhpProtocol.BearerScheme, token);
            }
        }

        if (body is { } data)
        {
            message.Content = JsonContent.Create(data, UhpJsonContext.Default.JsonElement);
        }

        HttpResponseMessage response;

        try
        {
            response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (submission && (exception is HttpRequestException or IOException || exception is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw new UhpSubmissionException(key, exception);
        }

        try
        {
            if (!response.IsSuccessStatusCode)
            {
                UhpError? error = null;

                try
                {
                    using var document = await ReadJsonAsync(response, ct).ConfigureAwait(false);
                    error = UhpJson.ParseError(document.RootElement);
                }
                catch (Exception exception) when (exception is UhpProtocolException or JsonException or IOException or HttpRequestException)
                {
                    // A proxy body cannot erase the received HTTP status.
                }

                var retryAfter = response.Headers.TryGetValues(UhpProtocol.RetryAfterHeader, out var values) ? string.Join(", ", values) : null;
                throw new UhpApiException(response.StatusCode, error, retryAfter, version, GetVersion(response));
            }

            var actual = GetVersion(response);

            if (actual is null || version is not null && actual != version)
            {
                throw new UhpProtocolException("The successful response has a missing or mismatched UHP-Version.");
            }

            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async IAsyncEnumerable<UhpEvent> ReadEventsAsync(JsonElement body, string? key, [EnumeratorCancellation] CancellationToken ct)
    {
        HttpResponseMessage response;

        try
        {
            response = await SendAsync(HttpMethod.Post, UhpProtocol.ResponsesPath, body, key, ProtocolVersion, true, true, ct).ConfigureAwait(false);
        }
        catch (UhpSubmissionException exception)
        {
            throw new UhpStreamException("The stream submission outcome is unknown.", null, null, key, exception.InnerException ?? exception);
        }

        using (response)
        {
            string? responseId = null;
            long? lastSequence = null;

            if (response.Content.Headers.ContentType?.MediaType != UhpProtocol.EventStreamMediaType)
            {
                throw new UhpStreamException("Expected an SSE response after task submission.", null, null, key, new UhpProtocolException("Expected text/event-stream."));
            }

            Stream raw;

            try
            {
                raw = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                throw new UhpStreamException("Could not open the accepted task stream.", null, null, key, exception);
            }

            await using var stream = raw;
            using var inactivity = new InactivityStream(stream, _inactivityTimeout);
            await using var events = SseParser.Create(inactivity).EnumerateAsync(ct).GetAsyncEnumerator(ct);

            while (true)
            {
                UhpEvent update;

                try
                {
                    if (!await events.MoveNextAsync().ConfigureAwait(false))
                    {
                        throw new EndOfStreamException("The task stream ended without a terminal response.");
                    }

                    using var document = JsonDocument.Parse(events.Current.Data);
                    update = UhpEventJson.Parse(document.RootElement);

                    if (events.Current.EventType != "message" && events.Current.EventType != update.Type)
                    {
                        throw new UhpProtocolException("SSE event and JSON type disagree.");
                    }

                    if (update.SequenceNumber != (lastSequence is null ? 0 : checked(lastSequence.Value + 1)))
                    {
                        throw new UhpProtocolException("A task event sequence was skipped or duplicated.");
                    }

                    if (lastSequence is null && update is not UhpResponseCreated)
                    {
                        throw new UhpProtocolException("A task stream must start with response.created.");
                    }

                    if (update is UhpResponseCreated created)
                    {
                        if (responseId is not null || created.Response.Status != UhpResponseStatus.InProgress)
                        {
                            throw new UhpProtocolException("Invalid or repeated response.created.");
                        }

                        responseId = created.Response.Id;
                    }

                    if (update is UhpResponseEvent lifecycle && lifecycle.Response.Id != responseId)
                    {
                        throw new UhpProtocolException("The stream changed response IDs.");
                    }

                    if (IsTerminal(update) && update is UhpResponseEvent terminal && terminal.Response.Status == UhpResponseStatus.InProgress)
                    {
                        throw new UhpProtocolException("A terminal event cannot carry an in-progress response.");
                    }

                    lastSequence = update.SequenceNumber;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new UhpStreamException("Task progress was interrupted; retrieve the stored response when its ID is known.", responseId, lastSequence, key, exception);
                }

                yield return update;

                if (IsTerminal(update))
                {
                    yield break;
                }
            }
        }
    }

    private static bool IsTerminal(UhpEvent update) => update is UhpResponseCompleted or UhpResponseIncomplete or UhpResponseFailed;

    private static string Segment(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (id is "." or "..")
        {
            throw new ArgumentException("An identifier cannot be a dot path segment.", nameof(id));
        }

        return Uri.EscapeDataString(id);
    }

    private static string? GetVersion(HttpResponseMessage response)
    {
        if (!response.Headers.NonValidated.TryGetValues(UhpProtocol.VersionHeader, out var values) || values.Count != 1)
        {
            return null;
        }

        var enumerator = values.GetEnumerator();
        return enumerator.MoveNext() ? enumerator.Current : null;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType != UhpProtocol.JsonMediaType)
            {
                throw new UhpProtocolException("A successful UHP JSON response requires application/json.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new UhpProtocolException("The response body is not a valid UHP JSON document.", exception);
        }
    }
}
