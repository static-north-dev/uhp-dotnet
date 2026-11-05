using System.Globalization;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Client;

public sealed partial class UhpClient
{
    private void RequireCapability(string capability)
    {
        if (capability.Length > 0 && !Discovery.Capabilities[capability])
        {
            throw new InvalidOperationException("The server does not advertise capability: " + capability + ".");
        }
    }

    private static HttpContent? ContractBody<T>(T? value) => value is null ? null : JsonContent.Create(value, ContractInfo<T>());

    private static JsonTypeInfo<T> ContractInfo<T>() => (JsonTypeInfo<T>)(UhpContractJsonContext.Default.GetTypeInfo(typeof(T)) ?? throw new UhpProtocolException("Missing generated contract metadata."));

    private async Task<T> SendContractAsync<T>(HttpMethod method, string path, HttpContent? body, string? key, CancellationToken ct)
    {
        using var ownedBody = body;
        using var response = await SendAsync(method, path, null, key, ProtocolVersion, true, method != HttpMethod.Get, ct, body).ConfigureAwait(false);

        if (typeof(T) == typeof(object))
        {
            return default!;
        }

        return await ReadContractAsync<T>(response, ct).ConfigureAwait(false);
    }

    private static async Task<T> ReadContractAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentType?.MediaType != UhpProtocol.JsonMediaType)
        {
            throw new UhpProtocolException("A successful UHP JSON response requires application/json.");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(stream, ContractInfo<T>(), ct).ConfigureAwait(false)
                ?? throw new UhpProtocolException("A contract response cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new UhpProtocolException("The response body is not a valid UHP contract.", exception);
        }
    }

    private async Task<T> SendBinaryAsync<T>(HttpMethod method, string path, UhpUpload? request, string? key, CancellationToken ct, bool multipart = false)
    {
        HttpContent? content = null;

        if (request is not null)
        {
            ArgumentNullException.ThrowIfNull(request.Content);
            var stream = new StreamContent(new BorrowedReadStream(request.Content));
            content = stream;
            stream.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);

            if (multipart)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
                var form = new MultipartFormDataContent();
                form.Add(stream, "file", request.FileName);
                content = form;
            }
        }

        using var ownedContent = content;
        var response = await SendAsync(method, path, null, key, ProtocolVersion, true, method != HttpMethod.Get, ct, content).ConfigureAwait(false);

        try
        {
            if (typeof(T) == typeof(UhpDownload))
            {
                var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                return (T)(object)new UhpDownload(response, stream);
            }

            using (response)
            {
                return await ReadContractAsync<T>(response, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static string AddQuery(string path, string name, string? value) => value is null ? path : path + (path.Contains('?') ? "&" : "?") + Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value);
    private static string AddQuery(string path, string name, long? value) => AddQuery(path, name, value?.ToString(CultureInfo.InvariantCulture));
    private static string AddQuery(string path, string name, bool? value) => AddQuery(path, name, value is null ? null : value.Value ? "true" : "false");
    private static string AddQuery(string path, string name, string[]? value) => AddQuery(path, name, value is null ? null : string.Join(',', value));
    private static string AddQuery(string path, string name, JsonElement? value) => AddQuery(path, name, value?.GetRawText());
}
