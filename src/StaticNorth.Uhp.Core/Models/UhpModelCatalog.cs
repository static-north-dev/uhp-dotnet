using System.Text.Json;

using System.Text.Json.Serialization;
using StaticNorth.Uhp.Core.Serialization;

namespace StaticNorth.Uhp.Core;

[JsonConverter(typeof(UhpModelCatalogJsonConverter))]
public sealed record UhpModelCatalog(IReadOnlyDictionary<string, UhpModelBackend> Backends)
{
    public JsonElement Raw { get; internal init; }
}
