using System.Text.Json;

using System.Text.Json.Serialization;
using Uhp.Core.Serialization;

namespace Uhp.Core;

[JsonConverter(typeof(UhpModelCatalogJsonConverter))]
public sealed record UhpModelCatalog(IReadOnlyDictionary<string, UhpModelBackend> Backends)
{
    public JsonElement Raw { get; internal init; }
}
