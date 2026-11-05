using System.Text.Json;

using System.Text.Json.Serialization;
using StaticNorth.Uhp.Core.Serialization;

namespace StaticNorth.Uhp.Core;

[JsonConverter(typeof(UhpSessionCancelResultJsonConverter))]
public sealed record UhpSessionCancelResult(string Id, string Status)
{
    public JsonElement Raw { get; internal init; }
}
