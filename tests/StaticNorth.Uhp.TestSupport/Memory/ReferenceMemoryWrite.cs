using System.Text.Json;

namespace StaticNorth.Uhp.TestSupport.Memory;

public sealed record ReferenceMemoryWrite(JsonElement Content)
{
    public string Type { get; init; } = "note";
    public string? Title { get; init; }
    public Dictionary<string, JsonElement>? Attributes { get; init; }
    public IReadOnlyList<JsonElement>? References { get; init; }
}
