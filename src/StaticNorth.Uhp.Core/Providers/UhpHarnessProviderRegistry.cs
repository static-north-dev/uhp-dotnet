using System.Collections.Frozen;

namespace StaticNorth.Uhp.Core.Providers;

public sealed class UhpHarnessProviderRegistry(IEnumerable<IUhpHarnessProvider> providers)
{
    public FrozenDictionary<string, IUhpHarnessProvider> ByBase { get; } = providers
        .ToDictionary(static provider => provider.Base, StringComparer.Ordinal)
        .ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Initializes the registered runtimes before the host starts accepting requests.</summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        foreach (var provider in ByBase.Values)
        {
            ct.ThrowIfCancellationRequested();
            await provider
                .InitializeAsync(ct)
                .ConfigureAwait(false);
        }
    }
}
