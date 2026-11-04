using System.Security.Claims;

namespace Uhp.Server;

public sealed record UhpRequestContext(ClaimsPrincipal Principal, string ProtocolVersion, string? IdempotencyKey);
