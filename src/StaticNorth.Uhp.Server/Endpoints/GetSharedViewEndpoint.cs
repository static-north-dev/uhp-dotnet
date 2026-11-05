using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using StaticNorth.Valiant.Endpoints;
using StaticNorth.Valiant.Endpoints.AspNetCore;
using StaticNorth.Uhp.Server.Transport;

namespace StaticNorth.Uhp.Server.Endpoints;

[ValiantEndpoint<UhpEndpointGroup>]
internal sealed class GetSharedViewEndpoint : IEndpoint
{
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder builder) => builder.MapGet("/v1/shared/{share_id}", HandleAsync).WithMetadata(new UhpSharedViewMetadata());

    private static async Task<Ok<UhpSharedSessionView>> HandleAsync(HttpContext http, CancellationToken ct)
    {
        var backend = UhpApiBinding.Backend<IUhpSharedViewBackend>(http, "session_sharing");
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        var view = await backend.GetSharedViewAsync(UhpApiBinding.Route(http, "share_id"), ct).ConfigureAwait(false);
        return TypedResults.Ok(view);
    }
}
