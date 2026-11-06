using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using StaticNorth.Uhp.ConformanceHost;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Harnesses.ClaudeCode;
using StaticNorth.Uhp.Harnesses.Codex;
using StaticNorth.Uhp.Harnesses.OpenCode;
using StaticNorth.Uhp.Reference;
using StaticNorth.Uhp.Reference.Memory;
using StaticNorth.Uhp.Reference.Environments;
using StaticNorth.Uhp.Server;
using StaticNorth.Uhp.Core.Providers;
using StaticNorth.Uhp.TestSupport.Memory;

ArgumentException.ThrowIfNullOrWhiteSpace(Environment.GetEnvironmentVariable("UHP_HOST_TOKEN"));
var builder = WebApplication.CreateSlimBuilder(args);
var claudeAuth = builder.Configuration["claude-auth"] ?? "api-key";

if (claudeAuth is not ("api-key" or "subscription"))
{
    throw new InvalidOperationException("--claude-auth must be api-key or subscription.");
}

if (claudeAuth == "subscription")
{
    ConfigureSubscriptionListener(builder);
}

builder.Services.AddSingleton(new HostCredential(Environment.GetEnvironmentVariable("UHP_HOST_TOKEN")!));
var root = Path.GetFullPath(builder.Configuration["state-root"] ?? "artifacts/conformance/host");
var authenticationHome = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
await using var environments = new ReferenceEnvironmentBackend(Path.Combine(root, "environments"));
builder.Services.AddUhpCodex(root, authenticationHome);
var hasClaude = await ConfigureClaudeAsync(builder, root, claudeAuth);
using var openCodeHttp = ConfigureOpenCodeHttp(builder.Configuration);

if (openCodeHttp is not null)
{
    ConfigureOpenCode(builder, openCodeHttp, root);
}

builder.Services.AddSingleton(environments);
builder.Services.AddSingleton<IUhpEnvironmentBackend>(environments);
builder.Services
    .AddAuthentication("Host")
    .AddScheme<AuthenticationSchemeOptions, HostAuthenticationHandler>("Host", null);
builder.Services.AddSingleton<IUhpServerBackend>(CreateBackend);
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
        ["files_input"] = !hasClaude && openCodeHttp is null,
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
await using var app = builder.Build();
await app.Services
    .GetRequiredService<UhpHarnessProviderRegistry>()
    .InitializeAsync(app.Lifetime.ApplicationStopping);
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

static ReferenceBackend CreateBackend(IServiceProvider services) => new(
    services.GetRequiredService<UhpHarnessProviderRegistry>(), "codex", services.GetRequiredService<ReferenceEnvironmentBackend>());

static HttpClient? ConfigureOpenCodeHttp(IConfiguration configuration)
{
    if (configuration["opencode-url"] is not { } url)
    {
        return null;
    }

    if (!Uri.TryCreate(url, UriKind.Absolute, out var origin) || origin.Scheme != Uri.UriSchemeHttp ||
        !IPAddress.TryParse(origin.DnsSafeHost, out var address) || !IPAddress.IsLoopback(address) ||
        origin.PathAndQuery != "/" || origin.UserInfo.Length != 0 || origin.Fragment.Length != 0)
    {
        throw new InvalidOperationException("--opencode-url requires an HTTP loopback origin, such as http://127.0.0.1:4096.");
    }

    var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
    {
        BaseAddress = origin,
        Timeout = Timeout.InfiniteTimeSpan
    };

    if (Environment.GetEnvironmentVariable("OPENCODE_SERVER_PASSWORD") is { Length: > 0 } password)
    {
        var username = Environment.GetEnvironmentVariable("OPENCODE_SERVER_USERNAME") ?? "opencode";
        var credential = Encoding.UTF8.GetBytes(username + ":" + password);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credential));
    }

    return http;
}

static void ConfigureOpenCode(WebApplicationBuilder builder, HttpClient http, string stateRoot)
{
    var selected = builder.Configuration["opencode-model"];
    var separator = selected?.IndexOf('/') ?? -1;

    if (separator <= 0 || separator == selected!.Length - 1)
    {
        throw new InvalidOperationException("--opencode-model must name a configured provider/model.");
    }

    var provider = selected[..separator];
    var model = selected[(separator + 1)..];
    builder.Services.AddUhpOpenCode(http, Path.Combine(stateRoot, "opencode"), provider, model);
}

static void ConfigureSubscriptionListener(WebApplicationBuilder builder)
{
    var url = builder.Configuration["urls"] ?? "http://127.0.0.1:5100";

    if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttp ||
        !IPAddress.TryParse(endpoint.DnsSafeHost, out var address) || !IPAddress.IsLoopback(address) ||
        endpoint.PathAndQuery != "/" || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0)
    {
        throw new InvalidOperationException("Claude subscription mode requires one HTTP loopback URL, such as http://127.0.0.1:5100.");
    }

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Configure(new ConfigurationBuilder().Build(), reloadOnChange: false);
        options.Listen(address, endpoint.Port);
    });
}

static async Task<bool> ConfigureClaudeAsync(WebApplicationBuilder builder, string stateRoot, string authentication)
{
    var configuration = builder.Configuration;

    if (configuration["claude-executable"] is not { } executable)
    {
        if (authentication == "subscription")
        {
            throw new InvalidOperationException("Claude subscription mode requires --claude-executable.");
        }

        return false;
    }

    var path = Path.GetFullPath(executable);
    var model = configuration["claude-model"];
    if (!File.Exists(path) || string.IsNullOrWhiteSpace(model))
    {
        throw new InvalidOperationException("Claude Code requires an installed executable and --claude-model.");
    }

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var claudeRoot = Path.Combine(stateRoot, "claude");

    if (authentication == "subscription")
    {
        var config = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        config = string.IsNullOrWhiteSpace(config) ? null : Path.GetFullPath(config);
        var runner = new ClaudeCodeRunner(path, config);
        var profile = await runner.GetSubscriptionProfileDirectoryAsync(Directory.CreateDirectory(claudeRoot).FullName, timeout.Token);

        if (profile is null)
        {
            throw new InvalidOperationException("Claude subscription mode requires a saved Claude Code login. Run claude auth login --claudeai first.");
        }

        builder.Services.AddUhpClaudeCode(path, claudeRoot, null, model, profile, config);
        return true;
    }

    var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    if (string.IsNullOrWhiteSpace(key))
    {
        throw new InvalidOperationException("Claude API key mode requires ANTHROPIC_API_KEY.");
    }

    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models/" + Uri.EscapeDataString(model));
    request.Headers.Add("x-api-key", key);
    request.Headers.Add("anthropic-version", "2023-06-01");
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
    response.EnsureSuccessStatusCode();
    await using var content = await response.Content.ReadAsStreamAsync(timeout.Token);
    using var catalog = await JsonDocument.ParseAsync(content, cancellationToken: timeout.Token);

    if (!catalog.RootElement.GetProperty("id"u8).ValueEquals(model))
    {
        throw new InvalidOperationException("Use the canonical Claude model ID returned by the Anthropic Models API.");
    }

    builder.Services.AddUhpClaudeCode(path, claudeRoot, key, model);
    return true;
}
