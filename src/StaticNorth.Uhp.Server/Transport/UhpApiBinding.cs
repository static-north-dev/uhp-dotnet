using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Server.Transport;

internal static class UhpApiBinding
{
    public static T Backend<T>(HttpContext http, string capability) where T : class
    {
        var options = http.RequestServices.GetRequiredService<UhpServerOptions>();

        if (capability.Length > 0 && !options.Capabilities[capability])
        {
            throw new UhpServerException(404, new(UhpProtocol.InvalidRequestError, "staticnorth_capability_unavailable", "This capability is not available."));
        }

        return http.RequestServices.GetService<T>() ?? throw new UhpServerException(404, new(UhpProtocol.InvalidRequestError, "staticnorth_capability_unavailable", "This backend is not configured."));
    }

    public static async Task<T?> ReadJsonAsync<T>(HttpContext http, JsonTypeInfo<T> metadata, bool optional)
    {
        if (optional && http.Request.ContentLength is null or 0 && http.Request.ContentType is null)
        {
            return default;
        }

        if (!UhpProtocol.IsJsonContentType(http.Request.ContentType))
        {
            throw new UhpServerException(415, new(UhpProtocol.InvalidRequestError, UhpProtocol.InvalidInput, "A JSON request body is required."));
        }

        try
        {
            using var stream = new LimitedReadStream(http.Request.Body, http.RequestServices.GetRequiredService<UhpServerOptions>().MaximumRequestBodyBytes);
            var value = await JsonSerializer.DeserializeAsync(stream, metadata, http.RequestAborted).ConfigureAwait(false);

            if (value is null && !optional)
            {
                throw Invalid("The request body cannot be null.");
            }

            return value;
        }
        catch (JsonException)
        {
            throw Invalid("The request body does not match the protocol contract.");
        }
    }

    public static async Task<UhpRequestBody> ReadBodyAsync(HttpContext http)
    {
        var limit = http.RequestServices.GetRequiredService<UhpServerOptions>().MaximumUploadBytes;
        var stream = new LimitedReadStream(http.Request.Body, limit, "file_too_large");
        var contentType = http.Request.ContentType ?? "application/octet-stream";

        if (MediaTypeHeaderValue.TryParse(contentType, out var media) && media.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            var boundary = HeaderUtilities.RemoveQuotes(media.Boundary).Value;

            if (string.IsNullOrEmpty(boundary) || boundary.Length > 128)
            {
                throw Invalid("Invalid multipart boundary.");
            }

            var reader = new MultipartReader(boundary, new LimitedReadStream(http.Request.Body, checked(limit + 32_768), "file_too_large"));

            try
            {
                while (await reader.ReadNextSectionAsync(http.RequestAborted).ConfigureAwait(false) is { } section)
                {
                    if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) && (disposition.FileName.HasValue || disposition.FileNameStar.HasValue))
                    {
                        var name = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
                        return new(new LimitedReadStream(section.Body, limit, "file_too_large"), section.ContentType ?? "application/octet-stream", name);
                    }
                }
            }
            catch (InvalidDataException)
            {
                throw Invalid("Malformed multipart request.");
            }

            throw Invalid("A multipart file is required.");
        }

        return new(stream, contentType);
    }

    public static FileStreamHttpResult File(HttpContext http, UhpBinaryContent content)
    {
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(content.Content, content.ContentType, content.FileName);
    }

    public static string Route(HttpContext http, string name) => http.Request.RouteValues[name] is string value ? Uri.UnescapeDataString(value) : throw Invalid("Missing route identifier.");

    public static string? QueryString(HttpContext http, string name)
    {
        var values = http.Request.Query[name];

        if (values.Count > 1)
        {
            throw Invalid("A query parameter cannot be repeated.");
        }

        return values.Count == 0 ? null : values[0];
    }

    public static long? QueryInteger(HttpContext http, string name)
    {
        var value = QueryString(http, name);

        if (value is null)
        {
            return null;
        }

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : throw Invalid("Invalid integer query parameter.");
    }

    public static bool? QueryBoolean(HttpContext http, string name)
    {
        var value = QueryString(http, name);
        return value is null ? null : bool.TryParse(value, out var result) ? result : throw Invalid("Invalid boolean query parameter.");
    }

    public static string[]? QueryStrings(HttpContext http, string name) => QueryString(http, name)?.Split(',');

    public static JsonElement? QueryJson(HttpContext http, string name)
    {
        var value = QueryString(http, name);

        if (value is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw Invalid("Invalid JSON query parameter.");
        }
    }

    private static UhpServerException Invalid(string message) => new(400, new(UhpProtocol.InvalidRequestError, UhpProtocol.InvalidInput, message));
}
