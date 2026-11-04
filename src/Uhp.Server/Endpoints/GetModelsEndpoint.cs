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
internal sealed class GetModelsEndpoint : IEndpoint
{
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("/v1/models", HandleAsync);
    }

    private static async Task<Ok<UhpModelCatalog>> HandleAsync([FromServices] IUhpServerBackend backend, HttpContext http, CancellationToken ct)
    {
        var models = await backend.GetModelsAsync(UhpHttp.GetContext(http), ct).ConfigureAwait(false);
        return TypedResults.Ok(models);
    }
}
