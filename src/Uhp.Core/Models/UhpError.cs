using System.Text.Json;

using System.Text.Json.Serialization;
using Uhp.Core.Serialization;

namespace Uhp.Core;

[JsonConverter(typeof(UhpErrorJsonConverter))]
public sealed record UhpError(string Type, string Code, string Message)
{
    public string? Param { get; init; }
    public JsonElement? Detail { get; init; }
    public JsonElement Raw { get; internal init; }
}
