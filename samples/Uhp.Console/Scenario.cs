using Uhp.Client;
using Uhp.Core;

namespace Uhp.ConsoleSample;

public static class Scenario
{
    public static async Task RunAsync(HttpClient http, string token, string harnessId, string? version, Action<string> report, CancellationToken ct)
    {
        var client = await UhpClient.ConnectAsync(http, new UhpClientOptions
        {
            ProtocolVersion = version,
            GetBearerTokenAsync = _ => ValueTask.FromResult<string>(token)
        }, ct);
        report($"discovery: {client.Discovery.ImplementationName} {client.Discovery.ImplementationVersion}; UHP {client.ProtocolVersion}");

        await client.ListHarnessesAsync(ct);
        report("list-harnesses: pass");
        await client.GetHarnessAsync(harnessId, ct);
        report("get-harness: pass");
        await client.GetModelsAsync(ct);
        report("models: pass");
        await client.GetHarnessModelsAsync(harnessId, ct);
        report("harness-models: pass");

        var result = await client.RunAsync(new UhpTaskRequest("Reply with one short sentence.") { HarnessId = harnessId, Store = true }, ct);
        report($"run: {result.Id} {result.Status}");
        Require(result.Status == UhpResponseStatus.Completed, "The non-streaming task did not complete.");

        string? responseId = null;
        UhpResponse? terminal = null;

        await foreach (var update in client.StreamAsync(new UhpTaskRequest("Reply with a short greeting.") { HarnessId = harnessId, Store = true }, ct))
        {
            switch (update)
            {
                case UhpResponseCreated created:
                    responseId = created.Response.Id;
                    break;
                case UhpTextDelta delta:
                    report($"text: {delta.Text}");
                    break;
                case UhpResponseEvent lifecycle when lifecycle.Response.Status.IsTerminal:
                    terminal = lifecycle.Response;
                    break;
            }
        }

        Require(terminal?.Status == UhpResponseStatus.Completed, "The streaming task did not complete.");
        report("stream: pass");
        var stored = await client.GetResponseAsync(responseId ?? throw new InvalidOperationException("Missing response ID."), ct);
        Require(stored.Status == terminal!.Status, "Stored and streamed terminal statuses differ.");
        report($"readback: {stored.Id} {stored.Status}");

        var continued = await client.RunAsync(new UhpTaskRequest("Expand on your previous answer.") { PreviousResponseId = stored.Id }, ct);
        Require(continued.Status == UhpResponseStatus.Completed && continued.SessionId == stored.SessionId, "Continuation did not preserve the session.");
        report("continuation: pass");

        await CancelActiveAsync(client, harnessId, false, report, ct);
        await CancelActiveAsync(client, harnessId, true, report, ct);
    }

    private static async Task CancelActiveAsync(UhpClient client, string harnessId, bool session, Action<string> report, CancellationToken ct)
    {
        string? id = null;

        await foreach (var update in client.StreamAsync(new UhpTaskRequest("Hold this task open and slowly write a long numbered list.") { HarnessId = harnessId, Store = true }, ct))
        {
            if (update is UhpResponseCreated created)
            {
                id = created.Response.Id;

                if (session)
                {
                    var sessionId = created.Response.SessionId ?? throw new InvalidOperationException("Missing session ID.");
                    var acknowledgement = await client.CancelSessionAsync(sessionId, ct);
                    Require(acknowledgement.Id == sessionId, "Session cancellation returned another ID.");
                }
                else
                {
                    await client.CancelResponseAsync(id, ct);
                }

                break;
            }
        }

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        UhpResponse snapshot;

        do
        {
            snapshot = await client.GetResponseAsync(id ?? throw new InvalidOperationException("Missing response ID."), ct);

            if (snapshot.Status.IsTerminal)
            {
                break;
            }

            await Task.Delay(50, ct);
        }
        while (DateTimeOffset.UtcNow < deadline);

        Require(snapshot.Status == UhpResponseStatus.Cancelled, "Cancellation did not produce a cancelled terminal snapshot; the task may have finished before cancellation.");
        report(session ? "cancel-session: pass" : "cancel-response: pass");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
