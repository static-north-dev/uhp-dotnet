using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using StaticNorth.Valiant.Endpoints;
using StaticNorth.Valiant.Endpoints.AspNetCore;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Server.Transport;

namespace StaticNorth.Uhp.Server.Endpoints;

[ValiantEndpoint<UhpEndpointGroup>]
internal sealed class CancelSessionEndpoint : IEndpoint
{
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost(UhpProtocol.CancelSessionRoute, HandleAsync);
    }

    private static async Task<Ok<UhpSessionCancelResult>> HandleAsync([FromRoute(Name = UhpProtocol.SessionId)] string sessionId, [FromServices] IUhpServerBackend backend, HttpContext http, CancellationToken ct)
    {
        var result = await backend.CancelSessionAsync(sessionId, UhpHttp.GetContext(http), ct).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }
}
