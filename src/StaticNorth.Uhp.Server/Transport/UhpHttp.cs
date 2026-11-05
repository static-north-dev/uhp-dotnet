using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Server.Validation;

namespace StaticNorth.Uhp.Server.Transport;

internal static class UhpHttp
{
    private static readonly IReadOnlyList<string> ToolsIgnored = Array.AsReadOnly<string>(["tools"]);
    private static readonly IReadOnlyList<string> IncludeIgnored = Array.AsReadOnly<string>(["include"]);
    private static readonly IReadOnlyList<string> BothIgnored = Array.AsReadOnly<string>(["tools", "include"]);

    public static UhpRequestContext GetContext(HttpContext http) => http.Features.Get<UhpRequestContext>() ?? throw new InvalidOperationException("The UHP protocol filter did not run.");

    public static async Task<Results<Ok<UhpResponse>, ServerSentEventsResult<byte[]>>> SubmitAsync(HttpContext http, IUhpServerBackend backend)
    {
        var context = GetContext(http);
        var options = http.RequestServices.GetRequiredService<UhpServerOptions>();

        if (!UhpProtocol.IsJsonContentType(http.Request.ContentType))
        {
            throw new UhpServerException(415, new UhpError(UhpProtocol.InvalidRequestError, UhpProtocol.InvalidInput, "Task requests require application/json."));
        }

        if (http.Request.ContentLength > options.MaximumRequestBodyBytes)
        {
            throw new UhpServerException(413, new UhpError(UhpProtocol.InvalidRequestError, UhpProtocol.InvalidInput, "The request body exceeds the configured size limit."));
        }

        if (context.IdempotencyKey is not null && !options.Capabilities.Idempotency)
        {
            throw new UhpServerException(400, new UhpError(UhpProtocol.InvalidRequestError, UhpProtocol.InvalidInput, "This server does not advertise task idempotency."));
        }

        UhpTaskRequest request;
        IReadOnlyList<string> ignored;
        bool stream;
        bool background;

        try
        {
            using var body = new LimitedReadStream(http.Request.Body, options.MaximumRequestBodyBytes);
            using var document = await JsonDocument
                .ParseAsync(body, new JsonDocumentOptions { MaxDepth = 64 }, http.RequestAborted)
                .ConfigureAwait(false);
            request = UhpJson.ParseRequest(document.RootElement, context.IdempotencyKey);
            ignored = IgnoredFields(document.RootElement);
            stream = document.RootElement.TryGetProperty("stream", out var streamField) && streamField.GetBoolean();
            background = document.RootElement.TryGetProperty("background", out var backgroundField) && backgroundField.GetBoolean();
        }
        catch (Exception exception) when (exception is UhpProtocolException or JsonException or ArgumentException or InvalidOperationException)
        {
            throw new UhpServerException(400, new UhpError(UhpProtocol.InvalidRequestError, UhpProtocol.InvalidInput, "The request does not match the UHP task contract."));
        }

        if ((request.InputFeatures & UhpInputFeatures.Files) != 0 && !options.Capabilities["files_input"] ||
            (request.InputFeatures & UhpInputFeatures.Interactive) != 0 && !options.Capabilities["input_requests"])
        {
            throw new UhpServerException(400, new(UhpProtocol.InvalidRequestError, UhpProtocol.InvalidInput, "The requested input capability is not available."));
        }

        var validator = http.RequestServices.GetRequiredService<UhpTaskBudgetsValidator>();
        var validation = validator.Validate(new UhpTaskBudgets(request.MaxStep ?? 0, request.MaxOutputTokens ?? 0, request.TimeoutSeconds ?? 0));

        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            throw new UhpServerException(400, new UhpError(UhpProtocol.InvalidRequestError, UhpProtocol.InvalidInput, "Task budgets cannot be negative.")
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
        var execution = await backend
            .SubmitAsync(new UhpSubmission(request, ignored) { Background = background }, context, stopping)
            .ConfigureAwait(false);

        if (execution.InitialResponse.Status != UhpResponseStatus.InProgress || string.IsNullOrWhiteSpace(execution.InitialResponse.Id))
        {
            throw new UhpServerException(500, new UhpError(UhpProtocol.ServerError, UhpProtocol.ServerError, "The backend returned an invalid accepted response."));
        }

        if (execution.IsReplay)
        {
            await execution.Completion
                .WaitAsync(http.RequestAborted)
                .ConfigureAwait(false);
        }

        if (background && !stream && !execution.IsReplay)
        {
            return TypedResults.Ok(execution.InitialResponse);
        }

        if (!stream)
        {
            var response = await execution.Completion
                .WaitAsync(http.RequestAborted)
                .ConfigureAwait(false);
            ValidateTerminal(execution.InitialResponse.Id, response);
            return TypedResults.Ok(response);
        }

        http.Response.Headers[UhpProtocol.BufferingHeader] = "no";
        return TypedResults.ServerSentEvents(ReadEventsAsync(execution, http.RequestAborted));
    }

    private static IReadOnlyList<string> IgnoredFields(JsonElement body)
    {
        List<string>? unknown = null;

        foreach (var field in body.EnumerateObject())
        {
            if (!IsKnown(field))
            {
                (unknown ??= []).Add(field.Name);
            }
        }

        var tools = body.TryGetProperty("tools", out _);
        var include = body.TryGetProperty("include", out _);

        if (unknown is not null)
        {
            if (tools)
            {
                unknown.Add("tools");
            }

            if (include)
            {
                unknown.Add("include");
            }

            return unknown.AsReadOnly();
        }

        return (tools, include) switch
        {
            (true, true) => BothIgnored,
            (true, false) => ToolsIgnored,
            (false, true) => IncludeIgnored,
            _ => Array.Empty<string>()
        };
    }

    private static bool IsKnown(JsonProperty field) =>
        field.NameEquals("input") || field.NameEquals("model") || field.NameEquals("metadata") ||
        field.NameEquals("stream") || field.NameEquals("background") || field.NameEquals("previous_response_id") ||
        field.NameEquals("instructions") || field.NameEquals("store") || field.NameEquals("max_output_tokens") ||
        field.NameEquals("max_step") || field.NameEquals("timeout_seconds") || field.NameEquals("tools") || field.NameEquals("include");

    private static async IAsyncEnumerable<byte[]> ReadEventsAsync(UhpExecution execution, [EnumeratorCancellation] CancellationToken ct)
    {
        long next = 0;

        await foreach (var update in execution
            .Subscribe(ct)
            .WithCancellation(ct)
            .ConfigureAwait(false))
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
            // ASP.NET Core writes byte[] SSE frames directly, avoiding polymorphic metadata lookup and a second serialization.
            yield return JsonSerializer.SerializeToUtf8Bytes(update, UhpJsonContext.Default.UhpEvent);

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
