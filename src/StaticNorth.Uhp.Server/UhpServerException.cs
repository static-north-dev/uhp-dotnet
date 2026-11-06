using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Server;

public sealed class UhpServerException(int statusCode, UhpError error, string? retryAfter = null) : UhpRequestException(statusCode, error, retryAfter)
{
    public static UhpServerException NotFound(string code) => new(404, new UhpError(UhpProtocol.InvalidRequestError, code, "The requested object was not found."));
}
