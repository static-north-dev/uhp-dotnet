using System.Text.Json;

using System.Text.Json.Serialization;
using Uhp.Core.Serialization;

namespace Uhp.Core;

[JsonConverter(typeof(UhpSessionCancelResultJsonConverter))]
public sealed record UhpSessionCancelResult(string Id, string Status)
{
    public JsonElement Raw { get; internal init; }
}
