using System.Text.Json;
using System.Text.Json.Serialization;
using StaticNorth.Uhp.Core.Serialization;

namespace StaticNorth.Uhp.Core;

[JsonConverter(typeof(UhpEventJsonConverter))]
public abstract record UhpEvent(string Type, long SequenceNumber)
{
    public JsonElement Raw { get; internal init; }
}
