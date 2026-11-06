using System.Text.Json;

namespace StaticNorth.Uhp.Core.Providers;

/// <summary>Memory access scoped by the host to the current principal and harness.</summary>
public interface IUhpHarnessMemory
{
    bool CanWrite { get; }
    Task<string> PrimeAsync(CancellationToken ct);
    Task<string> InvokeAsync(string tool, JsonElement arguments, CancellationToken ct);
}
