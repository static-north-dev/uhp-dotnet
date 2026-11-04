using System.Text.Json;

namespace Uhp.Core;

public sealed record UhpModel(string Id, bool Available)
{
    public string? Label { get; init; }
    public string? Backend { get; init; }
    public bool? IsDefault { get; init; }
    public JsonElement Raw { get; internal init; }
}
