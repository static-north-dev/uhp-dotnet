using System.Text.Json;

namespace StaticNorth.Uhp.Core;

public sealed record UhpUnknownEvent(string EventType, long Sequence, JsonElement Data) : UhpEvent(EventType, Sequence);
