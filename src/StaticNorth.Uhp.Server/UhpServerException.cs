using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Server;

public sealed class UhpServerException(int statusCode, UhpError error, string? retryAfter = null) : Exception(error.Message)
{
    public int StatusCode { get; } = statusCode;
    public UhpError Error { get; } = error;
    public string? RetryAfter { get; } = retryAfter;

    public static UhpServerException NotFound(string code) => new(404, new UhpError(UhpProtocol.InvalidRequestError, code, "The requested object was not found."));
}
