using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Server.Transport;

internal sealed class UhpProtocolFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var http = invocation.HttpContext;
        var options = http.RequestServices.GetRequiredService<UhpServerOptions>();
        http.Response.Headers[UhpProtocol.VersionHeader] = options.DefaultVersion;

        try
        {
            var versions = http.Request.Headers[UhpProtocol.VersionHeader];

            if (versions.Count > 1 || versions.Count == 1 && !options.Versions.Contains(versions[0]!, StringComparer.Ordinal))
            {
                throw new UhpServerException(400, new UhpError(UhpProtocol.InvalidRequestError, "unsupported_protocol_version", "The requested protocol version is not supported.")
                {
                    Detail = UhpJson.CreateElement(writer =>
                    {
                        writer.WriteStartObject();
                        writer.WriteStartArray("supported");

                        foreach (var supported in options.Versions)
                        {
                            writer.WriteStringValue(supported);
                        }

                        writer.WriteEndArray();
                        writer.WriteEndObject();
                    })
                });
            }

            var version = versions.Count == 1 ? versions[0]! : options.DefaultVersion;
            http.Response.Headers[UhpProtocol.VersionHeader] = version;

            if (http.GetEndpoint()?.Metadata.GetMetadata<UhpDiscoveryMetadata>() is null && http.GetEndpoint()?.Metadata.GetMetadata<UhpSharedViewMetadata>() is null)
            {
                var authentication = await http
                    .AuthenticateAsync(options.AuthenticationScheme)
                    .ConfigureAwait(false);

                if (!authentication.Succeeded || authentication.Principal?.Identity?.IsAuthenticated != true)
                {
                    throw new UhpServerException(401, new UhpError("authentication_error", "invalid_credential", "A valid bearer credential is required."));
                }

                http.User = authentication.Principal;

                if (options.AuthorizationPolicy is { } policy)
                {
                    var authorization = http.RequestServices.GetRequiredService<IAuthorizationService>();

                    if (!(await authorization
                        .AuthorizeAsync(http.User, http, policy)
                        .ConfigureAwait(false)).Succeeded)
                    {
                        throw new UhpServerException(403, new UhpError("permission_error", "insufficient_scope", "The credential is not permitted to perform this operation."));
                    }
                }
            }

            var keys = http.Request.Headers[UhpProtocol.IdempotencyHeader];

            if (keys.Count > 1 || keys.Count == 1 && !UhpProtocol.IsValidIdempotencyKey(keys[0]))
            {
                throw new UhpServerException(400, new UhpError(UhpProtocol.InvalidRequestError, UhpProtocol.InvalidInput, "Invalid Idempotency-Key header."));
            }

            var principal = new ClaimsPrincipal();

            if (http.User.Identities is IReadOnlyList<ClaimsIdentity> identities)
            {
                for (var index = 0; index < identities.Count; index++)
                {
                    principal.AddIdentity(new ClaimsIdentity(identities[index]));
                }
            }
            else
            {
                foreach (var identity in http.User.Identities)
                {
                    principal.AddIdentity(new ClaimsIdentity(identity));
                }
            }

            var prefix = http.GetEndpoint()?.Metadata.GetMetadata<UhpBasePathMetadata>()?.Prefix;
            http.Features.Set(new UhpRequestContext(principal, version, keys.Count == 1 ? keys[0] : null)
            {
                BasePath = string.Concat(http.Request.PathBase.Value, prefix)
            });
            return await next(invocation)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            return TypedResults.Empty;
        }
        catch (UhpServerException exception)
        {
            return Error(http, exception.StatusCode, exception.Error, exception.RetryAfter);
        }
        catch (Exception)
        {
            return Error(http, 500, new UhpError(UhpProtocol.ServerError, UhpProtocol.ServerError, "The server could not complete the request."));
        }
    }

    private static Microsoft.AspNetCore.Http.HttpResults.JsonHttpResult<UhpErrorEnvelope> Error(HttpContext http, int status, UhpError error, string? retryAfter = null)
    {
        if (status == 401)
        {
            http.Response.Headers.WWWAuthenticate = UhpProtocol.BearerScheme;
        }

        if (retryAfter is not null)
        {
            http.Response.Headers.RetryAfter = retryAfter;
        }

        return TypedResults.Json(new UhpErrorEnvelope(error), UhpJsonContext.Default.UhpErrorEnvelope, statusCode: status);
    }
}
