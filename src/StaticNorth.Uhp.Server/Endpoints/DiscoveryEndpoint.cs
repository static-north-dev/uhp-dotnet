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
internal sealed class DiscoveryEndpoint : IEndpoint
{
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder
            .MapGet("/" + UhpProtocol.DiscoveryPath, HandleAsync)
            .WithMetadata(new UhpDiscoveryMetadata());
    }

    private static Ok<UhpDiscovery> HandleAsync([FromServices] UhpServerOptions options)
    {
        var extended = options.Capabilities["session_listing"] && options.Capabilities["files_input"] && options.Capabilities["files_output"];
        var profile = extended ? options.Capabilities["harness_management"] ? "full" : "extended" : "core";
        var discovery = new UhpDiscovery(options.Versions, options.DefaultVersion, profile, options.Capabilities)
        {
            ImplementationName = options.ImplementationName,
            ImplementationVersion = options.ImplementationVersion,
            PluginSchemas = options.PluginSchemas
        };

        return TypedResults.Ok(discovery);
    }
}
