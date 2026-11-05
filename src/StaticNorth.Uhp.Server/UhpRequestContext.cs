using System.Security.Claims;

namespace StaticNorth.Uhp.Server;

public sealed record UhpRequestContext(ClaimsPrincipal Principal, string ProtocolVersion, string? IdempotencyKey)
{
    public string BasePath { get; init; } = "";
}
