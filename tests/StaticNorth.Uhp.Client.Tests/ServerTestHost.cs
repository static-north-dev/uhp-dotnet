using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StaticNorth.Valiant.Endpoints.AspNetCore;
using StaticNorth.Uhp.Client;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.FixtureServer;
using StaticNorth.Uhp.Reference;
using StaticNorth.Uhp.Server;

namespace StaticNorth.Uhp.Client.Tests;

internal sealed class ServerTestHost(WebApplication app) : IAsyncDisposable
{
    public FixtureBackend Backend => (FixtureBackend)app.Services.GetRequiredService<IUhpServerBackend>();

    public static async Task<ServerTestHost> StartAsync(string version = UhpProtocolVersions.Current, Action<ValiantEndpointOptions>? configureEndpoints = null, bool sessions = false, bool files = false, bool harnesses = false, long maximumUploadBytes = 67_108_864, bool sharing = false, bool memories = false, ReferenceBackend? backend = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost
            .UseKestrel()
            .UseUrls("http://127.0.0.1:0");
        builder.Services
            .AddAuthentication("Fixture")
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthenticationHandler>("Fixture", null);

        if (backend is not null)
        {
            builder.Services.AddSingleton<IUhpServerBackend>(backend);
        }

        builder.Services.AddUhpServer<FixtureBackend>(options =>
        {
            options.AuthenticationScheme = "Fixture";
            options.Versions = [version];
            options.DefaultVersion = version;
            options.MaximumUploadBytes = maximumUploadBytes;
            options.PluginSchemas = harnesses ? ["https://agent-plugins.org/schemas/1.0.0/plugin.schema.json"] : [];
            options.Capabilities = new UhpCapabilities(new Dictionary<string, bool>
            {
                ["streaming"] = true,
                ["sessions"] = true,
                ["cancellation"] = true,
                ["idempotency"] = true,
                ["session_listing"] = sessions,
                ["files_input"] = files,
                ["files_output"] = files,
                ["harness_management"] = harnesses,
                ["harness_idempotency"] = harnesses,
                ["plugins"] = harnesses,
                ["session_sharing"] = sharing,
                ["memories"] = memories
            });
        });
        builder.Services.AddSingleton<IUhpResponseStorageBackend>(GetReferenceBackend);

        if (sessions)
        {
            builder.Services.AddSingleton<IUhpSessionBackend>(GetReferenceBackend);
        }

        if (files)
        {
            builder.Services.AddSingleton<IUhpFileBackend>(GetReferenceBackend);
        }

        if (sharing)
        {
            builder.Services.AddSingleton<IUhpSharingBackend>(GetReferenceBackend);
            builder.Services.AddSingleton<IUhpSharedViewBackend>(GetReferenceBackend);
        }

        if (memories)
        {
            builder.Services.AddSingleton<StaticNorth.Uhp.TestSupport.Memory.IReferenceMemoryProvider, StaticNorth.Uhp.TestSupport.Memory.InMemoryMemoryProvider>();
            builder.Services.AddSingleton<IUhpMemoryBackend, StaticNorth.Uhp.Reference.Memory.ReferenceMemoryBackend>();
        }

        if (harnesses)
        {
            builder.Services.AddSingleton<IUhpHarnessManagementBackend>(GetReferenceBackend);
            builder.Services.AddSingleton<IUhpPluginBackend>(GetReferenceBackend);
        }

        if (configureEndpoints is not null)
        {
            builder.Services.Configure(configureEndpoints);
        }

        var app = builder.Build();

        if (memories)
        {
            GetReferenceBackend(app.Services).ConnectMemories((StaticNorth.Uhp.Reference.Memory.ReferenceMemoryBackend)app.Services.GetRequiredService<IUhpMemoryBackend>());
        }

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

    private static ReferenceBackend GetReferenceBackend(IServiceProvider services) => (ReferenceBackend)services.GetRequiredService<IUhpServerBackend>();

    private static Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult HandleHealth() => TypedResults.Text("healthy");
}
