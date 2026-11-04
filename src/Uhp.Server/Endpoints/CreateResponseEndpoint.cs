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

[ValiantEndpoint<UhpEndpointGroup>]
internal sealed class CreateResponseEndpoint : IEndpoint
{
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("/v1/responses", HandleAsync);
    }

    private static Task<Results<Ok<UhpResponse>, ServerSentEventsResult<System.Text.Json.JsonElement>>> HandleAsync(HttpContext http, [FromServices] IUhpServerBackend backend)
    {
        return UhpHttp.SubmitAsync(http, backend);
    }
}
