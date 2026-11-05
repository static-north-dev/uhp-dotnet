using System.Collections.Frozen;
using System.Text.Json;

namespace StaticNorth.Uhp.TestSupport.Memory;

/// <summary>An owned immutable snapshot for the reference provider, not an HTTP response DTO.</summary>
public sealed record ReferenceMemoryRecord(
    string Id,
    string MemoryId,
    string Type,
    string? Title,
    IReadOnlyList<JsonElement> Content,
    FrozenDictionary<string, JsonElement> Attributes,
    IReadOnlyList<JsonElement> References,
    string WrittenBy,
    DateTimeOffset WrittenAt,
    long Version = 1,
    string Status = ReferenceMemoryValues.Active,
    long? Supersedes = null)
{
    public string Trust => ReferenceMemoryValues.Untrusted;
}
