using System.Net;
using Uhp.Core;

namespace Uhp.Client;

public sealed class UhpApiException : HttpRequestException
{
    internal UhpApiException(HttpStatusCode status, UhpError? error, string? retryAfter, string? expectedVersion, string? actualVersion)
        : base(error?.Message ?? $"UHP HTTP request failed with status {(int)status}.", null, status)
    {
        Error = error;
        RetryAfter = retryAfter;
        ExpectedProtocolVersion = expectedVersion;
        ResponseProtocolVersion = actualVersion;
    }

    public UhpError? Error { get; }
    public string? ErrorType => Error?.Type;
    public string? Code => Error?.Code;
    public string? Param => Error?.Param;
    public System.Text.Json.JsonElement? Detail => Error?.Detail;
    public string? RetryAfter { get; }
    public string? ExpectedProtocolVersion { get; }
    public string? ResponseProtocolVersion { get; }
    public bool HasVersionMismatch => Error is not null && ExpectedProtocolVersion is not null && ExpectedProtocolVersion != ResponseProtocolVersion;
}
