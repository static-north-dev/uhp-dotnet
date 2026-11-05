using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace StaticNorth.Uhp.Server.Transport;

internal sealed record UhpBasePathMetadata(string Prefix)
{
    public static void Attach(EndpointBuilder endpoint)
    {
        if (endpoint is RouteEndpointBuilder { RoutePattern.RawText: { } pattern })
        {
            var protocolStart = pattern.LastIndexOf("/v1/", StringComparison.Ordinal);

            if (protocolStart > 0)
            {
                endpoint.Metadata.Add(new UhpBasePathMetadata(pattern[..protocolStart]));
            }
        }
    }
}
