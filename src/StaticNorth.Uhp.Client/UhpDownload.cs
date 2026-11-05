namespace StaticNorth.Uhp.Client;

/// <summary>An owned HTTP download. Dispose after consuming Content.</summary>
public sealed class UhpDownload : IAsyncDisposable
{
    private readonly HttpResponseMessage _response;

    internal UhpDownload(HttpResponseMessage response, Stream content)
    {
        _response = response;
        Content = content;
        ContentType = response.Content.Headers.ContentType?.ToString();
        FileName = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName;
        Length = response.Content.Headers.ContentLength;
    }

    public Stream Content { get; }
    public string? ContentType { get; }
    public string? FileName { get; }
    public long? Length { get; }
    public System.Net.Http.Headers.HttpResponseHeaders Headers => _response.Headers;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Content.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _response.Dispose();
        }
    }
}
