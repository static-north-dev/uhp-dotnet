using System.Net;
using System.Text;
using Uhp.Core;

namespace Uhp.Client.Tests;

internal sealed class ScriptedHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
{
    private int _requests;
    private int _posts;

    public int Requests => Volatile.Read(ref _requests);
    public int Posts => Volatile.Read(ref _posts);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);

        if (request.Method == HttpMethod.Post)
        {
            Interlocked.Increment(ref _posts);
        }

        return handle(request, cancellationToken);
    }

    public static HttpResponseMessage Json(string body, string version = UhpProtocolVersions.Current, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        response.Headers.Add("UHP-Version", version);
        return response;
    }
}
