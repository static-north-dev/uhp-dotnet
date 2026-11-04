using System.Text.Json;

namespace Uhp.Core;

public abstract record UhpEvent(string Type, long SequenceNumber)
{
    public JsonElement Raw { get; internal init; }
}
