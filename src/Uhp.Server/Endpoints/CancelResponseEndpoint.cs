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
internal sealed class CancelResponseEndpoint : IEndpoint
{
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("/v1/responses/{response_id}/cancel", HandleAsync);
    }

    private static async Task<Ok<UhpResponse>> HandleAsync([FromRoute(Name = "response_id")] string responseId, [FromServices] IUhpServerBackend backend, HttpContext http, CancellationToken ct)
    {
        var response = await backend.CancelResponseAsync(responseId, UhpHttp.GetContext(http), ct).ConfigureAwait(false);
        return TypedResults.Ok(response);
    }
}
