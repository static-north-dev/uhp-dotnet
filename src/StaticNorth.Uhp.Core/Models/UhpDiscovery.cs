using System.Text.Json;

using System.Text.Json.Serialization;
using StaticNorth.Uhp.Core.Serialization;

namespace StaticNorth.Uhp.Core;

[JsonConverter(typeof(UhpDiscoveryJsonConverter))]
public sealed record UhpDiscovery(IReadOnlyList<string> Versions, string DefaultVersion, string ConformanceClass, UhpCapabilities Capabilities)
{
    public string? ImplementationName { get; init; }
    public string? ImplementationVersion { get; init; }
    public JsonElement Raw { get; internal init; }
}
