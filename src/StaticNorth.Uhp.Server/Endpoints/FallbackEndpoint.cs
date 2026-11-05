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
internal sealed class FallbackEndpoint : IEndpoint
{
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder
            .MapMethods("/v1/{**path}", ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD"], HandleAsync)
            .WithOrder(int.MaxValue);
    }

    private static JsonHttpResult<UhpErrorEnvelope> HandleAsync()
    {
        return TypedResults.Json(new UhpErrorEnvelope(new UhpError(UhpProtocol.InvalidRequestError, UhpProtocol.InvalidInput, "This UHP operation is not supported by the preview.")), UhpJsonContext.Default.UhpErrorEnvelope, statusCode: 404);
    }
}
