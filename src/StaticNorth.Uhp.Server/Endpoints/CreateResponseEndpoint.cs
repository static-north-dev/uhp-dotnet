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
internal sealed class CreateResponseEndpoint : IEndpoint
{
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("/" + UhpProtocol.ResponsesPath, HandleAsync);
    }

    private static Task<Results<Ok<UhpResponse>, ServerSentEventsResult<byte[]>>> HandleAsync(HttpContext http, [FromServices] IUhpServerBackend backend)
    {
        return UhpHttp.SubmitAsync(http, backend);
    }
}
