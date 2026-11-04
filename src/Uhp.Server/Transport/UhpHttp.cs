using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Uhp.Core;
using Uhp.Server.Validation;

namespace Uhp.Server.Transport;

internal static class UhpHttp
{
    public static UhpRequestContext GetContext(HttpContext http) => http.Features.Get<UhpRequestContext>() ?? throw new InvalidOperationException("The UHP protocol filter did not run.");

    public static async Task<Results<Ok<UhpResponse>, ServerSentEventsResult<JsonElement>>> SubmitAsync(HttpContext http, IUhpServerBackend backend)
    {
        var context = GetContext(http);
        var options = http.RequestServices.GetRequiredService<UhpServerOptions>();

        if (!string.Equals(http.Request.ContentType?.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
        {
            throw new UhpServerException(415, new UhpError("invalid_request_error", "invalid_input", "Task requests require application/json."));
        }

        if (http.Request.ContentLength > options.MaximumRequestBodyBytes)
        {
            throw new UhpServerException(413, new UhpError("invalid_request_error", "invalid_input", "The request body exceeds the configured size limit."));
        }

        if (context.IdempotencyKey is not null && !options.Capabilities.Idempotency)
        {
            throw new UhpServerException(400, new UhpError("invalid_request_error", "invalid_input", "This server does not advertise task idempotency."));
        }

        UhpTaskRequest request;
        string[] ignored;
        bool stream;

        try
        {
            using var body = new LimitedReadStream(http.Request.Body, options.MaximumRequestBodyBytes);
            using var document = await JsonDocument.ParseAsync(body, new JsonDocumentOptions { MaxDepth = 64 }, http.RequestAborted).ConfigureAwait(false);
            request = UhpJson.ParseRequest(document.RootElement, context.IdempotencyKey);
            ignored = new[] { "tools", "include" }.Where(field => document.RootElement.TryGetProperty(field, out _)).ToArray();
            stream = document.RootElement.TryGetProperty("stream", out var streamField) && streamField.GetBoolean();
        }
        catch (Exception exception) when (exception is UhpProtocolException or JsonException or ArgumentException or InvalidOperationException)
        {
            throw new UhpServerException(400, new UhpError("invalid_request_error", "invalid_input", "The request does not match the UHP text-task contract."));
        }

        var validator = http.RequestServices.GetRequiredService<UhpTaskBudgetsValidator>();
        var validation = validator.Validate(new UhpTaskBudgets(request.MaxStep ?? 0, request.MaxOutputTokens ?? 0, request.TimeoutSeconds ?? 0));

        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            throw new UhpServerException(400, new UhpError("invalid_request_error", "invalid_input", "Task budgets cannot be negative.")
            {
                Param = failure.PropertyName switch
                {
                    "MaxStep" => "max_step",
                    "MaxOutputTokens" => "max_output_tokens",
                    _ => "timeout_seconds"
                }
            });
        }

        http.RequestAborted.ThrowIfCancellationRequested();
        var stopping = http.RequestServices.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        var execution = await backend.SubmitAsync(new UhpSubmission(request, Array.AsReadOnly(ignored)), context, stopping).ConfigureAwait(false);

        if (execution.InitialResponse.Status != UhpResponseStatus.InProgress || string.IsNullOrWhiteSpace(execution.InitialResponse.Id))
        {
            throw new UhpServerException(500, new UhpError("server_error", "server_error", "The backend returned an invalid accepted response."));
        }

        if (!stream)
        {
            var response = await execution.Completion.WaitAsync(http.RequestAborted).ConfigureAwait(false);
            ValidateTerminal(execution.InitialResponse.Id, response);
            return TypedResults.Ok(response);
        }

        http.Response.Headers["X-Accel-Buffering"] = "no";
        return TypedResults.ServerSentEvents(ReadEventsAsync(execution, http.RequestAborted));
    }

    private static async IAsyncEnumerable<JsonElement> ReadEventsAsync(UhpExecution execution, [EnumeratorCancellation] CancellationToken ct)
    {
        long next = 0;

        await foreach (var update in execution.Subscribe(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (update.SequenceNumber != next || next == 0 && update is not UhpResponseCreated)
            {
                throw new UhpProtocolException("The backend returned an invalid event sequence.");
            }

            if (update is UhpResponseEvent lifecycle && lifecycle.Response.Id != execution.InitialResponse.Id)
            {
                throw new UhpProtocolException("The backend changed response IDs.");
            }

            var terminal = update is UhpResponseCompleted or UhpResponseIncomplete or UhpResponseFailed;

            if (terminal)
            {
                ValidateTerminal(execution.InitialResponse.Id, ((UhpResponseEvent)update).Response);
            }

            next++;
            yield return UhpEventJson.Serialize(update);

            if (terminal)
            {
                yield break;
            }
        }

        throw new UhpProtocolException("The backend ended progress without a terminal response.");
    }

    private static void ValidateTerminal(string id, UhpResponse response)
    {
        if (response.Id != id || !response.Status.IsTerminal)
        {
            throw new UhpProtocolException("The backend returned an invalid terminal response.");
        }
    }
}
