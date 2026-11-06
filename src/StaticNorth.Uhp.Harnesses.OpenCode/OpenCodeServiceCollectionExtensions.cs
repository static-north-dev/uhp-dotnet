using Microsoft.Extensions.DependencyInjection;
using StaticNorth.Uhp.Core.Providers;

namespace StaticNorth.Uhp.Harnesses.OpenCode;

public static class OpenCodeServiceCollectionExtensions
{
    /// <summary>Registers an OpenCode provider using a caller-owned HTTP client.</summary>
    public static IServiceCollection AddUhpOpenCode(this IServiceCollection services, HttpClient client, string stateRoot,
        string provider, string model)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        if (client.BaseAddress is null)
        {
            throw new ArgumentException("The OpenCode HTTP client requires a base address.", nameof(client));
        }

        return services.AddSingleton<IUhpHarnessProvider>(_ => new OpenCodeRuntime(new OpenCodeConnection(client), stateRoot, provider, model));
    }
}
