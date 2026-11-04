using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Uhp.Core;

namespace Uhp.Server.Transport;

internal sealed class UhpProtocolFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var http = invocation.HttpContext;
        var options = http.RequestServices.GetRequiredService<UhpServerOptions>();
        http.Response.Headers["UHP-Version"] = options.DefaultVersion;

        try
        {
            var versions = http.Request.Headers["UHP-Version"];

            if (versions.Count > 1 || versions.Count == 1 && !options.Versions.Contains(versions[0]!, StringComparer.Ordinal))
            {
                throw new UhpServerException(400, new UhpError("invalid_request_error", "unsupported_protocol_version", "The requested protocol version is not supported.")
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
            http.Response.Headers["UHP-Version"] = version;

            if (http.GetEndpoint()?.Metadata.GetMetadata<UhpDiscoveryMetadata>() is null)
            {
                var authentication = await http.AuthenticateAsync(options.AuthenticationScheme).ConfigureAwait(false);

                if (!authentication.Succeeded || authentication.Principal?.Identity?.IsAuthenticated != true)
                {
                    throw new UhpServerException(401, new UhpError("authentication_error", "invalid_credential", "A valid bearer credential is required."));
                }

                http.User = authentication.Principal;

                if (options.AuthorizationPolicy is { } policy)
                {
                    var authorization = http.RequestServices.GetRequiredService<IAuthorizationService>();

                    if (!(await authorization.AuthorizeAsync(http.User, http, policy).ConfigureAwait(false)).Succeeded)
                    {
                        throw new UhpServerException(403, new UhpError("permission_error", "insufficient_scope", "The credential is not permitted to perform this operation."));
                    }
                }
            }

            var keys = http.Request.Headers["Idempotency-Key"];

            if (keys.Count > 1 || keys.Count == 1 && (string.IsNullOrWhiteSpace(keys[0]) || keys[0]!.Any(character => character < 0x21 || character > 0x7e)))
            {
                throw new UhpServerException(400, new UhpError("invalid_request_error", "invalid_input", "Invalid Idempotency-Key header."));
            }

            var principal = new ClaimsPrincipal(http.User.Identities.Select(identity => new ClaimsIdentity(identity)));
            http.Features.Set(new UhpRequestContext(principal, version, keys.Count == 1 ? keys[0] : null));
            return await next(invocation).ConfigureAwait(false);
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
            return Error(http, 500, new UhpError("server_error", "server_error", "The server could not complete the request."));
        }
    }

    private static Microsoft.AspNetCore.Http.HttpResults.JsonHttpResult<UhpErrorEnvelope> Error(HttpContext http, int status, UhpError error, string? retryAfter = null)
    {
        if (status == 401)
        {
            http.Response.Headers.WWWAuthenticate = "Bearer";
        }

        if (retryAfter is not null)
        {
            http.Response.Headers.RetryAfter = retryAfter;
        }

        return TypedResults.Json(new UhpErrorEnvelope(error), UhpJsonContext.Default.UhpErrorEnvelope, statusCode: status);
    }
}
