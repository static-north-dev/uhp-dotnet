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
internal sealed class GetHarnessEndpoint : IEndpoint
{
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet(UhpProtocol.HarnessRoute, HandleAsync);
    }

    private static async Task<Ok<UhpHarness>> HandleAsync([FromRoute(Name = UhpProtocol.HarnessId)] string harnessId, [FromServices] IUhpServerBackend backend, HttpContext http, CancellationToken ct)
    {
        var harness = await backend.GetHarnessAsync(harnessId, UhpHttp.GetContext(http), ct).ConfigureAwait(false);
        return TypedResults.Ok(harness);
    }
}
