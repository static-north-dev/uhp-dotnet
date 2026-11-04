using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StaticNorth.Valiant.Endpoints;
using StaticNorth.Valiant.Endpoints.AspNetCore;
using Uhp.Server.Transport;

namespace Uhp.Server.Endpoints;

[ValiantEndpointGroup]
internal sealed class UhpEndpointGroup : IEndpointGroup
{
    public static RouteGroupBuilder CreateGroup(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("").AllowAnonymous();
        group.AddEndpointFilter(new UhpProtocolFilter());

        return group;
    }
}
