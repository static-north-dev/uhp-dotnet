namespace StaticNorth.Uhp.Core;

public static class UhpProtocol
{
    public const string Name = "uhp";
    public const string DiscoveryObject = "uhp.discovery";
    public const string ResponseObject = "response";
    public const string HarnessObject = "harness";
    public const string VersionHeader = "UHP-Version";
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string RetryAfterHeader = "Retry-After";
    public const string BufferingHeader = "X-Accel-Buffering";
    public const string JsonMediaType = "application/json";
    public const string EventStreamMediaType = "text/event-stream";
    public const string BearerScheme = "Bearer";
    public const string DiscoveryPath = "v1/uhp";
    public const string HarnessesPath = "v1/harnesses";
    public const string ModelsPath = "v1/models";
    public const string ResponsesPath = "v1/responses";
    public const string SessionsPath = "v1/sessions";
    public const string HarnessRoute = "/" + HarnessesPath + "/{harness_id}";
    public const string HarnessModelsRoute = HarnessRoute + "/models";
    public const string ResponseRoute = "/" + ResponsesPath + "/{response_id}";
    public const string CancelResponseRoute = ResponseRoute + "/cancel";
    public const string CancelSessionRoute = "/" + SessionsPath + "/{session_id}/cancel";
    public const string HarnessId = "harness_id";
    public const string ResponseId = "response_id";
    public const string SessionId = "session_id";
    public const string InvalidRequestError = "invalid_request_error";
    public const string InvalidInput = "invalid_input";
    public const string ServerError = "server_error";
    public const string StreamingCapability = "streaming";
    public const string SessionsCapability = "sessions";
    public const string CancellationCapability = "cancellation";
    public const string IdempotencyCapability = "idempotency";

    public static bool IsValidIdempotencyKey(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (char t in value)
        {
            if (t is < (char)0x21 or > (char)0x7e)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsJsonContentType(string? value)
    {
        var mediaType = value.AsSpan();
        int separator = mediaType.IndexOf(';');

        if (separator >= 0)
        {
            mediaType = mediaType[..separator];
        }

        return mediaType.Trim().Equals(JsonMediaType, StringComparison.OrdinalIgnoreCase);
    }
}
