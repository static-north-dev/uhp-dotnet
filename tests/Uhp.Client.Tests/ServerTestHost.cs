using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StaticNorth.Valiant.Endpoints.AspNetCore;
using Uhp.Client;
using Uhp.Core;
using Uhp.FixtureServer;
using Uhp.Server;

namespace Uhp.Client.Tests;

internal sealed class ServerTestHost(WebApplication app) : IAsyncDisposable
{
    public FixtureBackend Backend => (FixtureBackend)app.Services.GetRequiredService<IUhpServerBackend>();

    public static async Task<ServerTestHost> StartAsync(string version = UhpProtocolVersions.Current, Action<ValiantEndpointOptions>? configureEndpoints = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthenticationHandler>("Fixture", null);
        builder.Services.AddUhpServer<FixtureBackend>(options =>
        {
            options.AuthenticationScheme = "Fixture";
            options.Versions = [version];
            options.DefaultVersion = version;
            options.Capabilities = new UhpCapabilities(new Dictionary<string, bool>
            {
                ["streaming"] = true,
                ["sessions"] = true,
                ["cancellation"] = true,
                ["idempotency"] = true
            });
        });

        if (configureEndpoints is not null)
        {
            builder.Services.Configure(configureEndpoints);
        }

        var app = builder.Build();
        app.MapUhp("/api/harness");
        app.MapGet("/health", HandleHealth);
        await app.StartAsync();
        return new ServerTestHost(app);
    }

    public HttpClient CreateHttp() => new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        BaseAddress = new Uri(app.Urls.Single() + "/api/harness/"),
        Timeout = Timeout.InfiniteTimeSpan
    };

    public static Task<UhpClient> ConnectAsync(HttpClient http, string user = "alice") => UhpClient.ConnectAsync(http, new()
    {
        GetBearerTokenAsync = _ => ValueTask.FromResult<string>("fixture-" + user)
    });

    public ValueTask DisposeAsync() => app.DisposeAsync();

    private static Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult HandleHealth() => TypedResults.Text("healthy");
}
