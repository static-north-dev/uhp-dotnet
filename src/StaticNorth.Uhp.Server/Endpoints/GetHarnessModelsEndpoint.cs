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
internal sealed class GetHarnessModelsEndpoint : IEndpoint
{
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet(UhpProtocol.HarnessModelsRoute, HandleAsync);
    }

    private static async Task<Ok<UhpHarnessModels>> HandleAsync([FromRoute(Name = UhpProtocol.HarnessId)] string harnessId, [FromServices] IUhpServerBackend backend, HttpContext http, CancellationToken ct)
    {
        var models = await backend.GetHarnessModelsAsync(harnessId, UhpHttp.GetContext(http), ct).ConfigureAwait(false);
        return TypedResults.Ok(models);
    }
}
