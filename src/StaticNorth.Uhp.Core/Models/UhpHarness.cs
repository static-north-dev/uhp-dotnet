using System.Text.Json;

using System.Text.Json.Serialization;
using StaticNorth.Uhp.Core.Serialization;

namespace StaticNorth.Uhp.Core;

[JsonConverter(typeof(UhpHarnessJsonConverter))]
public sealed record UhpHarness(string Id, string Name, string Base)
{
    public string? BaseLabel { get; init; }
    public string? DefaultModel { get; init; }
    public string? SystemPrompt { get; init; }
    public long? MaxStep { get; init; }
    public long? TimeoutSeconds { get; init; }
    public long? CreatedAt { get; init; }
    public JsonElement Raw { get; internal init; }
}
