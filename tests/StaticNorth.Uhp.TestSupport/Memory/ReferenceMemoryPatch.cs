using System.Text.Json;

namespace StaticNorth.Uhp.TestSupport.Memory;

/// <summary>Supplied fields replace their previous values; null fields retain them.</summary>
public sealed record ReferenceMemoryPatch
{
    public string? Title { get; init; }
    public JsonElement? Content { get; init; }
    public Dictionary<string, JsonElement>? Attributes { get; init; }
    public IReadOnlyList<JsonElement>? References { get; init; }
}
