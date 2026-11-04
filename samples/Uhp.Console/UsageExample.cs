using Uhp.Client;
using Uhp.Core;

namespace Uhp.ConsoleSample;

// Compiled against the packed public API; the package split adds the Uhp.Core import to the original specification example.
public static class UsageExample
{
    public static async Task RunAsync(Uri baseUri, string token, string harnessId, CancellationToken ct)
    {
        using var http = new HttpClient
        {
            BaseAddress = baseUri,
            Timeout = Timeout.InfiniteTimeSpan
        };
        var client = await UhpClient.ConnectAsync(http, new UhpClientOptions
        {
            GetBearerTokenAsync = _ => ValueTask.FromResult(token)
        }, ct);

        var request = new UhpTaskRequest("Summarize the README in three bullets.")
        {
            HarnessId = harnessId,
            Store = true
        };

        string? responseId = null;

        await foreach (var update in client.StreamAsync(request, ct))
        {
            switch (update)
            {
                case UhpResponseCreated created:
                    responseId = created.Response.Id;
                    break;
                case UhpTextDelta delta:
                    Console.Write(delta.Text);
                    break;
            }
        }

        var result = await client.GetResponseAsync(
            responseId ?? throw new InvalidOperationException("Missing response ID."), ct);

        if (result.Status == UhpResponseStatus.Completed)
        {
            var followUp = new UhpTaskRequest("Expand on the second bullet.")
            {
                PreviousResponseId = result.Id
            };
            var answer = await client.RunAsync(followUp, ct);
            Console.WriteLine(answer.Status);
        }
    }
}
