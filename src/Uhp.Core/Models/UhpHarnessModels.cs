using System.Text.Json;

using System.Text.Json.Serialization;
using Uhp.Core.Serialization;

namespace Uhp.Core;

[JsonConverter(typeof(UhpHarnessModelsJsonConverter))]
public sealed record UhpHarnessModels(IReadOnlyList<UhpModel> Models)
{
    public string? HarnessId { get; init; }
    public string? Backend { get; init; }
    public string? Default { get; init; }
    public string? Fallback { get; init; }
    public JsonElement Raw { get; internal init; }
}
