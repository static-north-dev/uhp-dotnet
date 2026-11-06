using Microsoft.Extensions.DependencyInjection;
using StaticNorth.Uhp.Core.Providers;

namespace StaticNorth.Uhp.Harnesses.ClaudeCode;

public static class ClaudeCodeServiceCollectionExtensions
{
    public static IServiceCollection AddUhpClaudeCode(this IServiceCollection services, string executablePath, string stateRoot,
        string? apiKey, string model, string? subscriptionProfileDirectory = null, string? cliConfigurationDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(subscriptionProfileDirectory))
        {
            throw new ArgumentException("Claude Code requires an API key or a signed-in subscription profile.", nameof(apiKey));
        }

        return services.AddSingleton<IUhpHarnessProvider>(_ => new ClaudeCodeRuntime(executablePath, stateRoot, apiKey, model,
            subscriptionProfileDirectory, cliConfigurationDirectory));
    }
}
