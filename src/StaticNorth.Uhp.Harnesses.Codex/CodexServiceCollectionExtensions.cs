using Microsoft.Extensions.DependencyInjection;
using StaticNorth.Uhp.Core.Providers;

namespace StaticNorth.Uhp.Harnesses.Codex;

public static class CodexServiceCollectionExtensions
{
    public static IServiceCollection AddUhpCodex(this IServiceCollection services, string stateRoot, string authenticationHome,
        string? executablePath = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationHome);
        return services.AddSingleton<IUhpHarnessProvider>(_ => new CodexRuntime(stateRoot, authenticationHome, executablePath));
    }
}
