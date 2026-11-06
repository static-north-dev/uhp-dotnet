using System.Text.Json;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Core.Providers;

namespace StaticNorth.Uhp.Reference;

public partial class ReferenceBackend
{
    private readonly Dictionary<string, (string Response, UhpOutputItem Question)> _pendingInputs = new(StringComparer.Ordinal);

    private UhpHarnessInputAnswer? ValidateAnswer(UhpTaskRequest request, ReferenceExecution? previous)
    {
        if ((request.InputFeatures & UhpInputFeatures.Interactive) == 0)
        {
            return null;
        }

        var answer = request.Input
            .EnumerateArray()
            .First(item => item.TryGetProperty("type", out var type) && type.ValueEquals("input_response"));
        var id = answer
            .GetProperty("input_request_id")
            .GetString()!;
        if (previous is null || !_pendingInputs.TryGetValue(previous.SessionId, out var pending) || pending.Response != previous.Response.Id || pending.Question.Id != id)
        {
            throw Failure(409, "input_not_pending", "The response does not hold this pending input request.");
        }

        var question = pending.Question.Raw;

        if (question.TryGetProperty("expires_at", out var expiration) && expiration.TryGetInt64(out var expires) && expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            throw Failure(410, "input_request_expired", "The pending input request has expired.");
        }

        var kind = question
            .GetProperty("kind")
            .GetString()!;
        var value = answer
            .GetProperty("answer")
            .GetString()!;

        if ((kind == "choice" || kind == "confirm") &&
            (question.TryGetProperty("options", out var options)
                ? !options
                    .EnumerateArray()
                    .Any(option => option
                        .GetProperty("id")
                        .GetString() == value)
                : kind == "choice" || value is not ("yes" or "no")))
        {
            throw Failure(422, "input_response_invalid", "The answer is outside the request's options.");
        }

        return new(id, kind, value, question
            .GetProperty("prompt")
            .GetString()!);
    }

    private static UhpTaskRequest RetainedRequest(UhpTaskRequest request, UhpHarnessInputAnswer? answer)
    {
        if (answer?.Kind != "secret")
        {
            return request;
        }

        return request.WithInput(UhpJson.CreateElement(writer =>
        {
            writer.WriteStartArray();

            foreach (var item in request.Input.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var type) && type.ValueEquals("input_response"))
                {
                    writer.WriteStartObject(); writer.WriteString("type", "input_response"); writer.WriteString("input_request_id", answer.RequestId); writer.WriteString("answer", "[redacted]"); writer.WriteEndObject();
                }
                else
                {
                    item.WriteTo(writer);
                }
            }

            writer.WriteEndArray();
        }));
    }
}
