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
internal sealed class DiscoveryEndpoint : IEndpoint
{
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("/v1/uhp", HandleAsync).WithMetadata(new UhpDiscoveryMetadata());
    }

    private static Ok<UhpDiscovery> HandleAsync([FromServices] UhpServerOptions options)
    {
        var discovery = new UhpDiscovery(options.Versions, options.DefaultVersion, "core", options.Capabilities)
        {
            ImplementationName = options.ImplementationName,
            ImplementationVersion = options.ImplementationVersion
        };

        return TypedResults.Ok(discovery);
    }
}
