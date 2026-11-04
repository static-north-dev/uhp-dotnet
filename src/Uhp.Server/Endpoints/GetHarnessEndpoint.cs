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
internal sealed class GetHarnessEndpoint : IEndpoint
{
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("/v1/harnesses/{harness_id}", HandleAsync);
    }

    private static async Task<Ok<UhpHarness>> HandleAsync([FromRoute(Name = "harness_id")] string harnessId, [FromServices] IUhpServerBackend backend, HttpContext http, CancellationToken ct)
    {
        var harness = await backend.GetHarnessAsync(harnessId, UhpHttp.GetContext(http), ct).ConfigureAwait(false);
        return TypedResults.Ok(harness);
    }
}
