using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using StaticNorth.Valiant.Endpoints;
using StaticNorth.Valiant.Endpoints.AspNetCore;
using Uhp.Core;
using Uhp.Server.Transport;

namespace Uhp.Server.Endpoints;

[ValiantEndpoint]
internal sealed class CancelSessionEndpoint : IEndpoint
{
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("/v1/sessions/{session_id}/cancel", HandleAsync);
    }

    private static async Task<Ok<UhpSessionCancelResult>> HandleAsync([FromRoute(Name = "session_id")] string sessionId, [FromServices] IUhpServerBackend backend, HttpContext http, CancellationToken ct)
    {
        var result = await backend.CancelSessionAsync(sessionId, UhpHttp.GetContext(http), ct).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }
}
