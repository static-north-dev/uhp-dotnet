namespace StaticNorth.Uhp.Core;

/// <summary>A UHP request failure with an error that can be returned to the caller.</summary>
public class UhpRequestException(int statusCode, UhpError error, string? retryAfter = null) : Exception(error.Message)
{
    public int StatusCode { get; } = statusCode;
    public UhpError Error { get; } = error;
    public string? RetryAfter { get; } = retryAfter;

    public static UhpRequestException InvalidRequest(int statusCode, string code, string message) =>
        new(statusCode, new(UhpProtocol.InvalidRequestError, code, message));
}
