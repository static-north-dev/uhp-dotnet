using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StaticNorth.Uhp.FixtureServer;

public sealed class FixtureAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var credential = Request.Headers.Authorization.ToString();
        var user = credential switch
        {
            "Bearer fixture-alice" => "alice",
            "Bearer fixture-bob" => "bob",
            _ => null
        };

        if (user is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid fixture credential."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
