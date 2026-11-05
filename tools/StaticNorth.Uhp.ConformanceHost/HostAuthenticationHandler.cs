using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace StaticNorth.Uhp.ConformanceHost;

internal sealed class HostAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, HostCredential credential)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();

        if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || !credential.Matches(header.AsSpan(7)))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid credential."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "conformance")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
