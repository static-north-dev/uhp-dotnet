using Microsoft.Extensions.DependencyInjection;

namespace StaticNorth.Uhp.Server;

internal static class UhpFeatureBackends
{
    public static void Validate(IServiceProvider services, UhpServerOptions options)
    {
        Require<IUhpSessionBackend>(services, options.Capabilities["session_listing"]);
        Require<IUhpFileBackend>(services, options.Capabilities["files_input"] || options.Capabilities["files_output"]);
        Require<IUhpHarnessManagementBackend>(services, options.Capabilities["harness_management"] || options.Capabilities["harness_idempotency"]);
        Require<IUhpSharingBackend>(services, options.Capabilities["session_sharing"]);
        Require<IUhpSharedViewBackend>(services, options.Capabilities["session_sharing"]);
        Require<IUhpPluginBackend>(services, options.Capabilities["plugins"]);
        Require<IUhpEnvironmentBackend>(services, options.Capabilities["environments"]);
        Require<IUhpMemoryBackend>(services, options.Capabilities["memories"]);
    }

    private static void Require<T>(IServiceProvider services, bool enabled) where T : class
    {
        if (enabled && services.GetService<T>() is null)
        {
            throw new InvalidOperationException("An advertised capability requires its backend: " + typeof(T).Name + ".");
        }
    }
}
