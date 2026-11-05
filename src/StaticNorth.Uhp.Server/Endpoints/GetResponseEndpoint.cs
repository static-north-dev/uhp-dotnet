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
internal sealed class GetResponseEndpoint : IEndpoint
{
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet(UhpProtocol.ResponseRoute, HandleAsync);
    }

    private static async Task<Ok<UhpResponse>> HandleAsync([FromRoute(Name = UhpProtocol.ResponseId)] string responseId, [FromServices] IUhpServerBackend backend, HttpContext http, CancellationToken ct)
    {
        var response = await backend
            .GetResponseAsync(responseId, UhpHttp.GetContext(http), ct)
            .ConfigureAwait(false);
        return TypedResults.Ok(response);
    }
}
