using Microsoft.AspNetCore.Authentication;
using StaticNorth.Uhp.ConformanceHost;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Reference;
using StaticNorth.Uhp.Reference.Memory;
using StaticNorth.Uhp.Reference.Environments;
using StaticNorth.Uhp.Server;
using StaticNorth.Uhp.TestSupport.Memory;

ArgumentException.ThrowIfNullOrWhiteSpace(Environment.GetEnvironmentVariable("UHP_HOST_TOKEN"));
var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddSingleton(new HostCredential(Environment.GetEnvironmentVariable("UHP_HOST_TOKEN")!));
var root = Path.GetFullPath(builder.Configuration["state-root"] ?? "artifacts/conformance/host");
var authenticationHome = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
await using var environments = new ReferenceEnvironmentBackend(Path.Combine(root, "environments"));
await using var runtime = new CodexRuntime(root, authenticationHome);
await runtime.InitializeAsync(CancellationToken.None);
builder.Services.AddSingleton<IReferenceRuntime>(runtime);
builder.Services.AddSingleton(environments);
builder.Services.AddSingleton<IUhpEnvironmentBackend>(environments);
builder.Services
    .AddAuthentication("Host")
    .AddScheme<AuthenticationSchemeOptions, HostAuthenticationHandler>("Host", null);
builder.Services.AddUhpServer<ReferenceBackend>(options =>
{
    options.AuthenticationScheme = "Host";
    options.Versions = [UhpProtocolVersions.Current];
    options.DefaultVersion = UhpProtocolVersions.Current;
    options.PluginSchemas = ["https://agent-plugins.org/schemas/1.0.0/plugin.schema.json"];
    options.Capabilities = new(new Dictionary<string, bool>
    {
        ["streaming"] = true,
        ["sessions"] = true,
        ["cancellation"] = true,
        ["idempotency"] = true,
        ["files_input"] = true,
        ["files_output"] = true,
        ["session_listing"] = true,
        ["harness_management"] = true,
        ["harness_idempotency"] = true,
        ["plugins"] = true,
        ["session_sharing"] = true,
        ["memories"] = true,
        ["input_requests"] = true,
        ["environments"] = true
    });
});
builder.Services.AddSingleton<IUhpSessionBackend>(GetBackend);
builder.Services.AddSingleton<IUhpFileBackend>(GetBackend);
builder.Services.AddSingleton<IUhpHarnessManagementBackend>(GetBackend);
builder.Services.AddSingleton<IUhpPluginBackend>(GetBackend);
builder.Services.AddSingleton<IUhpResponseStorageBackend>(GetBackend);
builder.Services.AddSingleton<IUhpSharingBackend>(GetBackend);
builder.Services.AddSingleton<IUhpSharedViewBackend>(GetBackend);
builder.Services.AddSingleton<IReferenceMemoryProvider, InMemoryMemoryProvider>();
builder.Services.AddSingleton<IUhpMemoryBackend, ReferenceMemoryBackend>();
var app = builder.Build();
var backend = GetBackend(app.Services);
environments.ConnectHarnesses(backend);
backend.ConnectMemories((ReferenceMemoryBackend)app.Services.GetRequiredService<IUhpMemoryBackend>());

if (Uri.TryCreate(builder.Configuration["public-origin"] ?? builder.Configuration["urls"], UriKind.Absolute, out var origin) &&
    (origin.Scheme == Uri.UriSchemeHttp || origin.Scheme == Uri.UriSchemeHttps))
{
    backend.ArtifactOrigin = origin;
}

app.MapUhp();
await app.RunAsync();

static ReferenceBackend GetBackend(IServiceProvider services) => (ReferenceBackend)services.GetRequiredService<IUhpServerBackend>();
