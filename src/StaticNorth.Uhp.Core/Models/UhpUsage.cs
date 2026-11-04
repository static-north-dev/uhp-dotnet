using System.Text.Json;

namespace StaticNorth.Uhp.Core;

public sealed record UhpUsage
{
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public long? CacheReadTokens { get; init; }
    public long? CacheWriteTokens { get; init; }
    public JsonElement Raw { get; internal init; }
}
